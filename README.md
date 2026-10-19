# 智能水箱上位机（WPF 现代化重构版）

> **项目状态**：已完成全部阶段开发并全绿通过定稿验收，正式归档发布 **`tag v1.0-wpf`**。
>
> 源项目基线：[.NET 8 WinForms 版本（已归档）](../Intelligent-water-storage-system/)

---

## 1. 项目简介与设计亮点

本项目是基于 .NET 8 与 WPF 的现代化工业级水箱监控上位机系统。通过自研的高可靠 Modbus TCP 通信层，实时采集并控制蓄水池、3 台工业水泵、主控阀门与分路阀门组，具备深色工业 HMI 视觉呈现、毫秒级响应、零回写风暴防护以及全生命周期报警追踪能力。

### 核心特性与设计指标

1. **ISA-101 深色工业标准 HMI 设计**
   - 采用深蓝灰底色（`#0F172A`）与层级化卡片布局（`#1E293B`），杜绝视觉刺眼与强眩光。
   - 告别裸标签与散落数值：蓄水池 2.5D 液位计、270° 环形仪表、工业拟物蜗壳叶轮水泵、管道水流粒子动画。
   - **自适应 Viewbox 矢量等比缩放**：解决高 DPI 缩放（如 125%/150%）及小屏幕下标题栏溢出不可达问题（P17 彻底闭环），窗口任意缩放无变形。

2. **零回写风暴与三层防护体系（TC-V1~V9）**
   - **层 1（手势拦截）**：所有开关控件采用单向视觉呈现（`OneWay` 绑 `SwitchStateStore`），切断 WPF 双向回写回路。
   - **层 2（协议隔离）**：PLC 轮询数据仅经由 `InitialSync` 首包同步视觉状态，不触发下发命令；10,000 轮轮询下 0x05 写计数严格为 0。
   - **层 3（自翻转回滚）**：控制手势必须弹出二度确认对话框（默认焦点为"取消"，回车安全），写入失败或超时时自动乐观回滚。

3. **工业级 Fail-Fast 数据契约（E01~E09 & A01~A09）**
   - 严格遵循"配置有误宁可不开机"的工业铁律：36 个数据点配置（`TagTable.csv`）在启动时执行 9 类语法与逻辑校验（缺失列、重名、越界、类型错、非偶对齐、零比例尺等），发现异常即刻弹出全量诊断报告并拒绝启动。
   - 运行时 XAML 绑定与标签表执行交叉核对（E08 控制入口缺失、E09 标签不存在）。

4. **ISA-18.2 四态生命周期报警与持久化**
   - 完整支持【报警中·未确认】、【报警中·已确认】、【已恢复·未确认】、【已关闭】四态状态机。
   - 多点位协同报警描红（压力浪涌全链路亮红关闭 P4）；SQLite 数据库持久化落库（`history.db`），掉电重启历史可溯。

5. **工业劣化适应与高可靠通信**
   - 内置指数退避重连机制（1s → 2s → 4s → 8s → 16s → 30s 封顶）。
   - 针对 TCP 分片（`frag`）、丢包（`drop`）、通信静默（`mute`）全面自愈与容错，保证 0 错帧、0 死锁。

---

## 2. 软件架构

```
water-system-wpf/
├── src/
│   ├── WaterSystem.Core/             # 纯净 .NET 8 类库（零 UI 依赖，可跨平台复用）
│   │   ├── Communication/            # ModbusFrame 帧编解码、CommStatistics
│   │   ├── Models/                   # TagAddress、AlarmModels、AlarmConfig
│   │   ├── Services/                 # ModbusClient、DataPollingService、ControlService、AlarmCenter
│   │   └── Utils/                    # TagTableLoader、ValueFormatter、GroupPlanner、RegisterCodec
│   │
│   └── WaterSystem.Wpf/              # WPF 工业桌面程序（net8.0-windows）
│       ├── Binding/                  # TagBindingRegistry、TagBinding
│       ├── Controls/                 # LevelTank、RingGauge、PumpGraphic、PipeLine、WaterTrendChart 等自绘控件
│       ├── Themes/                   # Dark.xaml（ISA-101 令牌字典）、HandyControl 3.5.1
│       ├── ViewModels/               # MainViewModel、ControlViewModel、AlarmPanelViewModel
│       └── Views/                    # MainWindow、LogViewerWindow、FailFastWindow、ConfirmDialog
│
├── tests/
│   ├── WaterSystem.Core.Tests/       # 129 项 Core 单元测试（帧协议/数据契约/编解码/状态机）
│   └── WaterSystem.Wpf.Tests/        # 95 项 WPF 单元与集成测试（回写防护/渲染断言/回归矩阵/浸泡）
│
├── tools/
│   ├── ModbusSlaveSim/               # 进程级/独立 Modbus TCP 从站模拟器（支持劣化注入）
│   └── TestRunner/                   # 协议级端到端测试套件（82 项全链路通过）
│
└── docs/                             # 架构方案、实施细则、详细设计书与各里程碑验收记录
```

---

## 3. UI 缺陷修复与人因优化汇总（P1~P18）

| 缺陷编号 | 原始 WinForms 缺陷表象 | WPF 重构解决方案 |
|---|---|---|
| **P1** | 浮点数全精度直出（如 3.59625473 m） | `ValueFormatter` 按 Scale 有效位严格推导定宽格式（2.55/0.35/26.5） |
| **P2** | 参数名与状态中英杂糅 | 统一人机工学中文术语与工业语义标准 |
| **P3** | 水泵/阀门状态无图仅文字 | 自绘工业蜗壳+叶轮拟物水泵，运行平滑旋转，故障状态醒目展示 |
| **P4** | 压力超限仅日志提示，画面无感知 | 主管压力 >10.0Mpa 时全管廊描红，实时卡、顶栏徽标、管路三处同步高亮 |
| **P5/P6** | 状态灯仅变色不变字、文字语义歧义 | 徽章文字随状态动态切换（● 运行 / ○ 停止 / ▲ 故障），色弱无障碍友好 |
| **P7** | 水池与水箱缺少直观水位指示 | 自绘 2.5D 波浪液位水箱（LevelTank）+ 270° 刻度环形仪表（RingGauge） |
| **P8** | 控制按钮无操作防误触提示 | `ConfirmDialog` 二度确认对话框，默认焦点为"取消"，回车安全 |
| **P9** | 控制指令发出后无法识别响应状态 | 按钮增加 busy 状态抑制防重入，写失败自动回滚乐观状态并记入日志 |
| **P10** | 水位趋势图无刻度、阈值与单位 | `WaterTrendChart` 自绘 Polyline，标注明确 Y 轴单位 "m" 及红虚线报警阈值 |
| **P11** | 日志窗口滚动行高不均、截断换行 | DataGrid 严格定高 22px，表头 26px，级别单行 64px 独立分列显示 |
| **P12** | 报警只有最新一条，无法回溯确认 | 顶栏集成 `⚠ 未确认` 实时徽标，实时报警卡提供逐项/全部确认闭环 |
| **P13** | 通信状态在折叠区隐蔽不可见 | 顶栏常驻三态通信状态 Chip（已连接/离线/通信中断），全屏全局可见 |
| **P14** | 窗口直接关闭缺少保护 | 捕获窗口关闭事件，弹出工业安全退出确认对话框，默认聚焦"取消" |
| **P15** | 管道流动状态静止无水流感知 | 11 段管道根据泵和阀门开闭状态计算水流拓扑，支持双向水流虚线平滑流动动画 |
| **P16** | 误按回车直接触发默认操作 | 全局弹窗取消默认 Enter 提交，统一默认绑定 `IsCancel=true` 安全退出 |
| **P17** | 高 DPI 屏幕（125%/150%）标题栏溢出不可达 | 外层包裹 Viewbox 矢量等比缩放，窗口自适应工作区边界，关闭钮永远可见 |
| **P18** | 缺少陌生人可用性帮助说明 | 顶栏 ⓘ 按钮弹出 `SystemInfoWindow`，即时解答系统用途、点位契约与通信协议 |

---

## 4. 里程碑交付台账

| 里程碑 | 版本标签 | 交付核心内容 | 验证与测试状态 |
|---|---|---|:---:|
| **M0** | `v0.0-baseline` | 建立仓库基线、git 拓扑规范、.gitignore、文档体系入库 | ✅ 验证通过 |
| **M1** | `v0.1-core` | Core 类库纯净化抽取，解耦 UI 依赖，ModbusClient/TagTableLoader 等 | 110/110 单测通过 |
| **M2** | `v0.2-shell` | WPF 启动七步链、深色令牌字典、Viewbox 自适应外壳、TagBinding 注册表 | 单测全绿 |
| **M3** | `v0.3-monitor` | 监视链路全面打通、ParaBox 定宽、状态徽章、看门狗超时 | 129 Core + 31 WPF |
| **M4** | `v0.4-graphics` | 过程工艺总览图自绘重构（LevelTank、RingGauge、PumpGraphic、PipeLine） | 离屏渲染比对通过 |
| **M5** | `v0.5-control` | 控制决策链路、ConfirmDialog 确认、三层回写防护（TC-V1~V8）、回写风暴专项 | 零写回验证通过 |
| **M6** | `v0.6-alarm` | ISA-18.2 报警中心、SQLite 持久化、LogRowStyler 行着色、F12 全量日志窗口 | 闭环通过 |
| **M7** | `v0.7-trend` | 水位趋势自绘（300 点滚动截断、Y 轴单位 "m"、红虚线阈值） | 单元测试通过 |
| **M8** | `v0.8-persist` | AppStateStore 持久化通信配置与开关状态，坏配置 fail-soft 降级 | 往返测试通过 |
| **M9** | `v1.0-wpf` | **全量回归矩阵（§5.2）、5 大劣化演练（frag/mute/surge/fault/drop）、浸泡稳定性** | **224 单测 + 82 端到端全绿** |

---

## 5. 快速上手与运行验证

### 环境要求
- Windows 10 / Windows 11 x64
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (>= 8.0.300)

### 运行程序
```powershell
# 1. 构建整个解决方案
dotnet build

# 2. 启动 WPF 上位机主程序
dotnet run --project src/WaterSystem.Wpf

# 3. 在上位机主界面中点击【模拟】按钮，即可一键启动内置从站进行交互演练
```

### 运行全量自动化测试
```powershell
# 运行全部 224 项单元测试与集成测试
dotnet test

# 运行 82 项协议级端到端全链路测试套件（拉起模拟器自动演习）
dotnet run --project tools/TestRunner
```

---

## 6. 劣化演练与实证截图

### 压力浪涌（Surge 演练）
> 模拟器注入 `surge on`（压力升至 12.0 MPa），主管路描红、顶栏 `⚠ 未确认 1`、实时报警卡高亮。
![Surge 压力浪涌实证](docs/drill-surge.png)

### 水泵电气故障（Fault 演练）
> 2# 水泵电气故障注入，泵卡徽标转为醒目的红色 `▲ 故障`，报警四态流转。
![Fault 水泵故障实证](docs/drill-fault.png)

### 通信中断（Mute 演练）
> 通信静默超时，看门狗自动置为 `通信中断`，动画和仪表安全复位，防止过期读数误导操作员。
![Mute 通信静默实证](docs/drill-mute.png)

---

## 7. 许可证与开发规范
- 遵循严谨的工业软件工程标准与 Git 分支开发纪律（每任务独立 feature 分支，单测与验收全绿后 `--no-ff` 并入 master）。
- 详见完整验收台账：[docs/验收记录-M9.md](docs/验收记录-M9.md)。
