# 实验数据追溯系统（PlcLogger）

读取汇川 Easy521-0808TP PLC 的 D200–D240 寄存器，当“测试完成标志”寄存器（默认 D240）由 0 变 1 时，
把一次测试结果（常量型寄存器转换为语义化文本）追加写入 Excel；按设备、按月分文件保存。
界面提供设备列表（编号 / IP / 连接状态）、所选设备的实时寄存器值、按天查询历史记录。

## 运行环境

- Windows 10/11，.NET 8（发布为自包含单文件 exe 时无需安装运行时）
- PLC 开启 Modbus TCP（默认端口 502）

## 目录结构（运行时，位于 exe 旁边）

```
PlcLogger.exe
config/
  registers.json      寄存器含义、常量选项、Excel 列顺序、触发寄存器
  devices.json        设备列表（界面新增/编辑/删除/启停设备时自动写回）
logger/
  <设备编号>/
    2026-11.xlsx              一个月一个文件，首列为保存时间
    2026-11.pending.tsv       xlsx 被 Excel 打开占用时的暂存记录，下次写入成功后自动合并进 xlsx 并删除
    2026-11_v2.xlsx           当月中途修改了 excelColumns / 列名（表头变化）后另起的文件
    2026-11.corrupt-*.xlsx    检测到损坏的文件会被移到一旁重新开始（不会覆盖）
logs/
  app-yyyyMMdd.log    程序运行日志
```

## 构建与发布

```powershell
dotnet test                                   # 运行单元测试
dotnet build -c Release                       # 编译
dotnet publish src/PlcTestLogger.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

发布产物在 `publish/` 下（主程序 `PlcLogger.exe`），连同 `config/` 文件夹一起复制到目标电脑即可运行。

用户操作说明（含截图）见 [docs/manual/README.md](docs/manual/README.md)；两台模拟设备产生的 Excel 样例在 `samples/logger/`。

## 配置说明

### config/devices.json

```json
{
  "devices": [
    { "id": "DEV01", "ip": "192.168.1.10", "port": 502, "unitId": 1, "pollIntervalMs": 200 }
  ]
}
```

- `id`：设备编号，也是 `logger/` 下子文件夹名。
- `pollIntervalMs`：轮询周期（毫秒）。
- `enabled`：是否启用（默认 true）。关闭后不轮询、不保存；界面设备列表第一列的开关对应此字段，切换后自动写回。

### config/registers.json

| 字段 | 说明 |
|------|------|
| `dRegisterBase` | D0 对应的 Modbus 保持寄存器地址。汇川 Easy 系列 D 元件默认从 0 开始，即 D200 → 地址 200。若实际映射不同在此修改。 |
| `startRegister` / `count` | 一次连续读取的起始 D 编号与个数（默认 D200 起 41 个，即 D200–D240）。 |
| `wordOrder` | 32 位数据的字序，本项目 PLC 为 `highLow`（高字在前：D212 高 16 位、D213 低 16 位）。 |
| 寄存器的 `dataType` | `uint16`（默认）/`int16`/`uint32`/`int32`/`float32`/`auto32`；32 位占两个地址。`auto32` 按内容自动判断整数还是浮点（测试方法 D204&D205、接口结果 D212–D235 使用），判定为浮点时直接取工程值、不乘 `scale`。 |
| `triggerRegister` | “测试完成、可以保存”的标志寄存器编号（默认 230）。由 0 变 1 时触发一次保存；保持为 1 不重复保存；程序启动时若已为 1 不触发。 |
| `resetTriggerAfterSave` | 为 `true` 时保存成功后把触发寄存器写回 0（默认 `false`，不向 PLC 写入）。 |
| `wordOrder` | 32 位数据的字序：`lowHigh` 低字在前（汇川默认），`highLow` 高字在前。 |
| `registers[]` | 每个寄存器的定义，见下。 |
| `excelColumns` | 输出到 Excel 的寄存器编号及列顺序（“保存时间”固定为第一列）。 |

寄存器定义 `registers[]` 的字段：

| 字段 | 适用 type | 说明 |
|------|-----------|------|
| `address` | 全部 | D 编号 |
| `name` | 全部 | 列名 / 显示名 |
| `type` | 全部 | `enum` 常量选项；`number` 数值；`raw` 原始整数 |
| `options` | enum | `{"寄存器值": "语义文本"}`，例如 `"1": "JX321干式"`。未匹配的值输出 `K<值>` |
| `scale` / `decimals` / `unit` | number | 显示值 = 原始值 × scale，保留 decimals 位小数，附单位 |
| `unitFrom` | number | 单位 / 倍率 / 小数位由另一寄存器（enum）的当前选项决定，例如接口测试结果随“测试方法”切换 PsiG 或 ml/min |
| `resultUnits` / `resultScales` / `resultDecimals` | enum | 配合 `unitFrom`：本寄存器取某选项时，引用它的结果列使用的单位 / 倍率 / 小数位 |
| `dataType` | 全部 | `uint16`（默认）、`int16`、`uint32`、`int32`、`float32`。32 位类型占本地址和下一地址两个寄存器（如 D212+D213），下一地址不要再定义 |
| `wordOrder` | 32 位 | 覆盖全局字序 |
| `enabled` | 全部 | `false` = 备用/未使用：不在实时界面显示，不能作为 Excel 列。读取不受影响（程序总是整块连续读取） |
| `signed` | 16 位 | 等价于 `dataType: "int16"`，兼容旧配置 |

> 示例配置中的倍率（温度 ×0.1、压力 ×0.01、流量 ×0.1）是根据样例 Excel 推测的，请按 PLC 实际存储方式校正。

### 16 位 / 32 位数据

Modbus 保持寄存器每个都是 16 位。PLC 侧如果用 32 位整数（DINT）或浮点数（REAL）存放，会占用两个连续的 D 寄存器，
此时把该寄存器的 `dataType` 设为 `int32` / `uint32` / `float32`，程序会自动把两个字拼成一个值，例如：

```json
{ "address": 212, "name": "接口1测试结果", "type": "number", "dataType": "float32", "decimals": 2, "unitFrom": 204 }
```

上例占用 D212 和 D213，接口 2 的结果应从 D214 开始。字序不确定时先用默认 `lowHigh`，如果读出的值明显异常再改为 `highLow`。

### 关于触发信号

保存只在触发寄存器“由 0 变为非 0”的那一刻执行一次。信号保持为 1 期间（例如 30 秒）不会重复保存，
必须回到 0 后再变 1 才会触发下一次。程序启动或断线重连时若信号已经是 1，也不会触发。

## 界面

- **设备列表**：设备编号、IP、端口、连接状态（已连接 / 重连中 / 未连接）、本次运行保存条数、最近保存时间、最近错误。
- **实时数据**：所选设备 D200–D240 的原始值与语义化内容，随轮询实时刷新。32 位寄存器的原始值显示为两个字（高字在前）。
- **新增设备 / 编辑设备 / 删除设备**：立即生效并写回 `config/devices.json`。编辑前必须先停用该设备；设备编号不可修改。
- **查询记录**：选择设备和日期，列出当天全部保存记录（含 _v2 等版本文件和暂存记录，可直接复制到 Excel）。

## 保存机制说明

- 写入 xlsx 时先写临时文件再原子替换，掉电或进程被杀不会损坏已有记录。
- 当月 xlsx 被 Excel 打开时无法写入：程序会重试，仍失败则把该条记录暂存到 `.pending.tsv`，
  界面“事件”区会提示；关闭 Excel 后下一条记录写入时自动合并。暂存记录在查询窗口中同样可见。
- `devices.json` 启动时校验（编号唯一且不区分大小写、IP 合法、端口 1~65535、轮询周期 ≥ 200ms），不合法会提示并退出。
- **打开记录文件夹**：打开 `logger/` 目录。

## 本机联调

`tools/modbus_sim.py` 是一个极简 Modbus TCP 从站模拟器，可在没有 PLC 时验证整条链路：

```powershell
py -3.12 tools/modbus_sim.py 5020
```

把 `devices.json` 中设备的 IP 改为 `127.0.0.1`、端口 `5020`，启动程序，模拟器会按脚本把 D240 置 1，
`logger/DEV01/` 下应出现当月 xlsx。
