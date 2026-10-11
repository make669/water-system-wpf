# 智能水箱上位机 · WinForms → WPF 迁移实施方案

| 项 | 内容 |
|---|---|
| 文档版本 | v1.4 |
| 编写日期 | 2026-10-11 |
| 源工程 | `D:\wpf\one\Intelligent-water-storage-system`（.NET 8 WinForms，git tag `v1.0` 定稿基线） |
| 目标工程 | `D:\wpf\one\water-system-wpf`（.NET 8 WPF，当前为空壳模板） |
| 配套依据 | **《系统总体设计.md》（纲：分层/模块职责/数据流/质量体系——职责争议以它为准）**；源仓库《设计文档》《界面定稿设计》《验收标准》《AGENTS.md》（均已实读）；《UI人性化设计概要.md》（v2.0，风格定稿）；《WPF-UI详细设计书.md》（v1.2，含渲染实证与组件策略 D11）；《WPF实施细则.md》（v1.2，任务书） |
| 修订记录 | v1.4：UI 渲染实证（§8-8）+ 决策 D11 + 引入《系统总体设计》为纲；v1.3：git 纪律每任务一分支；v1.2：实施细则；v1.1：M4 人性化重构；v1.0：初版 |
| 使用方式 | 按 M0~M9 顺序执行；每阶段内逐任务按《WPF实施细则》§2 的 T 编号开分支开发，**验收达标才并入 master**；视觉与交互按《WPF-UI详细设计书》实施 |

> **总原则（用户定调）**：贴合实际使用的上位机设计；每一步都要有理可依，不想当然；用 git 进行项目开发管理。
> 本文所有"依据"均指向两仓库实际代码的文件与行为，无一来自想象。

---

## 0. 结论速览

1. **这不是"重写一个软件"，而是"UI 换壳 + 接线重造"**。源项目工程层（通信/数据契约/报警/控制/模拟器/测试，约 4500 行）与 UI 层天然分界，工程层经最小适配后整体复用，由 110 项单元测试护航（实测基线 110/110 全绿，见 §8）。
2. **UI 层无法移植，只能重造**：界面重度依赖 Zhaoxi WinForms 控件库（ParaTextBox×24、UPipe×14 等，见 §1.2），该库没有 WPF 版本；用 `WindowsFormsHost` 混排会触发 WPF airspace 限制（WinForms 控件永远浮在 WPF 内容之上、不能参与样式/变换），不可取。因此 UI 以 WPF 自绘控件 + MVVM 重造。**重造分两档（v1.1）**：安全行为语义（确认/回滚/fail-fast/空态诚实）逐条忠实平移；**视觉与布局按《UI人性化设计概要》重设计**——现状 UI 经评审存在 18 项人性化问题（P1~P18：数值未格式化、IP/Port 标签错位、报警时主画面无感、泵故障无灯位、过程图对象无标注等），用户已裁定 WPF 版不再像素级还原。
3. **最大的技术风险不是画界面，而是一条 WinForms 的"天然语义"会消失**：源项目依赖"编程赋值不触发控件事件"（E1 预检结论，FrmMain.cs:629-631）来避免 PLC 数据同步引发控制回写风暴。WPF 数据绑定没有这个天然语义，必须显式设计回写防护（§2.4），并配专项测试。
4. **数据契约零损迁移**：`TagTableLoader.CheckUiBindings` 的签名本来就收**字符串集合**而非控件树（TagTableLoader.cs:254-260），E08/E09 fail-fast 校验在 WPF 侧可以原样复用，只需换一个"绑定名收集器"（§2.5）。
5. **工期估计**：单人 14~20 个工作日（M0~M9，见 §7）。期间 WinForms v1.0 仓库**冻结只读**，作为行为对照基线，不修改一行代码。

---

## 1. 双项目现状盘点（实读证据）

### 1.1 源工程结构（Intelligent-water-storage-system）

```
Intelligent water storage system/    上位机主体（net8.0-windows + UseWindowsForms）
  FrmMain.cs                1231 行  全部 UI 行为（接线、控制、报警呈现、日志窗口）
  FrmMain.Designer.cs       2306 行  全部 UI 布局（用户 UI1.0 定稿，1492×1000 无边框）
  Program.cs                  28 行  STA 入口 + Application.Run
  Services/                           通信/轮询/控制/报警（纯逻辑，零 UI 依赖）
  Model/                              数据契约（TagAddress/DataPoint/AlarmModels/AlarmConfig）
  Utils/                              加载器/编解码/映射（部分 WinForms 耦合，见 1.2）
  Simulation/                         进程内 Modbus 从站模拟器（纯逻辑）
tests/IntelligentWaterStorage.Tests/  xUnit 110 项断言（实测 110/110；不创建控件实例，见其 csproj 注释）
tools/TestRunner/                     端到端集成回归（混合体：协议级用例可迁移 + WinForms 专项探针不可迁移，见 §8-3）
tools/ModbusSlaveSim/                 控制台从站模拟器（fault/surge/mute/frag/set/scenario）
configFiles/                          TagTable.csv(36点) / AlarmConfig.json / Communication.txt
docs/                                 设计文档 + 界面定稿 + 验收标准 + 截图包
```

### 1.2 工程层逐文件依赖判定表（迁移范围的事实依据）

判定方法：逐文件检查 `using System.Windows.Forms`、Zhaoxi 命名空间引用、`Form/Control` 类型出现。

| 文件 | 行数 | 判定 | 依据 |
|---|---|---|---|
| Services/ModbusClient.cs | 384 | ✅ 原样复用 | 仅 System.Net.Sockets/.Diagnostics，三态连接机 + 两阶段精确读，无 UI |
| Services/ModbusFrame.cs | 67 | ✅ 原样复用 | 纯函数 |
| Services/DataPollingService.cs | 417 | ✅ 原样复用 | System.Timers + 事件广播，无 UI；注释明确"FrmMain 订阅此事件" |
| Services/ControlService.cs | 111 | ✅ 原样复用 | 只依赖 ModbusClient/TagAddress |
| Services/ControlDecision.cs | 126 | ✅ 原样复用 | 类注释自称"纯函数"，三阶段决策零 UI |
| Services/AlarmService.cs | 239 | ✅ 原样复用 | 阈值评估 + HashSet 增量，无 UI |
| Services/AlarmCenter.cs | 355 | ✅ 原样复用 | Microsoft.Data.Sqlite + lock，无 UI |
| Services/CommStatistics.cs | 90 | ✅ 原样复用 | 纯统计 |
| Services/LogService.cs | 90 | ⚠️ 一行适配 | **唯一 WinForms 引用：`Application.StartupPath`（LogService.cs:40）** → 改 `AppContext.BaseDirectory`（语义等价：exe 目录）；`BindingList<LogEntry>` 在 WPF 中由 `BindingListCollectionView` 原生支持，DataGrid 可直接绑定，不动 |
| Utils/TagTableLoader.cs | 308 | ✅ 原样复用 | 纯文件解析；关键：`CheckUiBindings(tags, controlBindings, displayBindings, …)` 收 `IEnumerable<string>`（TagTableLoader.cs:254-260），天然跨 UI 框架 |
| Utils/RegisterCodec.cs | 112 | ✅ 原样复用 | 纯函数，字序参数化 |
| Utils/GroupPlanner.cs | 94 | ✅ 原样复用 | 纯函数 |
| Utils/Utility.cs | 73 | ✅ 原样复用 | 纯文件解析 |
| Utils/AppConstants.cs | 46 | ✅ 原样复用 | 常量（轮询 1s、IO 超时 2s、字序 CDAB 等） |
| Utils/Excelhelper.cs | 306 | ✅ 原样复用 | 仅 NPOI |
| Model/*（5 个文件） | 483 | ✅ 原样复用 | 纯 POCO/枚举；AlarmConfig.Load fail-fast A01~A09 |
| Simulation/*（6 个文件） | ~700 | ✅ 原样复用 | 进程内从站，纯 TcpListener 协议处理 |
| **Utils/ControlTagMap.cs** | 254 | ❌ 不迁移 | 递归扫描 WinForms `Control` 树 + Zhaoxi 控件类型（ControlTagMap.cs:1,123-215）；WPF 由**数据绑定**取代其全部职责（§2.5） |
| **Utils/Initializer.cs** | 269 | ❌ 不迁移 | 反射读写 `Form.Controls.Find` + 控件属性（Initializer.cs:71-167）；WPF 改为 **ViewModel 状态持久化**（§M8） |
| **Utils/MessageHelper.cs** | 66 | ❌ 不迁移 | 薄封装 WinForms `MessageBox`；WPF 用 `System.Windows.MessageBox`，API 同形 |
| **FrmMain.cs / Designer.cs / Program.cs** | 3565 | ❌ 不迁移 | UI 层本体，WPF 侧重造 |

**结论**：不可复用的只有 UI 三件套 + 三个 UI 耦合工具类，约 4150 行；可复用工程层约 4500 行且有 110 项单测覆盖（沙箱实测通过，§8）。迁移的工作量重心在 UI 重造，不在协议与业务逻辑。

### 1.3 UI 层的关键行为清单（WPF 侧必须一一对应，来源 FrmMain.cs）

| # | 行为 | 源码位置 | 要点 |
|---|---|---|---|
| B1 | 启动顺序 | FrmMain.cs:140-183 | 控件映射 → 事件接线 → 标签表 fail-fast → 报警配置 fail-fast → AlarmCenter(SQLite) → 通信配置与服务创建 → 恢复状态 → **空态归零**（不显示历史/样例值）→ 启动 200ms UI 定时器 |
| B2 | 日志三层 | LogService.cs:18-22, 49-84 | 文件全量 / 主网格 INFO+（上限 500）/ OnEntry 事件全量（F12 窗口，缓冲 5000） |
| B3 | 日志行着色 | FrmMain.cs:268-294 | ERROR `#C00000/#FDECEC`；WARN `#B85C00/#FFF7EA`；恢复（"…恢复正常"/"…已恢复"结尾）`#1B7A34/#EEF9F0`——主网格与 F12 窗口同一函数，唯一出处 |
| B4 | 连接/断开 | FrmMain.cs:389-436 | IP 格式预检 → "连接中…"→ 已连接绿/失败红；断开后**过程画面全复位**（"不得显示过期状态"） |
| B5 | 模拟模式 | FrmMain.cs:448-531 | 一键：断真实连接 → 进程内从站 127.0.0.1:1502 → 指向本机 → 走与真机**完全相同**的连接/轮询路径（AGENTS.md：双模式不分叉） |
| B5' | 劣化剧本 | tools/ModbusSlaveSim | `set / fault / surge / mute / drop / frag / scenario / status`——WPF 版验收必须全部可复演 |
| B6 | 数据上屏 | FrmMain.cs:568-625 | ParaTextBox 反向索引批量更新；波浪球=实际/计划×100；仪表/圆仪表按 Text 携带的 Tag 写 Value；报警评估；水位曲线 |
| B7 | 首包同步 | FrmMain.cs:627-643 | `_initialSyncDone` 守卫：首包把 PLC 开关态同步进控件；**依赖"编程赋值不触发事件"（E1a）** |
| B8 | 过程画面因果 | FrmMain.cs:717-758 | 用户裁定物理规则：主阀=总进水源；进水段随主阀；泵支路=主阀开**且**该泵运行；泵图变色仅在值变化时赋值（重复赋值重置动画相位） |
| B9 | 控制链路 | FrmMain.cs:811-892 | HandleToggle 唯一入口：预检（未连接/R 只读/标签缺失）→ 确认框 → 写线圈 → 失败回滚；`_handlingToggle` 防重入 |
| B10 | 趋势曲线 | FrmMain.cs:898-925 | 单曲线水位，300 点滚动；**历史缺陷教训**：series3 未赋值导致每秒空引用风暴——WPF 侧"取不到系列必须显式报错"，不许静默 |
| B11 | 看门狗 | FrmMain.cs:927-950 | 200ms UI 定时器：数据过期兜底（>3×轮询周期 → 通信中断）；管道重绘兜底（**WPF 保留模式渲染不需要此项**，仅保留数据过期部分） |
| B12 | F12 日志窗口 | FrmMain.cs:113-121, 960-1229 | 级别筛选/关键字/自动滚动/暂停/导出 TXT/清屏/双击详情/**确认报警**（Acknowledge 落库）/打开日志目录 |
| B13 | 状态持久化 | FrmMain.cs:177-181, Initializer | 退出存 `app_init.json`，启动恢复；**恢复后立即空态归零显示值**（历史快照不得当作事实显示） |
| B14 | 窗口形态 | 界面定稿 §2.1 | 1492×1000 固定、无边框、panel1 拖动（Win32 ReleaseCapture/SendMessage，FrmMain.cs:34-41）、button1 关闭、FormClosing 停服务 |

### 1.4 WPF 工程现状（water-system-wpf）

- `net8.0-windows` + `UseWPF`，仅默认模板：`App.xaml`（StartupUri 指向 MainWindow）、`MainWindow.xaml`（800×450 空 Grid）、无 git 仓库。
- 结论：目标仓库是白纸，架构自由度完整；**第一步必须 git init**（§M0）。

---

## 2. 目标架构与关键技术决策（每条有依据）

### 2.1 解决方案布局（WPF 仓库内）

```
water-system-wpf/
├── src/
│   ├── WaterSystem.Core/            工程层类库（net8.0，无任何 UI 框架引用）
│   │   ├── Services/  Model/  Simulation/  Utils/      ← 自 WinForms v1.0 抽取（附录 A）
│   │   └── PROVENANCE.md            ← 记录抽取源 commit hash 与文件清单
│   └── WaterSystem.Wpf/             WPF 上位机
│       ├── App.xaml(.cs)            ← 组合根 + fail-fast 启动 + 全局异常
│       ├── Controls/                ← Zhaoxi 等价自绘控件（附录 B）
│       ├── Views/                   ← MainWindow.xaml / LogViewerWindow.xaml / LogDetailDialog
│       ├── ViewModels/              ← MainViewModel / LogViewerViewModel
│       ├── Binding/                 ← TagBinding 标记扩展 + 绑定注册表（§2.5）
│       └── State/                   ← VM 状态持久化（§M8）
├── tests/
│   ├── WaterSystem.Core.Tests/      ← 110 项单元测试随 Core 迁移（net8.0，不再需要 UseWindowsForms；沙箱实测 110/110，§8-4）
│   └── WaterSystem.Wpf.Tests/       ← 新增：VM 层测试（回写防护/控制决策接线/空态复位）
├── tools/
│   ├── TestRunner/                  ← 端到端集成回归（拉起模拟器；仅协议级部分随迁，WinForms 专项探针留在源仓库，见 §8-3）
│   └── ModbusSlaveSim/              ← 控制台模拟器随迁
├── configFiles/                     ← TagTable.csv / AlarmConfig.json / Communication.txt（同一数据契约，字节级复制）
└── docs/                            ← 本文档 + 各阶段验收记录
```

### 2.2 决策表（D1~D10，每条给理由）

| # | 决策 | 理由（有据） |
|---|---|---|
| D1 | 工程层抽成 `WaterSystem.Core` 类库（net8.0，不引 WindowsForms/WPF） | ① 110 项单测护航行为等价（**沙箱已实测 110/110**，§8-4；tests csproj 注释自证"用例不创建控件实例"）；② 强制"零 UI 依赖"由编译器把关（Core 里写不出 `System.Windows.Forms`）；③ WinForms/WPF 双端可同时消费，迁移期可对照 |
| D2 | Core 复制进 WPF 仓库而非跨仓库引用 | 源仓库已定稿冻结（**抽取源 = master HEAD `098df6d`**；注意 tag 有 v1.0~v1.3 共 4 个，v1.0 后工程层仍有 17 个提交——含 AlarmConfig.json 外置等产品级特性，**从 v1.0 复制会丢特性**，见 §8-1）。git submodule/跨仓引用会把冻结基线和活跃开发耦在一起，风险大于收益。用 PROVENANCE.md 记录来源 commit hash，漂移靠测试拦截 |
| D3 | UI 采用 MVVM，ViewModel 持有全部状态与行为 | 源项目已经是"服务层事件 + FrmMain 接线"的单向数据流；MVVM 只是把 FrmMain 的职责拆进 VM，`ControlDecision` 纯函数、服务层事件原样对接，不需要推翻任何既有设计。用 CommunityToolkit.Mvvm（8.x，微软维护）减样板；若坚持零第三方依赖，手写 INotifyPropertyChanged 亦可，不影响本方案其余部分 |
| D4 | Zhaoxi 控件全部 WPF 自绘（UserControl/Style+Template） | Zhaoxi 是 WinForms 程序集，无 WPF 版；WindowsFormsHost 有 airspace 限制（HwndHost 永远盖在 WPF 上层），且源界面定稿清单要求视觉一致（界面定稿 §2.2）。自绘控件保留同名同属性名（附录 B），使行为映射可逐条核对 |
| D5 | 布局仍是 Canvas 绝对定位的**画布式设计稿**，但内容为《UI人性化设计概要》§2 的新版面 | E4 教训仍成立：画布式界面套自适应容器会产生空洞（《布局设计-v3》§1.1 留档）；v1.1 起设计稿内容不再是 Designer.cs 坐标还原，而是人性化重排版（用户 2026-10-11 裁定解除"界面冻结"对 WPF 版的约束）；是否叠加 WPF 矢量等比缩放（Viewbox）为待用户裁决项（概要 §7 Q1） |
| D6 | 无边框拖动用 `Window.DragMove()` | 替代 Win32 `ReleaseCapture/SendMessage(WmNclButtonDown)`（FrmMain.cs:34-41,103-110）——WPF 内建能力，行为等价，删 P/Invoke |
| D7 | 趋势曲线自绘（Polyline + 轴），不引图表库 | 单曲线 300 点/1s，WPF 矢量渲染毫无压力；ScottPlot 在源项目已被用户裁决退役（界面定稿 §2.3），不得复活；WindowsFormsHost 承载现有 Chart 仅作后备方案 |
| D8 | `BindingList<LogEntry>` 保留 | WPF 对 `IBindingList` 有原生视图 `BindingListCollectionView`，DataGrid 直接绑定；LogService 仅改一行 `Application.StartupPath → AppContext.BaseDirectory`，把改动面压到最小 |
| D9 | 服务事件 → VM 的线程封送用 `Dispatcher.InvokeAsync` | 源模式是 `InvokeRequired → Invoke`（FrmMain.cs:207-211, 534-562）；WPF 对应物即 Dispatcher。**用 InvokeAsync（Begin）而非同步 Invoke**，避免"工作线程同步等 UI、UI 又在等工作线程"的死锁形态 |
| D10 | 200ms DispatcherTimer 只保留"数据过期看门狗" | 管道重绘兜底是 GDI 特有补丁（FrmMain.cs:932-936 注释：动画在跑而屏幕静止）；WPF 保留模式渲染下动画自动提交，此项无对应物。数据过期判定（>3×轮询周期）逻辑原样保留（FrmMain.cs:940-947） |
| D11 | 组件策略：**自绘控件 12 个（工艺语义 8 + 开关 3 + 卡骨架 1）+ 基础件引 HandyControl（MIT）** | 渲染实证（§8 第 8 项）：原生 DataGrid 深色表头需整套样式接管（≈1~2 天），HC 现成深色皮肤；工艺语义件（水路因果/量程=TagTable）无库可替代，自绘可行性已由 PoC 验证；HC 只做"壳"不替代自绘控件——避免 Zhaoxi 式"控件库无对应版本→整套重写"教训；零依赖备选=全自绘（+1~2 天）。详见详细设计书 §8 |

### 2.3 线程模型（对应关系表）

| WinForms 现状（有据） | WPF 对应 |
|---|---|
| 服务在 ThreadPool 触发事件；FrmMain 用 `InvokeRequired/Invoke` 封送（FrmMain.cs:207-211） | 同样：服务在 ThreadPool 触发事件；VM 处理器入口统一 `Application.Current.Dispatcher.InvokeAsync(() => …)` |
| LogService.Add "必须在 UI 线程调用"（LogService.cs:32 注释） | 不变——VM 的 `Log()` 封送后调用，保持 BindingList 的 ListChanged 在 UI 线程 |
| `System.Windows.Forms.Timer`（UI 定时器） | `DispatcherTimer`（同为 UI 线程队列定时器） |
| `System.Timers.Timer`（轮询定时器，服务内部） | 不变（Core 层原样） |

### 2.4 回写防护（本项目最大技术风险，专项设计）

**问题本质**：WinForms 中 `sw.Checked = plcValue` 不触发 `CheckedChanged`（E1a 实证，FrmMain.cs:629-631），所以"PLC 数据 → 控件"与"用户点击 → 写 PLC"两条路天然不串。WPF 中如果开关状态走双向绑定，PLC 同步改 VM 属性 → 控件视觉变 → 控件若再发"用户改了"回调 → VM 又写 PLC，形成回写风暴（对现场 = 误发控制指令，**不可挽回**）。

**设计（三层防护，缺一不可）**：

1. **单一状态属主**：开关状态的唯一事实源是 VM 属性；WPF 控件（USwitch/UJogSwitch/URotarySwitch 等价物）视觉一律 `OneWay` 绑定 VM。控件只上报**用户手势**（Command/事件），不反向写状态——从结构上消灭"控件改状态"这条路。
2. **手势与同步分流**：VM 属性 setter 带 `_syncingFromPlc` 抑制标志（对应源 `_handlingToggle`，FrmMain.cs:71,865）。PLC 首包同步（B7）与轮询回写只改状态属性；用户手势走 `ToggleCommand(tagName)` → `ControlDecision.Precheck → 确认 → WriteCoilAsync → PostWrite`。
3. **自翻转语义的等价实现**：源项目中 Jog/Rotary 控件"先自翻转再发事件"（ControlDecision.cs:6-16），USwitch"不自翻转"。WPF 统一为：手势命令先把 VM 状态置为目标值（对应自翻转后的视觉），取消/拒绝/写失败时回写旧值（对应 `RevertNeeded`/`Rollback`）。`ControlDecision` 三阶段纯函数**原样复用**，调用侧把"尚未翻转的当前态"按 kind 映射（Switch: current=旧态；Jog/Rotary: current=翻转后目标态），行为由迁移过来的 ControlDecisionTests + 新增 VM 测试双重覆盖。

**专项验证**（并入 M5）：模拟器正常轮询 60 秒（含 `scenario wave`），TestRunner 在从站侧断言**写功能码（0x05）请求计数 = 0**；用户无操作时零回写，才允许出阶段。

### 2.5 数据契约 E08/E09/W01 在 WPF 的落地

源项目的 UI 绑定核对分两半：控件扫描（WinForms 专属）+ 字符串核对（跨框架）。后者签名（TagTableLoader.cs:254-260）：

```csharp
CheckUiBindings(List<TagAddress> tags,
    IEnumerable<string> controlBindings,   // 控制类绑定的 Tag 名
    IEnumerable<string> displayBindings,   // 显示类绑定的 Tag 名
    List<string> errors, List<string> warnings)
```

WPF 侧做法：所有与 Tag 挂钩的绑定统一走 `TagBinding` 标记扩展（或附加属性 `ui:TagMap.Name="Pump1_F"`），在 XAML 解析/加载完成时把 Tag 名注册进静态注册表（区分控制/显示两类）；启动流程在 `InitializeComponent()` 后调用 `CheckUiBindings` 收集结果——E08（RW 无控制入口）/E09（绑定不存在的标签）继续 fail-fast 拒绝启动，W01 仅警告。**保证"标签表是唯一事实源 + fail-fast"这条产品底线跨框架不变**。绑定注册表实现必须在 M2 交付并配单测（给定一组绑定名 → 错误码逐一断言），防止"注册表漏注册 → 校验静默通过"的假绿——这正是源项目 series3 缺陷（B10）的教训：静默失败比崩溃更危险。

---

## 3. 分阶段实施计划（M0~M9）

> 每阶段固定五要素：目标 / 依据 / 任务 / 验证要求 / 退出标准（含 git 操作）。
> 验证沿用源仓库三类验收口径（验收标准 §0.1）：**[自动]** 一条命令判定；**[操作]** 人工按步骤操作观察；**[检查]** grep/看代码/看 git 确认。
> 纪律沿用源仓库（验收标准 §0.2）：每条验收留证据（命令输出/日志行/截图），失败先修、修不动回退，不允许"先过后面再补"。

### M0 仓库初始化与迁移基线（0.5 天）

| 项 | 内容 |
|---|---|
| 依据 | 源仓库 AGENTS.md 工程纪律（设计文档先行、feature 分支、提交附实证）；目标仓库当前无 git（实查 `git status` = not a repository） |
| 任务 | ① `git init` + `.gitignore`（bin/ obj/ .vs/ logs/ data/*.db app_init.json *.user）+ 首次提交；② 本文档入库；③ 打 tag `v0.0-baseline` |
| 验证 | [检查] `git log` 有且仅有基线提交；[检查] 随意 build 后 `git status` 零未跟踪产物（.gitignore 生效） |
| 退出 | tag `v0.0-baseline` |

### M1 Core 类库抽取 + 测试护航（1~2 天）

| 项 | 内容 |
|---|---|
| 依据 | §1.2 依赖判定表；测试工程注释"用例不创建控件实例"（tests csproj 第 8 行） |
| 任务 | ① 按附录 A 清单从 WinForms 仓库 **master HEAD（`098df6d`）**复制文件 → `src/WaterSystem.Core`（net8.0 类库，不引任何 UI 框架）。**不得取 v1.0 标签**：v1.0 后工程层仍在演进（17 个提交），含 AlarmConfig.json 外置等特性（§8-1）；② 唯一代码改动：LogService.cs:40 `Application.StartupPath` → `AppContext.BaseDirectory`（语义等价：可执行文件目录）；③ 写 `PROVENANCE.md`（源 commit hash `098df6d` + 文件清单 + 该唯一改动说明）；④ 110 项测试迁入 `WaterSystem.Core.Tests`（net8.0，去掉 UseWindowsForms）；⑤ configFiles 三件字节级复制入库；⑥ csproj 对三件配置加 `CopyToOutputDirectory=PreserveNewest`（对齐源工程做法，Core 按 `AppContext.BaseDirectory\configFiles` 寻址） |
| 验证 | [自动] `dotnet test` = 110 项全绿（源仓库实测 110/110 为对照基线）；[自动] TestRunner **协议级部分**改引 Core 后全绿（WinForms 专项探针不随迁，§8-3）；[检查] `grep -r "System.Windows.Forms" src/WaterSystem.Core` = 0 命中；`grep -r "Zhaoxi" src/WaterSystem.Core` = 0 命中；[检查] 源仓库 `git status` 干净（一行未动）。**本阶段已在 2026-10-11 沙箱预演通过（§8-4），执行时按同口径重跑** |
| 退出 | tag `v0.1-core` |

### M2 WPF 骨架：启动链 + 主窗口 + 组合根（1 天）

| 项 | 内容 |
|---|---|
| 依据 | 启动顺序 B1（FrmMain.cs:140-183）；窗口形态 B14（界面定稿 §2.1：1492×1000 固定、无边框、panel1 拖动）；fail-fast 哲学（源 AGENTS.md 产品级底线） |
| 任务 | ① `App.xaml` 去掉 `StartupUri`，改 `OnStartup` 显式链：加载 TagTable（E01~E09 全量报出）→ 加载 AlarmConfig（A01~A09）→ 任一 Error = 弹错误清单窗 + `Shutdown()`（对应 WinForms Load 中 Close，FrmMain.cs:157-161,164-167）→ 创建服务组合根 → 显示 MainWindow；② `DispatcherUnhandledException` → LogService + 弹窗（源项目无全局兜底，WPF 补上是补强不是变更，行为对齐"错误必须可见"）；③ MainWindow：`WindowStyle=None` + `ResizeMode=NoResize` + 1492×1000 + `DragMove()`（D6）+ 关闭按钮走 `Closing`（对应 FormClosing：存状态/停轮询/停模拟器，FrmMain.cs:185-201）；④ Dispatcher 封送助手（D9）；⑤ TagBinding 注册表骨架 + 单测（§2.5） |
| 验证 | [操作] 依次注入 9 类标签表错误（删表头/重名/空名/地址越界/Type 非法/float 奇地址/Scale=0/删 UI 绑定标签/控件绑定不存在标签）→ 每次拒绝启动且多错误一次性列全（对应源验收标准 T1.3 全套）；验完复原；[操作] 正常配置启动显示空壳主窗，标题栏可拖动，关闭无异常；[自动] TagBinding 注册表单测绿 |
| 退出 | tag `v0.2-shell` |

### M3 数据监视链路上屏（2~3 天）

| 项 | 内容 |
|---|---|
| 依据 | B4/B5/B6/B11；通信控制区布局（界面定稿 §2.2：IP/端口/连接/断开/label3 三态）；`AppConstants`（轮询 1s、连接超时 5s、IO 超时 2s） |
| 任务 | ① MainViewModel 订阅 `OnDataReceived/OnConnectionChanged/OnError/OnQualityChanged`，Dispatcher 封送；② 通信控制区 UI + IP 格式预检（FrmMain.cs:393-398）；③ `ParaBox` 控件（名称/值/单位，对应 ParaTextBox）+ `TagValues` 索引器绑定；④ 波浪球（实际/计划×100，FrmMain.cs:601-602）、仪表/圆仪表值绑定（B6）；⑤ 模拟按钮（B5：断真实 → 进程内从站 1502 → 自动连接，逻辑照抄 ToggleSimulationAsync）；⑥ 首包同步守卫 `_initialSyncDone` + 数据过期看门狗 DispatcherTimer（B11，去掉管道重绘部分）；⑦ 空态复位 `ResetDisplayValues`/`ResetProcessGraphics`（B1/B4 语义） |
| 验证 | [自动] TestRunner 通信/数据用例全绿；[操作] 一键模拟 → 36 点全部上屏且与模拟器 `status` 输出逐点一致；`set CurWaterPos 0.8` 后 ≤2s 界面跟随（1s 轮询 + 1s 余量）；[操作] `mute on` → "通信中断"红字 → `mute off` → 恢复"已连接"绿字 + 数据续流（对应源"断线韧性"验收）；[操作] 断开连接 → 所有显示值归零、仪表回零、无任何过期状态残留（B4"不得显示过期状态"）；[检查] 启动未连接时界面无数值（对应"设计期样例值是假数据"空态归零，FrmMain.cs:782-794） |
| 退出 | tag `v0.3-monitor` |

### M4 人性化界面重构（5~7 天，工作量最大；v1.1 由"过程画面控件库"扩展）

| 项 | 内容 |
|---|---|
| 依据 | **《UI人性化设计概要》v2.0 + 《WPF-UI详细设计书》v1.2（自解释原则/状态矩阵/逐卡详设/管廊拓扑/copydeck/组件策略 D11）**；B8 水路因果（用户裁定 2026-10-06，git 提交 `8f02100`）继续有效；泵图"仅值变化时赋色"教训继续有效（FrmMain.cs:731-738） |
| 任务 | ⓪ 引入 HandyControl 3.5.1（实测可用版本，§8-9）并做深色令牌对齐（D11）；① 按概要 §2 分区骨架：顶栏状态带（通信/未确认报警徽标/时钟）/ 右栏实时报警面板 / 过程画面管廊布局+泵卡×3 / 日志加高至 ≥6 行可见；② 按附录 B 实现域控件库（视觉令牌按概要 §4.4 色板，属性名映射不变）；③ Core 增 `ValueFormatter.Format(scale)` 纯函数 + 单测（修 P1）与单位显示映射（修 P2）；④ 仪表量程接 TagTable Min/Max（修 P7）；⑤ 报警三处可见：顶栏徽标 + 报警面板 + 过程图对象描红（修 P4，数据源 AlarmCenter 已备）；⑥ 泵双徽章（运行/故障，修 P5）+ 灯文字随状态变（修 P6）；⑦ 趋势阈值线/单位/图例（修 P10）；⑧ 通信控制互斥启用与 busy 态（修 P13）；⑨ 水池液位可视化（修 P15）；⑩ Core 增只读属性 `ReconnectAttempts`（暴露退避计数，唯一服务增量） |
| 验证 | [操作] **概要 §5 映射表 P1~P18 逐条复验关闭**（每条留截图证据）；[操作] 五态水路验证保留（全停/泵转无阀/开阀泵转/泵停阀开/全停）；[操作] 报警发生时主画面三处同时可见（顶栏徽标+面板行+过程图描红），恢复后徽标计数递减；[自动] ValueFormatter 单测全绿；[检查] 水泵数据面板与对应泵列同列对齐（P9） |
| 退出 | tag `v0.4-graphics` |

### M5 控制链路（2 天）

| 项 | 内容 |
|---|---|
| 依据 | B9（FrmMain.cs:811-892）；控制语义用户裁定（源 AGENTS.md：旋转按钮=泵电机、拨动开关=阀门、球灯=纯显示）；ControlDecision 纯函数（D3）；回写防护设计（§2.4） |
| 任务 | ① `ToggleCommand(tagName)`：Precheck → 未连接/只读拒绝弹窗 → 确认框（WPF MessageBox，文案逐字复用 `ControlDecision.DescribeAction`："确定要启动水泵 [Pump1_Run] 吗？"）→ `ControlService.WriteCoilAsync` → 失败回滚 + ERROR 日志；② Jog/Rotary 自翻转等价实现（§2.4 第 3 层）；③ `_handlingToggle` 防重入等价物（VM 忙标志）；④ 控制/显示绑定经 TagBinding 注册表接入 E08/E09（§2.5）；⑤ 球灯（uJogSwitch7-10）绑定为纯显示（不挂命令）——映射错误=越权控制，属不可挽回后果，须在代码评审中逐一点名 |
| 验证 | [自动] ControlDecisionTests 迁移后全绿（决策语义零漂移的直接证据）；[自动] 新增 VM 测试：未连接拒绝 / R 只读拒绝 / 取消回翻 / 写失败回滚 / 防重入；[自动] **回写风暴专项**：轮询 60s（含 wave 场景）从站侧 0x05 写请求数 = 0（§2.4）；[操作] 真实点击旋转按钮 → 确认 → 从站线圈翻转 → 下轮轮询回流确认；写失败（中途 mute on 断流）→ 控件回滚 + ERROR 日志"控制失败已回滚"（FrmMain.cs:875 文案）；[操作] 确认框点"否" → 状态还原不写（Jog/Rotary 回翻） |
| 退出 | tag `v0.5-control` |

### M6 报警与日志呈现（2 天）

| 项 | 内容 |
|---|---|
| 依据 | B2/B3/B12；着色唯一出处原则（界面定稿 §3.1）；确认入口（界面定稿 §3.2 已实施语义）；AlarmCenter ISA-18.2 生命周期注释（AlarmCenter.cs:13-22） |
| 任务 | ① 主日志 DataGrid 绑定 `LogService.Entries`（BindingListCollectionView，D8）+ 行着色（`TryResolveLogRowStyle` 整段移植为共享静态函数——**主网格与 F12 窗口同一出处**，B3）；② F12 = `PreviewKeyDown` → LogViewerWindow：级别筛选/关键字/自动滚动（暂停时冻结、恢复时重建，对应 TogglePause）/导出 TXT/清屏确认/双击详情/确认报警（`AlarmCenter.Acknowledge(id, by)`，by 取 Windows 用户名空则"操作员"，FrmMain.cs:1209-1224）/打开日志目录；③ 报警接线：`OnAlarmStatusChanged` → WARN 日志 + `Raise/Clear`（FrmMain.cs:647-663 原样平移）；④ 滚动语义：`ScrollToEnd` 需判"句柄已创建"（源 FrmMain.cs:1123-1131 曾因句柄未创建抛 ArgumentOutOfRangeException 导致 F12 打不开——WPF 对应判 `IsLoaded`） |
| 验证 | [自动] AlarmCenterTests / AlarmService 相关测试全绿；[操作] `surge on` → 橙色报警行跳出 → F12 双击 → 确认 → 日志"报警已确认 [HighPressure]（用户名）"→ 重启程序 → history.db 记录仍在（对应源实证链 界面定稿 §3.2）；[操作] `fault 2 on/off` → 报警橙行 + 恢复绿行（恢复文案着色判定 B3）；[检查] DEBUG 级不进主网格、只进 F12 窗口与文件（B2）；[检查] F12 四级筛选互斥组合、关键字过滤、导出文件内容抽查 |
| 退出 | tag `v0.6-alarm` |

### M7 趋势曲线（1 天）

| 项 | 内容 |
|---|---|
| 依据 | B10（300 点水位单曲线；series3 缺陷教训）；D7 |
| 任务 | 自绘 `WaterTrendChart`：Polyline + 时间轴/值轴刻度（按 TagTable 的 Min/Max 量程列——源项目该列已解析入库但界面未消费，README"已知取舍"有言在先，WPF 侧首次消费它，属增强不属变更，需在验收记录注明）；`AppendChartPoint` 滚动窗口逻辑照抄（FrmMain.cs:898-911）；**取不到曲线数据上下文时显式 ERROR 日志**（B10 教训：不许静默） |
| 验证 | [操作] `scenario wave` 跑 6 分钟：曲线连续、300 点滚动无内存增长、停场景曲线停在尾值不越界；[操作] 断线期间曲线停更（不画过期点），恢复续画 |
| 退出 | tag `v0.7-trend` |

### M8 状态持久化（0.5~1 天）

| 项 | 内容 |
|---|---|
| 依据 | B13（FrmMain.cs:177-181 + Initializer 全文）；"持久化恢复的是历史快照：未连接时灯/仪表/数值不得显示历史"（FrmMain.cs:179-181 注释） |
| 任务 | VM 状态（各开关状态、IP/端口）→ `app_init.json` 序列化/恢复；**显示值不持久化**（与源一致：恢复后立即 `ResetDisplayValues` 空态归零）；恢复失败降级为 WARN 日志不阻断启动（Initializer.cs:107-111 同语义） |
| 验证 | [操作] 开泵1、改 IP → 关闭 → 重开：开关状态与 IP 恢复、数值全零、灯全灭；[操作] 手工写入非法 JSON → 启动仅 WARN 不崩溃；[检查] 持久化文件不含任何显示类数值字段 |
| 退出 | tag `v0.8-persist` |

### M9 集成回归、定稿验收与归档（1~2 天）

| 项 | 内容 |
|---|---|
| 依据 | 源验收标准 §0.3（V1~V7 映射）；AGENTS.md 回归底线；README 截图包口径 |
| 任务 | ① 全量回归矩阵执行（§5）；② 劣化演练全套：`frag on`（TCP 分片）、`mute on`（超时→退避重连 1s→30s）、`surge`、`fault N`、`drop`——每种都有"现象截图 + 日志行"证据（对应源劣化验收手段）；③ 浸泡测试 ≥2 小时（模拟模式连续轮询 + wave 场景）：内存曲线平稳、无 UI 冻结、无 ERROR 增长；④ 截图包（主界面/报警发生/报警恢复/控制下发/通信中断/F12 窗口，与 README 截图一一对应）；⑤ 验收记录入库 docs/；⑥ tag `v1.0-wpf`；⑦ WinForms 仓库归档说明（README 增补一行指向 WPF 仓库——此为源仓库唯一允许的文档性改动，提交注明） |
| 验证 | 见 §5 总表，全部通过 |
| 退出 | tag `v1.0-wpf`（定稿基线） |

---

## 4. 风险登记册（每条：风险 / 依据 / 缓解 / 触发器）

| # | 风险 | 依据 | 缓解 | 触发器（出现即停线处理） |
|---|---|---|---|---|
| R1 | **绑定回写风暴**（PLC 同步被当成用户手势 → 误发控制指令） | WinForms 依赖 E1"编程赋值不触发事件"（FrmMain.cs:629-631；README 技术决策 #3），WPF 无此天然语义 | §2.4 三层防护 + M5 专项测试 | 从站侧观察到任何非人工 0x05 写请求 |
| R2 | 重排版面引入新缺陷（遮挡/溢出/错位） | 布局设计-v3 曾实测五处坐标缺陷（L1~L5） | 概要 §2 已做分区预算核对（水平/垂直逐项求和）；M4 保留"布局回归截图对照概略图"验收项；内部坐标在 M4a 设计稿细化时逐项核算（沿用 v3 核对纪律） | 任一分区内容溢出面板边界 |
| R3 | TagBinding 注册表漏注册 → E08/E09 假绿（校验静默通过） | series3 缺陷教训：静默失败比崩溃危险（FrmMain.cs:913-925） | 注册表单测（M2）+ M5 全量控制绑定核对的 [操作] 验证（故意删标签行 → 必须报 E09 拒绝启动） | 注入错误却启动成功 |
| R4 | Dispatcher 同步 Invoke 死锁 | 源 Invoke 模式直译过来的惯用错误形态 | D9 强制 InvokeAsync；代码评审检查所有封送点 | UI 冻结且工作线程栈停在 Wait |
| R5 | Core 与 WinForms 双端漂移 | D2 复制式共享 | WinForms v1.0 冻结不再演进（只读对照基线）；Core 改动必须有测试护航并回写 PROVENANCE.md | 任何"顺手改 Core"而无测试的提交 |
| R6 | 字体/字号视觉偏差（WinForms 9pt 微软雅黑 ≠ WPF 数值） | 源日志窗口指定"微软雅黑 9F"（FrmMain.cs:986） | 换算规则：WPF 中 1pt=4/3 px，9pt → 12px（FontSizes=12）；M4 截图比对一并确认 | 截图比对发现行高/密度明显不一致 |
| R7 | DataGrid 高频刷新卡顿 | 主网格 500 条上限 + ListChanged 每条触发（LogService.cs:36,76-79） | 上限有界；若实测卡顿，主网格改 ObservableCollection + 批量提交（仅性能优化，不改语义） | 浸泡测试 CPU/掉帧异常 |
| R8 | 高 DPI 下布局失真 | 源项目 AutoScaleMode.None 固定画布（界面定稿 §2.1） | WPF 用设备无关单位天然 DPI 无关（这是迁移收益之一）；布局提取脚本用设计坐标直接落 Canvas | 多 DPI 屏幕截图比对异常 |

---

## 5. 总验证要求（定稿验收矩阵）

### 5.1 三类验收口径（沿用源验收标准 §0.1）

- **[自动]**：`dotnet test`（≥110 项）、TestRunner 协议级部分、grep 检查——每阶段必跑，结果贴验收记录。
- **[操作]**：模拟器命令驱动的全链路演练——按 §3 各阶段"验证"栏逐步执行。
- **[检查]**：git 状态 / grep 零残留 / 代码评审点名。

### 5.2 回归矩阵（M9 执行，任一不过回对应阶段排查）

| 组 | 用例 | 判定 |
|---|---|---|
| 数据契约 | 正常启动 36 点加载；9 类标签表错误逐一 fail-fast；A 系列报警配置错误 fail-fast | 与源行为逐字一致（弹窗文案、错误码、日志行） |
| 通信 | 连接/断开/重连；mute 超时 → Broken → 退避重连（1s→30s）；frag 分片不错帧 | 状态灯三态正确；TID/两阶段读缺陷不重现（TestRunner 断言） |
| 监视 | 36 点上屏与模拟器 status 一致；`set` 命令 ≤2s 跟随；仪表/波浪球/过程画面全联动 | 逐点核对 |
| 控制 | 泵启停/阀门开关全点位：确认弹窗 → 写 → 回流；未连接拒绝；只读拒绝；失败回滚；**零回写风暴** | 文案与源逐字一致 |
| 报警 | surge/fault 触发 → 橙行 → 恢复绿行 → F12 确认 → SQLite 落库 → 重启可查 | ISA-18.2 生命周期四态正确（AlarmCenter.cs:13-22） |
| 日志 | 三层分流（文件全量/主网格 INFO+/F12 全量）；着色规则；导出；清屏 | B2/B3 |
| 趋势 | 300 点滚动、断线停更、恢复续画 | B10 |
| 空态 | 启动未连接全零；断开全灭；不显示过期/历史状态 | B1/B4/B13 |
| 持久化 | 开关/IP 恢复、显示值不恢复、非法文件降级 | B13 |
| 稳定性 | ≥2h 浸泡：内存平稳、无冻结、日志零 ERROR 增长 | 浸泡报告 |

### 5.3 证据纪律

每条验收的证据（命令输出/日志行复制/截图）随该阶段 feature 分支的提交入库（对应源仓库"每步提交附实证"纪律）；阶段验收记录合入 `docs/验收记录-Mx.md`。

---

## 6. git 开发管理细则

### 6.1 仓库拓扑

| 仓库 | 角色 | 纪律 |
|---|---|---|
| `Intelligent-water-storage-system` | **冻结基线 + 行为对照物**（tag v1.0） | 迁移期零代码改动；仅 M9 允许一行 README 归档说明 |
| `water-system-wpf` | **迁移主场** | M0 初始化；master 保持全绿（可编译 + 全部测试过）；一切开发走 feature 分支 |

### 6.2 分支与提交模型（用户裁定 2026-10-11：**每个任务一条分支，验收达标才并入 master**）

```
master                              ← 只接受任务分支合入；合入前置条件 = 该任务验收全绿
└── feature/T1.1-copy-core          ← 每个 T 任务一条分支（实施细则 §2 任务编号）
└── feature/T1.2-…                  ← 下一任务从最新 master 重新切出
└── …（阶段全部任务合并后打里程碑 tag）
```

- **提交信息**：`{T编号} {一句话}（证据：…）`，中文、含证据（对齐源仓库风格）。
- **合并门槛**：任务的 [自动]/[操作]/[检查] 验收全过 + 证据贴合并描述；未达标不得合并，修不动整支废弃。
- **里程碑 tag**：`v0.0-baseline → v0.1-core → v0.2-shell → v0.3-monitor → v0.4-graphics → v0.5-control → v0.6-alarm → v0.7-trend → v0.8-persist → v1.0-wpf`。
- **回退策略**：问题定位到某任务即回退该分支的合并（revert merge commit）；禁止"先过后面再补"（源验收标准 §0.2 原文）。
- **阶段回归**：里程碑 tag 前重跑此前所有 [自动] 项。

---

## 7. 工作量汇总

| 阶段 | 内容 | 估时 |
|---|---|---|
| M0 | 仓库初始化 | 0.5 天 |
| M1 | Core 抽取 + 110 测试迁移 | 1~2 天 |
| M2 | WPF 骨架（启动链/主窗/组合根/绑定注册表） | 1 天 |
| M3 | 数据监视链路上屏 | 2~3 天 |
| M4 | 人性化界面重构（概要 §2 分区骨架 + 控件库附录 B + P1~P18 修复） | 5~7 天 |
| M5 | 控制链路 + 回写防护专项 | 2 天 |
| M6 | 报警与日志呈现 | 2 天 |
| M7 | 趋势曲线 | 1 天 |
| M8 | 状态持久化 | 0.5~1 天 |
| M9 | 集成回归 + 定稿验收 | 1~2 天 |
| **合计** | | **16~22 个工作日** |

---

## 附录 A：Core 抽取文件清单（M1 执行清单，按 §1.2 判定表）

**复制（原样）**：
`Services/ModbusClient.cs`、`Services/ModbusFrame.cs`、`Services/DataPollingService.cs`、`Services/ControlService.cs`、`Services/ControlDecision.cs`、`Services/AlarmService.cs`、`Services/AlarmCenter.cs`、`Services/CommStatistics.cs`、
`Model/TagAddress.cs`、`Model/DataPoint.cs`、`Model/AlarmModels.cs`、`Model/AlarmConfig.cs`、`Model/CommunicationSet.cs`、
`Utils/TagTableLoader.cs`、`Utils/RegisterCodec.cs`、`Utils/GroupPlanner.cs`、`Utils/Utility.cs`、`Utils/AppConstants.cs`、`Utils/Excelhelper.cs`、
`Simulation/DataStore.cs`、`Simulation/ModbusServer.cs`、`Simulation/ProtocolProcessor.cs`、`Simulation/ScenarioEngine.cs`、`Simulation/ModbusException.cs`、`Simulation/SimulatorOptions.cs`

**复制 + 一行适配**：`Services/LogService.cs`（`Application.StartupPath` → `AppContext.BaseDirectory`，LogService.cs:40）

**不复制（WinForms 专属，WPF 以新机制替代）**：`Utils/ControlTagMap.cs`、`Utils/Initializer.cs`、`Utils/MessageHelper.cs`、`FrmMain.*`、`Program.cs`

**命名空间处理**：源命名空间 `Intelligent_water_storage_system.*` 在 Core 内保留原名（减少 diff 噪声、测试零改动）还是改 `WaterSystem.Core.*`——**决定：保留原名**。理由：110 项测试的 using 零改动即可编译（沙箱已实证，§8-4），等价性验证少一个变量；后续如需改名，在测试护航下一次性完成。

## 附录 B：控件迁移映射表（Zhaoxi WinForms → WPF 自绘）

数量统计自 FrmMain.Designer.cs 字段声明（实查）。

| Zhaoxi 控件 | 数量 | 关键属性（保留同名） | WPF 实现要点 |
|---|---|---|---|
| ParaTextBox | 24 | ParaName / Value / 单位 | UserControl：名称+数值+单位三段布局；Value DP（double），绑定 `TagValues[ParaName]` |
| UPipe | 14 | Active / FlowSpeed | Line/Path + StrokeDashArray，Active 驱动 DashOffset 双精度动画；值不变不重启动画（B8 相位教训） |
| UCPump3 | 3 | PrimaryColor | 泵图形（Path 组合），运行色 `#3FB950`（FrmMain.cs:733 与 TestRunner 校准值），停止回默认色 |
| USwitch | 4 | Checked / Tag | 拨动开关模板；**OneWay 绑定 VM 状态** + UserToggled 命令（§2.4） |
| UJogSwitch | 7 | IsOn / Tag | 球形指示灯模板；7 盏中 4 盏是阀门纯显示（uJogSwitch7-10）、3 盏是泵状态灯（dispPump*）——同一控件两种角色，命令挂接必须在 M5 评审点名（R1/R2） |
| URotarySwitch | 3 | SwitchState / Tag | 旋钮模板；泵电机启停入口 |
| UMeter | 2 | Value / MinValue / MaxValue / Text(=Tag) | 弧形表盘：Arc + 针 RotateTransform，角度线性映射量程 |
| UCircleMeter | 2 | 同上 | 圆形表盘 |
| UWaveBall | 1 | Value(0~100) / Text(=Tag) | 椭圆 + 波浪 Path 裁剪，液位百分比动画 |
| UGroupPanel2/3 | 3+5 | 标题/圆角/渐变 | Border + HeaderedContentControl 样式 |
| UButton | 2 (+运行时【模拟】按钮) | Text / BgColor / BgColor2 | Button ControlTemplate（渐变背景） |
| 基础控件 | Label×44 / PictureBox×12 / TextBox×3 / Panel×2 / DataGridView×1 / Button×1 / Chart×1 | — | TextBlock / Image / TextBox / Border / DataGrid / Button / 自绘 WaterTrendChart（M7） |

**状态灯候选名机制保留**：`dispPump2Run` 在设计器中实为 `uJogSwitch1`（候选名双匹配，FrmMain.cs:724）——WPF 侧注册表按候选名列表匹配，避免重蹈"设计期改名破坏代码"。

## 附录 C：行为等价对照速查（WinForms → WPF）

| WinForms 原文（位置） | WPF 等价实现 | 阶段 |
|---|---|---|
| `InvokeRequired → Invoke`（FrmMain.cs:207-211） | `Dispatcher.InvokeAsync`（D9） | M2 |
| `ProcessCmdKey` 捕获 F12（FrmMain.cs:113-121） | Window `PreviewKeyDown` | M6 |
| `ReleaseCapture + SendMessage` 拖动（FrmMain.cs:103-110） | `Window.DragMove()`（D6） | M2 |
| `Application.StartupPath`（LogService.cs:40） | `AppContext.BaseDirectory` | M1 |
| `System.Windows.Forms.Timer` 200ms（FrmMain.cs:929） | `DispatcherTimer` 200ms（仅数据过期看门狗，D10） | M3 |
| `ControlTagMap` 反向索引批量刷新（ControlTagMap.cs:100-110） | XAML 绑定 `TagValues[tagName]`（§2.5） | M3 |
| 编程赋值不触发事件（E1a，FrmMain.cs:629-631） | 单一状态属主 + 抑制标志 + 手势分流（§2.4） | M5 |
| `MessageBox.Show(YesNo)` 确认（FrmMain.cs:854-856） | `System.Windows.MessageBox` 同参 | M5 |
| `FormClosing`（FrmMain.cs:185-201） | `Window.Closing` + OnClosing | M2 |
| `Controls.Find(name, true)` 候选名查找（FrmMain.cs:666-676） | TagBinding 注册表候选名匹配（附录 B） | M4 |
| DataGridView + BindingList（FrmMain.cs:241） | DataGrid + BindingListCollectionView（D8） | M6 |
| Initializer 反射控件树持久化（Initializer.cs:71-167） | VM 状态序列化（M8） | M8 |

---

## 8. 可行性验证记录（2026-10-11 实测）

> 本节是对本文关键断言的实测结果。环境：Windows 10.19045 x64，SDK 9.0.313。验证在临时沙箱进行，源仓库与目标仓库零改动，沙箱已清理。

| # | 验证项 | 方法 | 结果 |
|---|---|---|---|
| 1 | 抽取源选择 | `git tag` + `git rev-list --count v1.0..HEAD` + 工程层目录的 `git log` | **发现并纠正**：标签共 4 个（v1.0~v1.3），v1.0..HEAD 有 17 个提交，其中 d90914a 在工程层新增 AlarmConfig.json 外置（[A01]~[A09]）+ AlarmConfigTests 8 项——**从 v1.0 复制会丢产品级特性**。抽取源改为 master HEAD `098df6d`（工作树干净 = HEAD），D2/M1 已同步更正 |
| 2 | 工程层 UI 依赖 | `grep -r "System.Windows.Forms\|Zhaoxi\|System.Drawing"` 于 Services/Model/Simulation/Utils（排除 3 个已知不迁移文件） | **0 命中**——§1.2 判定表成立；UI 依赖仅 LogService 一行 + 3 个不迁移文件 |
| 3 | TestRunner 可迁移性 | 通读其结构（1092 行） | **修正认知**：TestRunner 是混合体——协议级端到端（`RunCommTests`，拉起控制台模拟器，可随迁）+ WinForms 专项探针（DockOrderExperiment、E1 控件事件探针、WinForms 控件盘点，共 89 处 WinForms/Zhaoxi 引用，**不可迁移也无须迁移**）。WPF 侧的对应物：VM 层单测（回写防护/控制决策）+ 渲染断言，已列入 M3/M5 |
| 4 | **M1 沙箱全链路** | 临时目录：按附录 A 复制 26 文件 → LogService 单行补丁 → 纯 net8.0 类库 `dotnet build` → 9 个测试文件 + net8.0 测试工程（去 UseWindowsForms）`dotnet test` | **Core 0 错误编译；110/110 测试全绿（827ms）**。M1 的全部关键断言实证成立 |
| 5 | 源仓库基线 | `dotnet test`（源解决方案） | **110/110 通过（945ms）**。README"102 项"已过时（d90914a 加了 8 项），以实测为准。构建警告 5 处（CS1998/CS8602×2/CS0169×2）全部位于 FrmMain（不在迁移范围），工程层零警告 |
| 6 | WPF 目标工程可编译 | `dotnet build water-system-wpf` | 通过，零错误 |
| 7 | 测试源码 UI 依赖 | grep tests/*.cs | 0 命中（命中均为 bin/obj 生成物）——测试可整体迁入 net8.0，与沙箱结果互证 |
| 8 | **UI 层渲染实证（第二轮）** | 沙箱 net8.0-windows WPF（零第三方依赖）：按《详细设计书》令牌/坐标代码构建主画面 → RenderTargetBitmap 离屏渲染 1492×1000 → PNG | **编译零错误、渲染成功**：深色令牌/CardChrome/环形仪表/管道流动层/状态徽章/报警四态 chip/日志 DataGrid+BindingList+行着色/趋势阈值线全部按设计出图（[design-preview.png](../docs/design-preview.png)）。D5 画布、D7 自绘趋势、D8 BindingList 绑定、附录 B 域控件自绘路线全部实证成立。**实证驱动的设计修订**：①不透明泵卡遮挡管路 → 设计书 v1.1 管廊布局；②原生 DataGrid 默认白表头 → 决策 D11（基础件引 HandyControl）；③PipeIdle 对比度上调 #334155→#3D4E6B |
| 9 | HandyControl 组件依赖（D11 的 B 类） | 沙箱 net8.0-windows WPF + `PackageReference HandyControl`（通配最新）→ `dotnet restore` + `dotnet build` + HC 命名空间编译 | 解析 **3.5.1**（最新稳定版）：联网还原成功（首包下载约 3 分钟，后续走缓存）、引用 `HandyControl.Controls` 编译零错误——B 类依赖闭环。**版本口径由"4.x"更正为 3.5.1**（设计书 §8/D11/细则同步） |

**验证后修订**：本文 §1.1/D1/D2/M1/§5.1/§7/附录 A 的"102 项/106 断言/v1.0 标签"表述已按上述结果更正；遗留问题清单见下。

### 8.1 遗留问题清单（验证发现，已定处置）

| # | 问题 | 影响 | 处置 |
|---|---|---|---|
| L1 | 源仓库 README/AGENTS.md 写"102 项测试"，实测 110 | 文档数字失真 | 本仓库以实测 110 为准；源仓库文档**不在迁移期修改**（冻结纪律），归档时随 README 一并更正 |
| L2 | "系统设置"页若含报警阈值编辑，需要 AlarmConfig 的**写回保存**能力——服务层只有 `Load` 无 `Save`（AlarmConfig.cs） | 与"零服务改动"表述冲突 | 阈值编辑页 v1 先做**只读展示 + 打开配置文件目录**；写回保存作为带测试护航的后续增强，不并入 v1.0-wpf 验收线 |
| L3 | 若 Q4 选导航形态，连接/断开操作不得藏进设置页 | 通信是最高频操作 | 无论何种导航形态，【连接/断开】保持主屏可达（顶栏快捷按钮或通信卡保留操作按钮）——已写入概要 §7 Q4 |
| L4 | TestRunner"106 断言"口径混杂协议用例与 WinForms 探针 | 回归基线口径不清 | 拆分：协议级部分随迁 Core 并全绿；WinForms 探针留源仓库作对照；WPF 的 UI 层回归由 M3/M5 的 VM 测试 + 截图断言承担 |
