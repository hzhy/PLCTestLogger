# PLC 测试结果记录器 — 设计文档

日期：2026-11-07

## 目的

读取汇川 Easy521-0808TP PLC 的 D200–D230 寄存器，在 D230 由 0 变 1 时把一次测试的结果
（含语义化的常量选项）追加写入按月、按设备分文件的 Excel 中；并提供设备列表、实时数据和
按天查询历史记录的桌面界面。

## 目标

- 支持多台设备（当前 1 台，可在界面上新增/删除），每台设备独立轮询、独立记录。
- 寄存器含义、常量选项、列顺序、单位与倍率全部放在配置文件中，改配置不改代码。
- 保存触发：D230 上升沿（0→1）触发一次保存，1→0 不触发；连续为 1 不重复触发。
- Excel 文件：`<exe所在目录>/logger/<设备编号>/<yyyy-MM>.xlsx`，一个月一个文件，表头与示例一致，
  第一列为保存时间戳。
- 界面：设备列表（设备编号、IP、连接状态）、所选设备的实时寄存器值（语义化）、查询窗口
  （选设备 + 选日期，列出该天记录）。

## 非目标

- 不做 PLC 写操作（可选配置项 `resetTriggerAfterSave`，默认关闭，开启后保存成功后把 D230 写 0）。
- 不做数据库；不做远程/网页访问。

## 技术选择（我的决定）

- C# / .NET 8 / WPF（Windows 桌面，可发布为单文件 exe，"exe 旁边的 logger 文件夹"自然成立）。
- Modbus TCP 库：NModbus（`NModbus` 4.x 包）。Easy521 的 D 寄存器映射为保持寄存器，
  D 的起始 Modbus 地址可配置（`dRegisterBase`，默认 0，即 D200 → 地址 200）。
- Excel：ClosedXML。
- 配置：JSON，两份文件放在 exe 旁边：
  - `config/registers.json`：寄存器定义（名称、类型、常量选项、倍率、单位、是否输出到 Excel 及顺序）。
  - `config/devices.json`：设备列表（设备编号、IP、端口、UnitId、轮询周期），界面新增设备时写回。
- 测试：xUnit，覆盖纯逻辑（配置解析、数值→语义转换、上升沿检测、Excel 行写入/读取）。
  Modbus 通信通过接口抽象，便于用假客户端测试轮询器。

## 寄存器配置模型

```json
{
  "dRegisterBase": 0,
  "startRegister": 200,
  "count": 31,
  "triggerRegister": 230,
  "resetTriggerAfterSave": false,
  "registers": [
    { "address": 200, "name": "产品编号", "type": "enum",
      "options": { "1": "JX321干式", "2": "JX362干湿式", ... } },
    { "address": 203, "name": "接口数量", "type": "enum",
      "options": { "1": "1接口", ... "12": "12接口" } },
    { "address": 204, "name": "测试方法", "type": "enum",
      "options": { "1": "压力20PsiG", ... },
      "resultUnits": { "1": "PsiG", ..., "11": "ml/min" },
      "resultScales": { "1": 0.01, ..., "11": 0.1 } },
    { "address": 208, "name": "电机转速", "type": "number", "scale": 1, "unit": "rpm" },
    { "address": 210, "name": "温度", "type": "number", "scale": 0.1, "unit": "℃", "decimals": 1 },
    { "address": 212, "name": "接口1测试结果", "type": "number", "unitFrom": 204, "scale": 0.01, "decimals": 2 },
    ...
  ],
  "excelColumns": [200, 202, 203, 204, 208, 210, 209, 212, 213, ..., 223]
}
```

- `type`：`enum`（常量：数值→选项文本；无匹配时输出 `K<值>`）、`number`（数值 × scale，保留 decimals 位，附 unit）、
  `raw`（原始整数）。
- `unitFrom`：结果列的单位/倍率由另一寄存器（测试方法）的当前选项决定（压力→PsiG、流量→ml/min）。
- 16 位寄存器按无符号整数读取；`signed: true` 可按有符号解释。
- 示例图中的倍率（温度 0.1、压力 0.01 等）是我的猜测，已写入配置供现场校正。

## 运行时行为

- 每台设备一个后台轮询任务：每 `pollIntervalMs`（默认 200ms）读一次 D200–D230（一次 Modbus 读 31 个寄存器）。
- 连接失败自动重连（退避 1s→5s），状态在设备列表显示：未连接 / 已连接 / 重连中。
- 上升沿检测基于上一次成功读取的 D230 值；程序启动时若 D230 已为 1，不触发（避免重复记录）。
- 保存：根据配置把寄存器快照转换为一行，追加到当月 xlsx；文件不存在时创建并写表头；
  写入在单独的锁内进行，Excel 被占用（打开中）时重试 3 次后记录到 `logger/<设备>/failed.log`。
- 查询窗口：选择设备和日期，读取该月文件，按"保存时间"列前缀过滤，表格显示。

## 留给实现者

- WPF 界面样式；MVVM 层次（CommunityToolkit.Mvvm）。
- 日志：简单的滚动文本日志文件 `logs/app-yyyyMMdd.log`。
