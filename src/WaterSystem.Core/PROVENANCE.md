# PROVENANCE（来源与变更记录）

| 项 | 内容 |
|---|---|
| 抽取源仓库 | `D:\wpf\one\Intelligent-water-storage-system`（WinForms 上位机，迁移期冻结只读） |
| 抽取源 commit | **`098df6d`**（master HEAD，工作树干净 = HEAD） |
| 抽取日期 | 2026-10-11 |
| 版本说明 | 源仓库标签有 v1.0~v1.3 共 4 个；**刻意不取 v1.0**——v1.0 后工程层仍有 17 个提交（含 d90914a 的 AlarmConfig.json 外置 + AlarmConfigTests 8 项等产品级特性），从 v1.0 复制会丢特性（迁移方案 §8-1） |

## 复制清单（26 个 .cs，零改动除注明外）

- `Services/`（9）：ModbusClient、ModbusFrame、DataPollingService、ControlService、ControlDecision、AlarmService、AlarmCenter、CommStatistics、**LogService（唯一补丁，见下）**
- `Model/`（5）：TagAddress、DataPoint、AlarmModels、AlarmConfig、CommunicationSet
- `Simulation/`（6）：DataStore、ModbusServer、ProtocolProcessor、ScenarioEngine、ModbusException、SimulatorOptions
- `Utils/`（6）：TagTableLoader、RegisterCodec、GroupPlanner、Utility、AppConstants、Excelhelper

## 唯一代码改动（1 处）

- `Services/LogService.cs`：`Application.StartupPath` → `AppContext.BaseDirectory`（语义等价：可执行文件目录；LogService 是工程层中唯一的 WinForms 类型引用）

## 不随迁（WinForms 专属，WPF 以新机制替代）

`Utils/ControlTagMap.cs`（被 XAML 绑定取代）、`Utils/Initializer.cs`（被 AppStateStore 取代）、`Utils/MessageHelper.cs`（WPF MessageBox）、`FrmMain.*`、`Program.cs`

## TestRunner 处置（迁移方案 §8-3）

源仓库 `tools/TestRunner` 为混合体：协议级端到端（`RunCommTests` 等，拉起 ModbusSlaveSim）**随迁本仓库**；WinForms 专项探针（DockOrderExperiment、E1 控件事件探针、WinForms 控件盘点，共 89 处 WinForms/Zhaoxi 引用）**不随迁**，留源仓库作对照。迁移实证：协议级回归 **PASS=88 / FAIL=0**（拉起模拟器全链路，2026-10-11）。

## 命名空间决定

保留源命名空间 `Intelligent_water_storage_system.*`——110 项测试的 using 零改动即可编译（沙箱已实证，迁移方案 §8-4）。

## 漂移纪律

Core 一旦抽取，语义改动必须测试护航并回写本文件；WinForms 仓库冻结不再演进。
