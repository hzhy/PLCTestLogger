# PLC 测试结果记录器 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 读取汇川 Easy521 PLC D200–D230，D230 上升沿时把语义化结果追加到按设备/按月的 Excel，并提供设备列表、实时值、按天查询的 WPF 界面。

**Architecture:** `PlcTestLogger.Core`（纯逻辑：配置模型、数值→语义转换、上升沿检测、设备轮询器、Excel 读写，Modbus 通过 `IRegisterReader` 抽象）；`PlcTestLogger.App`（WPF + CommunityToolkit.Mvvm，NModbus 实现 `IRegisterReader`，设备管理器，三个窗口）；`PlcTestLogger.Core.Tests`（xUnit）。

**Tech Stack:** .NET 8, WPF, CommunityToolkit.Mvvm, NModbus 3.x, ClosedXML, xUnit.

**Spec:** docs/superpowers/specs/2026-11-07-plc-test-logger-design.md

## Global Constraints

- 寄存器范围 D200–D230（31 个），触发寄存器 D230，均可配置。
- Excel 路径：`<AppContext.BaseDirectory>/logger/<设备编号>/<yyyy-MM>.xlsx`，首列“保存时间”，格式 `yyyy-MM-dd HH:mm:ss`。
- 配置文件：`config/registers.json`、`config/devices.json`，UTF-8 JSON。
- 不向 PLC 写入，除非 `resetTriggerAfterSave=true`。

## Review Focus

1. 枚举值在选项表中不存在 → 输出 `K<值>` 而不是异常（Task 2 测试）。
2. 程序启动时 D230 已为 1 → 不触发；随后 1→0→1 才触发（Task 3 测试）。
3. 读失败（异常）期间不得把“上次值”清零导致误触发；恢复后以恢复后的首个值作为基线（Task 3 测试）。
4. Excel 文件被用户打开占用 → 写入失败不得丢数据：重试后写 failed.log（Task 4 测试覆盖 writer 失败路径）。
5. 跨月：11 月 30 日 23:59 与 12 月 1 日 00:00 的记录落在不同文件（Task 4 测试）。

---

## 文件结构

```
PlcTestLogger.sln
src/PlcTestLogger.Core/
  Config/RegisterDefinition.cs   寄存器定义 + RegisterConfig 根对象
  Config/DeviceDefinition.cs     设备定义 + DevicesConfig
  Config/ConfigLoader.cs         JSON 读写
  Mapping/SnapshotFormatter.cs   ushort[] 快照 -> (列名, 文本) 列表
  Polling/IRegisterReader.cs     Modbus 抽象
  Polling/RisingEdgeDetector.cs
  Polling/DevicePoller.cs        轮询循环、状态、事件
  Storage/ExcelLogStore.cs       追加行 / 按日查询
src/PlcTestLogger.App/
  App.xaml(.cs)  MainWindow.xaml(.cs)  AddDeviceWindow.xaml(.cs)  SearchWindow.xaml(.cs)
  ViewModels/MainViewModel.cs  DeviceItemViewModel.cs  SearchViewModel.cs
  Services/NModbusRegisterReader.cs  DeviceManager.cs  AppPaths.cs
  config/registers.json  config/devices.json  (CopyToOutputDirectory)
tests/PlcTestLogger.Core.Tests/
  SnapshotFormatterTests.cs  RisingEdgeDetectorTests.cs  DevicePollerTests.cs  ExcelLogStoreTests.cs  ConfigLoaderTests.cs
```

### Task 1: 解决方案骨架 + 配置模型与加载

**Files:** Create sln、三个 csproj、`Config/*.cs`、`config/registers.json`、`config/devices.json`、`ConfigLoaderTests.cs`

**Interfaces (Produces):**
- `RegisterConfig { int DRegisterBase; int StartRegister; int Count; int TriggerRegister; bool ResetTriggerAfterSave; List<RegisterDefinition> Registers; List<int> ExcelColumns; }`
- `RegisterDefinition { int Address; string Name; string Type /*enum|number|raw*/; Dictionary<string,string>? Options; Dictionary<string,string>? ResultUnits; Dictionary<string,double>? ResultScales; double Scale=1; int Decimals=0; string Unit=""; int? UnitFrom; bool Signed; }`
- `DeviceDefinition { string Id; string Ip; int Port=502; byte UnitId=1; int PollIntervalMs=200; }`
- `DevicesConfig { List<DeviceDefinition> Devices; }`
- `ConfigLoader.LoadRegisters(path) / LoadDevices(path) / SaveDevices(path, cfg)`

- [ ] 测试：`LoadRegisters_ParsesSampleFile` 断言 Count==31, Registers 中 200 为 enum 且 Options["1"]=="JX321干式"；`SaveDevices_RoundTrips`。
- [ ] 实现；`dotnet test` 通过；commit。

### Task 2: SnapshotFormatter

**Produces:** `SnapshotFormatter(RegisterConfig cfg)`; `IReadOnlyList<(string Name, string Text)> Format(ushort[] snapshot)`（按 ExcelColumns 顺序）；`string FormatRegister(int address, ushort[] snapshot)`；`IReadOnlyList<string> Headers`。

- [ ] 测试：enum 命中 → 文本；未命中 → `K37`；number scale 0.1 decimals 1 unit ℃ → `36.5℃`；结果列 unitFrom 204：204=1 → `0.01PsiG`，204=11 → `0.2ml/min`；signed 读 65535 → -1。
- [ ] 实现、测试通过、commit。

### Task 3: RisingEdgeDetector + DevicePoller

**Produces:**
- `RisingEdgeDetector.Feed(ushort value) -> bool`（首个值仅做基线，不触发）；`Reset()`。
- `IRegisterReader { Task<ushort[]> ReadAsync(ushort startAddress, ushort count, CancellationToken ct); Task WriteAsync(ushort address, ushort value, CancellationToken ct); }`
- `IRegisterReaderFactory { IRegisterReader Create(DeviceDefinition d); }`
- `DevicePoller(DeviceDefinition dev, RegisterConfig cfg, IRegisterReaderFactory f)`; `Start()`, `StopAsync()`; `enum ConnectionState { Disconnected, Connected, Reconnecting }`; events `StateChanged(ConnectionState)`, `SnapshotUpdated(ushort[])`, `SaveTriggered(ushort[] snapshot, DateTime at)`; 异常后 `Reset()` 边沿检测并重连。

- [ ] 测试：边沿 0,1→触发一次；1,1,1→无；启动即 1 → 无，之后 0,1 → 触发；Feed 异常后 Reset，恢复首值 1 不触发。
- [ ] Poller 用假 reader 测试：序列 [..0..][..1..] 触发一次 SaveTriggered；reader 抛异常 → 状态 Reconnecting，恢复后 Connected。
- [ ] 实现、通过、commit。

### Task 4: ExcelLogStore

**Produces:** `ExcelLogStore(string loggerRoot)`; `string GetFilePath(string deviceId, DateTime at)`; `void Append(string deviceId, DateTime at, IReadOnlyList<string> headers, IReadOnlyList<string> cells)`（失败重试 3 次，之后写 `failed.log` 并抛 `ExcelWriteException`）；`List<string[]> QueryDay(string deviceId, DateOnly day)`（含表头行在首位）。

- [ ] 测试：新建文件含表头+1 行；二次 Append 为 2 行；跨月两个文件；QueryDay 过滤前缀；文件被独占占用时写 failed.log。
- [ ] 实现、通过、commit。

### Task 5: WPF 应用

- `NModbusRegisterReader`：`ModbusFactory().CreateMaster(TcpClient)`，`ReadHoldingRegistersAsync(unitId, start+dBase, count)`。
- `DeviceManager`：为每台设备建 `DevicePoller`，接 `SaveTriggered` → `SnapshotFormatter` → `ExcelLogStore.Append`；可选 reset 触发位；新增/删除设备时写回 devices.json。
- `MainWindow`：左侧设备 DataGrid（编号/IP/状态/操作），右侧所选设备实时寄存器表（名称/原始值/语义值），工具栏：新增设备、删除设备、查询、打开 logger 目录。
- `AddDeviceWindow`：编号、IP、端口、UnitId、轮询周期。
- `SearchWindow`：设备下拉 + DatePicker + 查询按钮 + DataGrid；导出按钮可选。
- [ ] `dotnet build` 零错误；`dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true` 成功；commit。

### Task 6: README

- [ ] 写 README.md：运行方式、配置文件说明、Modbus 地址说明、Excel 位置。commit。
