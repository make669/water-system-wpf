# WPF 实施细则（任务分解 + 实现规格）

| 项 | 内容 |
|---|---|
| 文档版本 | v1.2 |
| 编写日期 | 2026-10-11 |
| 修订记录 | v1.2：过程卡改**管廊布局**（泵卡 240×356 @y36，上廊主管/下廊汇管，渲染实证修正）；M4 增 T4.0 引入 HandyControl（D11）；v1.1：git 纪律改每任务一条分支；v1.0：初版 |
| 定位 | 《迁移实施方案》= 战略（M0~M9）；《UI人性化设计概要》= 设计方向（P1~P18 + 概略图）；**本文 = 可执行任务书**：逐任务 T 编号、产出文件、三类验收、通过标准，以及代码级实现规格 |
| 使用方式 | 与源仓库《验收标准》同纪律：每完成一个任务按"验收方法"操作，全部满足"通过标准"才勾选进下一任务；证据贴任务完成记录 |
| 上游依据 | 迁移方案 §8 实测结论（抽取源=HEAD `098df6d`、110/110 基线、TestRunner 拆分）；概要 §4.4 深色色板 |

---

## 0. 任务总览与依赖

```
M0 仓库基线
└─ M1 Core 抽取（T1.1~T1.5）
   └─ M2 WPF 骨架（T2.1~T2.6）
      ├─ M3 监视链路（T3.1~T3.8）
      │   └─ M5 控制链路（T5.1~T5.7）← 依赖 M3 的 TagValues/徽章
      ├─ M4 界面重构（T4.0~T4.11）  ← 依赖 M2 骨架，与 M3 并行可
      │   └─ M6 报警日志（T6.1~T6.5）
      │        └─ M7 趋势（T7.1~T7.3）
      └─ M8 持久化（T8.1~T8.2）
         └─ M9 验收归档（T9.1~T9.4）
```

## 1. 文件清单总表（哪些里程碑创建哪些文件）

| 里程碑 | 新增/修改文件（相对 water-system-wpf 仓库根） |
|---|---|
| M0 | `.gitignore`、`docs/`（两份既有文档入库）、`README.md`（骨架） |
| M1 | `src/WaterSystem.Core/**`（26 个 .cs + Core.csproj + PROVENANCE.md）、`tests/WaterSystem.Core.Tests/**`（9 个 .cs + csproj）、`configFiles/`（3 件）、`tools/ModbusSlaveSim/**`、`tools/TestRunner/**`（仅协议级部分，§8-3） |
| M2 | `src/WaterSystem.Wpf/App.xaml(.cs)`、`Views/MainWindow.xaml(.cs)`、`ViewModels/MainViewModel.cs`、`Infrastructure/UiDispatcher.cs`、`Binding/TagBinding.cs`、`Binding/TagBindingRegistry.cs` |
| M3 | `Controls/ParaBox.xaml(.cs)`、`Controls/StatusBadge.xaml(.cs)`、`Controls/CardChrome.cs`、`ViewModels/CommViewModel.cs`（并入 MainViewModel 亦可）、`Themes/Dark.xaml`（色板资源字典） |
| M4 | `Controls/RingGauge`、`Controls/LevelTank`、`Controls/PumpGraphic`、`Controls/PipeLine`、`Controls/USwitchW`、`Controls/UJogSwitchW`、`Controls/URotarySwitchW`、`Controls/AlarmBadgeChip`、`Views/ProcessCanvas.xaml`、`Views/Panels/*.xaml`（右栏三卡）、`ViewModels/ProcessViewModel.cs`、`ViewModels/AlarmPanelViewModel.cs`（+ NuGet `HandyControl` 3.5.1 基础件壳，D11） |
| M5 | `ViewModels/ControlViewModel.cs`（ToggleCommand + 防护）、`Views/ConfirmDialog.xaml(.cs)`、`tests/WaterSystem.Wpf.Tests/ControlViewModelTests.cs` 等 |
| M6 | `Views/LogViewerWindow.xaml(.cs)`、`Views/LogDetailDialog.xaml(.cs)`、`ViewModels/LogViewerViewModel.cs`、`Services/LogRowStyler.cs`（着色唯一出处） |
| M7 | `Controls/WaterTrendChart.xaml(.cs)` |
| M8 | `State/AppStateStore.cs`（app_init.json 读写） |
| 测试工程 | `tests/WaterSystem.Wpf.Tests/`：`WriteBackGuardTests.cs`、`ToggleFlowTests.cs`、`ValueFormatterTests.cs`、`TagBindingRegistryTests.cs`、`TrendWindowTests.cs`、`AppStateStoreTests.cs` |
| Core 增量 | `Utils/ValueFormatter.cs`（M3）、`DataPollingService.ReconnectAttempts` 只读属性（M3，1 行） |

---

## 2. 逐任务分解（T 编号，三类验收沿用源仓库口径）

> 格式对齐源仓库《验收标准》：每任务 = 验收方式 [自动/操作/检查] + 验收方法 + 通过标准。

### M0 仓库基线

| T | 内容 | 验收 | 通过标准 |
|---|---|---|---|
| T0.1 | `git init` + `.gitignore`（bin/obj/.vs/logs/data/*.db/app_init.json/*.user）+ 首提交（两份文档 + 本细则入库） | [检查] | `git log` 恰 1 提交；build 后 `git status` 零未跟踪产物 |
| T0.2 | 打 tag `v0.0-baseline` | [检查] | `git tag` 含 v0.0-baseline |

### M1 Core 抽取（已在 2026-10-11 沙箱预演通过，迁移方案 §8-4）

| T | 内容 | 验收 | 通过标准 |
|---|---|---|---|
| T1.1 | 按**迁移方案附录 A** 从 HEAD `098df6d` 复制 26 文件 → `src/WaterSystem.Core`；LogService 单行补丁；`PROVENANCE.md`（源 hash + 清单 + 补丁说明） | [检查] | 文件数 26；`git show 098df6d --stat` 可追溯；补丁仅 1 处 |
| T1.2 | `configFiles` 三件复制 + Core.csproj 无 UI 引用 | [自动] | `dotnet build src/WaterSystem.Core` 0 错误；grep Forms/Zhaoxi = 0 |
| T1.3 | 110 项测试迁入 `WaterSystem.Core.Tests`（net8.0） | [自动] | `dotnet test` = 110/110（对照源仓库实测 110/110） |
| T1.4 | TestRunner 协议级部分随迁（拉起 ModbusSlaveSim） | [自动] | 协议用例全绿；WinForms 探针不随迁（记录在 PROVENANCE） |
| T1.5 | tag `v0.1-core` | [检查] | — |

### M2 WPF 骨架

| T | 内容 | 验收 | 通过标准 |
|---|---|---|---|
| T2.1 | `Dark.xaml` 色板资源字典（概要 §4.4 全部令牌） | [检查] | 令牌键名与概要 §4.4 一一对应 |
| T2.2 | App 启动链按 §3.1 顺序：TagTable fail-fast → AlarmConfig fail-fast → 组合根 → MainWindow；`DispatcherUnhandledException` 兜底 | [操作] | 注入 9 类标签表错误逐一"弹错误清单 + 退出"；正常启动出空主窗 |
| T2.3 | MainWindow：无边框 1492×1000 + DragMove + Closing（停轮询/停模拟/存状态占位） | [操作] | 拖动流畅；关闭无异常；1 行 INFO 日志"会话结束" |
| T2.4 | `UiDispatcher`（§3.2） | [自动] | 后台线程调 `Ui.Post` 后 UI 线程执行（测试用 SynchronizationContext 桩） |
| T2.5 | TagBinding 注册表（§3.4）+ 单测 | [自动] | 注册/去重/控制显示分类正确；`TagBindingRegistryTests` 全绿 |
| T2.6 | tag `v0.2-shell` | [检查] | — |

### M3 监视链路

| T | 内容 | 验收 | 通过标准 |
|---|---|---|---|
| T3.1 | MainViewModel 订阅四事件 + Dispatcher 封送（§3.3 成员表） | [自动] | VM 单测：模拟 OnDataReceived → TagValues 更新 |
| T3.2 | Core 增量：`ValueFormatter`（§3.7）+ `ReconnectAttempts` 只读属性 | [自动] | TC-F1~F5 全绿；属性返回退避计数 |
| T3.3 | 通信卡两态 UI（概要 §3.4）：标签同排、按钮互斥启用、busy 态 | [操作] | 未连接【断开】禁用；连接中【连接】变"连接中…"；IP/Port 标签与框同行（P3 关闭） |
| T3.4 | ParaBox 控件（§4.2-1）+ TagValues 绑定 | [操作] | 模拟器 `set CurWaterPos 0.8` → ≤2s 显示 `0.8 m`（P1 格式化生效） |
| T3.5 | 状态徽章带（§4.2-9）：7 组徽章文字动态化 | [操作] | 阀徽章显示"开/关"（P6 关闭）；泵徽章三态 |
| T3.6 | 模拟模式：ToggleSim 时序照抄 FrmMain.cs:467-516 | [操作] | 一键起从站 1502 → 自动连接 → 36 点上屏；再点停止 |
| T3.7 | 空态复位 + 200ms 看门狗（数据过期部分） | [操作] | `mute on` → 通信中断徽章红 + 界面数据冻结不消失；`mute off` → 恢复；断开 → 数值归零灯全灭（P4/G5） |
| T3.8 | tag `v0.3-monitor` | [检查] | — |

### M4 人性化界面重构（坐标总表见 §4.1，控件规格 §4.2）

| T | 内容 | 验收 | 通过标准 |
|---|---|---|---|
| T4.0 | 引入 HandyControl 3.5.1（NuGet，已实测可还原/编译，迁移方案 §8-9）+ 深色令牌对齐（D11，概要 §4.4 → HC 资源键映射表） | [操作] | HC 深色下 Card/Button/TextBox/DataGrid 表头无白底残留（PoC 已证默认表头为白） |
| T4.1 | 九分区骨架 + CardChrome（§4.1） | [检查] | 截图对照概要 §2；分区预算逐项核对一致；零遮挡零溢出 |
| T4.2 | 过程画面：管廊布线 + PipeLine 动画（§4.2-8） | [操作] | 五态验证（全停/泵转无阀/开阀泵转/泵停阀开/全停）截图；**陌生测试**：每个图形对象有名称标签（蓄水池/主管/总阀门/启停/汇管→主管输出/支阀）且工艺图例常驻（设计书 §1.0/§2.2） |
| T4.3 | LevelTank 水池（§4.2-6） | [操作] | 液位高度=CurWaterPos/6.0；越限变色（阈值=AlarmConfig） |
| T4.4 | PumpGraphic + 泵卡"卡中卡"（§4.2-7） | [操作] | 运行叶轮转 + 变绿；故障红 + 卡底「▲故障」徽章（P5） |
| T4.5 | RingGauge 环形仪表 ×4（§4.2-5），量程=TagTable Min/Max | [操作] | 实际用水 85.4 指在 0~500 的 17% 位置（P7 关闭）；越限红 |
| T4.6 | 报警卡 + 顶栏徽标（概要 §3.3） | [操作] | surge on → 三处同时可见（顶栏/报警卡/过程图描红）；P4 关闭 |
| T4.7 | 趋势卡阈值线/单位/图例 | [操作] | Y 轴"m"；1.0/6.0 红虚线；图例"水位"（P10 关闭） |
| T4.8 | 日志卡加高（≥6 行）+ 列宽防折行 | [检查] | 列头"级别"单行（P11 关闭） |
| T4.9 | 退出确认 + 深色统一检查 | [操作] | 关闭弹确认（P14）；全窗无残留浅色控件 |
| T4.10 | 系统说明窗（顶栏 ⓘ）+ 陌生人测试复核 | [操作] | ⓘ 打开系统说明（系统名称/监控对象 36 点摘要/通信方式 Modbus TCP/当前连接/版本/日志目录）；设计书 §1.0 四问逐条回答 |
| T4.11 | tag `v0.4-graphics` | [检查] | — |

### M5 控制链路

| T | 内容 | 验收 | 通过标准 |
|---|---|---|---|
| T5.1 | ToggleCommand 全流程（§3.6 伪代码） | [自动] | TC-V2/V5/V6 全绿 |
| T5.2 | 回写防护（§3.5 三层） | [自动] | **TC-V1：轮询 100 轮写调用数=0**；TC-V7 首包同步零写 |
| T5.3 | ConfirmDialog（§3.6：上下文 + 默认焦点取消） | [操作] | 文案与源逐字一致；回车=取消；P16 关闭 |
| T5.4 | 写失败回滚 + 防重入 | [自动] | TC-V3/V4 全绿；日志文案"控制失败已回滚 [tag] → 旧值" |
| T5.5 | E08/E09 接入 TagBinding 注册表 | [操作] | 删 Valve2 行 → 启动报 E09 指名控件；加 TestTag → W01 仅警告 |
| T5.6 | **端到端回写风暴专项**：模拟器 wave 轮询 60s，从站侧 0x05 计数 | [自动] | 计数=0 才允许出阶段（迁移方案 §2.4） |
| T5.7 | tag `v0.5-control` | [检查] | — |

### M6 报警与日志

| T | 内容 | 验收 | 通过标准 |
|---|---|---|---|
| T6.1 | LogRowStyler（着色唯一出处，主卡与 F12 共用） | [检查] | 色值 = FrmMain.cs:268-294 移植 |
| T6.2 | 报警卡行色（红/黄/灰 = ISA-18.2 四态） | [操作] | surge → 红；确认 → 黄；恢复 → 灰 |
| T6.3 | F12 LogViewerWindow 全功能平移 | [操作] | 筛选/关键字/自动滚动/暂停/导出/清屏/双击详情/确认报警/打开目录 |
| T6.4 | SQLite 闭环 | [操作] | surge → 确认 → 重启 → history.db 可查 |
| T6.5 | tag `v0.6-alarm` | [检查] | — |

### M7 趋势 / M8 持久化

| T | 内容 | 验收 | 通过标准 |
|---|---|---|---|
| T7.1 | WaterTrendChart（§4.2-11） | [自动] | TC-T1：301 点滚动后首点移除；取不到系列显式 ERROR（B10 教训） |
| T7.2 | `scenario wave` 6 分钟 | [操作] | 曲线连续无跳变；断线停更恢复续画 |
| T8.1 | AppStateStore（§3.8 schema）+ Closing 保存/启动恢复 | [操作] | 开泵1改IP → 重开恢复开关与 IP、数值全零；坏 JSON 仅 WARN |
| T8.2 | tag `v0.8-persist` | [检查] | — |

### M9 定稿验收

| T | 内容 | 验收 | 通过标准 |
|---|---|---|---|
| T9.1 | 全量回归矩阵（迁移方案 §5.2） | [自动+操作] | 全绿 |
| T9.2 | 劣化演练：frag/mute/surge/fault/drop 各留证据 | [操作] | 每项"截图+日志行" |
| T9.3 | 浸泡 ≥2h | [自动] | 内存平稳、无冻结、零 ERROR 增长 |
| T9.4 | 截图包 + tag `v1.0-wpf` + 源仓库 README 归档注记（唯一允许的源仓库改动） | [检查] | — |

---

## 3. 关键实现规格（代码级）

### 3.1 App 启动顺序（对应 FrmMain_Load 平移，FrmMain.cs:140-183）

```text
App.OnStartup:
 1. 注册 DispatcherUnhandledException → LogService.Error + 弹窗（不静默退出）
 2. tags = TagTableLoader.Load(BaseDirectory/configFiles/TagTable.csv)
    ├─ errors>0 → FailFastWindow("标签表校验失败…E01~E09 全量") → Shutdown(1)   ← 对应 FrmMain.cs:309-318
 3. cfg = AlarmConfig.Load(…AlarmConfig.json)
    ├─ errors>0 → FailFastWindow("报警配置校验失败 [A01]~[A09]") → Shutdown(1)
 4. alarmCenter = new AlarmCenter(BaseDirectory/data/history.db)
 5. comm 配置读取（Utility.LoadCommunicationConfig）→ 组合根:
    ModbusClient → DataPollingService(SetTags) → ControlService → AlarmService(cfg)
    （与 FrmMain.cs:366-387 同构；订阅关系见 §3.3）
 6. MainWindow = new(); DataContext = mainViewModel; Show()
 7. mainViewModel.OnLoaded(): 恢复持久化 → ResetDisplayValues → 启动看门狗   ← 空态归零先于一切显示
```

### 3.2 UiDispatcher（D9：只允许 Post，禁止同步 Invoke）

```csharp
public static class Ui {
    public static void Post(Action a) =>
        Application.Current.Dispatcher.InvokeAsync(a, DispatcherPriority.Normal);
    // 禁止提供同步 Invoke 包装 —— 防 R4 死锁（代码评审检查项）
}
```

### 3.3 MainViewModel 成员总表（订阅关系与 FrmMain 字段一一对应）

| 成员 | 类型 | 来源/对应 |
|---|---|---|
| ConnState | enum{Disconnected, Connecting, Connected, Reconnecting} | label3 三态 + 重连计数（P13） |
| ReconnectAttempts | int（透传只读属性） | Core 新增量 |
| IpText / PortText | string | textBox1/2，通信卡两态共享 |
| SimRunning | bool | _btnSim.Text 切换 |
| TagValues | `ITagValues`（string 索引器 + per-tag INPC） | _currentData 字典（FrmMain.cs:68） |
| Pumps[3] / Valves[4] | 状态结构（Run/Fault/IsOpen） | AlarmService SystemStatus（FrmMain.cs:647-663 只留报警本体） |
| WaterLevel | double | CurWaterPos（液位/趋势共用同一来源，P8） |
| TrendPoints | 环形缓冲 300（IRoObservable） | _waterHistory（FrmMain.cs:85） |
| OpenAlarms / UnackCount | 集合/计数 | AlarmCenter.OpenSnapshot/UnacknowledgedCount（徽标） |
| LogEntries | BindingList<LogEntry>（LogService.Entries 直绑，D8） | dataGridView1 |
| 命令 | Connect / Disconnect / ToggleSim / ToggleTag(tag) / Ack(id) / AckAll / Mute / Exit | uButton1/2/HandleToggle/报警按钮/退出确认 |

订阅（全部经 `Ui.Post` 封送）：`polling.OnDataReceived → OnData`；`OnConnectionChanged / OnError / OnQualityChanged → 状态与空态复位`；`alarmService.OnStatusChanged → 报警增量→AlarmCenter`；`alarmCenter.OnAlarmChanged → 徽标/面板刷新`。

### 3.4 TagBinding 注册表（E08/E09 的 WPF 数据源）

```xml
<!-- 用法：任何与 Tag 挂钩的绑定都经此扩展，解析时自动注册 -->
<TextBlock Text="{ui:TagBinding Pump1_F, Mode=Display}" />
<controls:USwitchW ui:TagBinding.Name="Valve1" ui:TagBinding.Kind="Control" … />
```

```csharp
public static class TagBindingRegistry {
    // ProvideValue 时登记 (name, kind)；MainWindow.Loaded 后由 MainViewModel 调 Check：
    public static IReadOnlySet<string> ControlNames { get; }
    public static IReadOnlySet<string> DisplayNames { get; }
    public static (List<string> errors, List<string> warnings) Check(IEnumerable<TagAddress> tags)
        => TagTableLoader.CheckUiBindings(tags, ControlNames, DisplayNames, …);  // 签名天然匹配，TagTableLoader.cs:254
}
```
测试断言：给定绑定集合 → E09/E08/W01 逐码命中（防"漏注册→校验假绿"，迁移方案 R3）。

### 3.5 回写防护三层（对应 WinForms E1 语义，迁移方案 §2.4）

| 层 | 实现 | 对应源证据 |
|---|---|---|
| 1 单一属主 | 开关控件视觉一律 OneWay 绑 VM；控件只发 `UserToggled` 手势，不反向写 | E1a：编程赋值不触发事件（FrmMain.cs:629-631） |
| 2 同步/手势分流 | `_syncingFromPlc` 抑制标志：OnData 路径先置 true 再写 TagValues/状态 | _handlingToggle（FrmMain.cs:71,865） |
| 3 自翻转等价 | 手势命令先置目标值（对应 Jog/Rotary 自翻转），取消/失败回写旧值 | ControlDecision.RevertNeeded/Rollback |

**专项测试用例表**（`WriteBackGuardTests` / `ToggleFlowTests`，ControlService 用 mock 计数）：

| TC | 步骤 | 断言 |
|---|---|---|
| TC-V1 | 模拟 PLC 数据 100 轮写入 TagValues（每轮泵状态翻转） | WriteCoilAsync 调用数 == 0 |
| TC-V2 | Connected + 用户手势 ToggleTag("Pump1_Run") + 确认=true | 写调用 == 1（值=true）；VM 状态=ON |
| TC-V3 | 同上但确认=false | 写调用 == 0；Jog/Rotary 状态回翻；USwitch 状态不变 |
| TC-V4 | 确认=true 且写返回 false | 状态回滚旧值；日志含"控制失败已回滚 [Pump1_Run]" |
| TC-V5 | Disconnected 下手势 | 不弹确认、无写；状态回翻；提示"PLC 未连接" |
| TC-V6 | 对 Access=R 的标签手势 | 无写；提示只读拒绝 |
| TC-V7 | 首包同步（_initialSyncDone 语义） | 开关视觉=PLC 值；写调用数 == 0 |
| TC-V8 | OnQualityChanged(false) | 空态复位被调用：徽章全灰、管道停、泵图复位 |
| TC-V9 | LastGoodPollAt 距今 >3×1s | ConnState=Reconnecting/中断徽章红 |

### 3.6 ToggleCommand 全流程伪代码（对齐 HandleToggle，FrmMain.cs:811-892）

```text
ToggleTag(tag):
  if busy: return                                    # _handlingToggle 等价
  tagAddr = tags[tag] ?? → Log ERROR "找不到标签" return
  plan = ControlDecision.Precheck(IsConnected, tagAddr, kind(tag), visualState(tag))
  switch plan.Action:
    RejectNotConnected/RejectReadOnly → revert if needed → 提示 → return
  confirmed = ConfirmDialog(标题"操作确认",
      文案 = ControlDecision.DescribeAction(tag, plan.TargetValue) + " [" + tag + "] 吗？",
      上下文行 = 描述/当前值/目标值/PLC 地址, 默认焦点 = 取消)             # P16
  plan2 = ControlDecision.ResolveConfirmation(confirmed, kind(tag), visualState(tag))
  if plan2.Action == CancelNoop: revert if needed; return
  old = 当前视觉状态; 置视觉 = plan2.TargetValue                       # 自翻转等价（层3）
  try:
    ok = await controlService.WriteCoilAsync(tag, plan2.TargetValue)
    if ControlDecision.PostWrite(ok) == Rollback: 置视觉 = old; Log ERROR 回滚
    else: Log INFO "控制指令已下发 [tag] → ON/OFF"
  catch ex: 置视觉 = old; Log ERROR "控制异常已回滚"
  finally: busy = false
```

### 3.7 ValueFormatter 规格（Core 新增纯函数 + 测试）

```csharp
public static class ValueFormatter {
    /// 小数位 = Scale 的十进制有效位数（0.1→1 位，0.01→2 位，1→0 位）
    public static int DecimalsOf(double scale);
    /// 输出 "3.60" 这类定宽文本（CultureInfo.InvariantCulture）
    public static string Format(double raw, double scale);
}
```
| TC | 输入 | 期望 |
|---|---|---|
| TC-F1 | (3.59625473, 0.1) | "3.6" |
| TC-F2 | (12.0, 0.01) | "12.00" |
| TC-F3 | (0, 1.0) | "0" |
| TC-F4 | (85.43989, 0.1) | "85.4"（不再截断，P1） |
| TC-F5 | (0.05, 0.1) | "0.1"（MidpointRounding.AwayFromZero，现场读数习惯） |

### 3.8 持久化 schema（app_init.json v2，语义对齐 Initializer）

```json
{ "version": 2,
  "comm": { "ip": "127.0.0.1", "port": 502 },
  "switches": { "Pump1_Run": false, "MainValve": true, "Valve1": false, "Valve2": false, "Valve3": false,
                "Pump1_Mode": false, "Pump2_Mode": false, "Pump3_Mode": false } }
```
- 只存**开关语义状态**（按 TagName），不存显示值（对应 FrmMain.cs:179-181"历史快照不得当事实显示"）；坏文件 → WARN 不阻断。

---

## 4. M4 界面实施规格

### 4.1 固定坐标总表（1492×1000；精确到分区，管路布线允许实施期微调）

| 分区 | X | Y | W | H | 内部要点 |
|---|---|---|---|---|---|
| 顶栏 | 0 | 0 | 1492 | 40 | 左：标题(16,9)；中左：通信 Chip；中：报警徽章；右：静音/时钟(14px)/窗控 3×32 |
| 过程画面卡 | 12 | 52 | 1078 | 470 | 见下"过程卡内部" |
| 状态徽章带 | 12 | 530 | 1078 | 110 | 7 组徽章，每组 140×72，行内间距 12，起点 (24,19) |
| 用水卡 | 12 | 648 | 322 | 180 | 完成率环 96×96 (24,42)；计划/实际键值两行 (136,44/88) |
| 趋势卡 | 342 | 648 | 748 | 180 | 绘图区 (12,34) 724×112 + 底部时间轴 22 |
| 报警卡 | 1102 | 52 | 384 | 330 | 卡头 24 + 列头 26 + 行区 240（行高 24×10）+ 按钮条 36；列宽 内容Fill/52/48/96 |
| 环境卡 | 1102 | 390 | 384 | 250 | 2×2 环形仪表，单元 168×104，内边距 12，间距 12 |
| 通信卡 | 1102 | 648 | 384 | 180 | 输入态：IP 行(16,40)/端口行(16,84)/按钮行(16,128)；键值态：4 行键值(行高 28) + 按钮行 |
| 日志卡 | 0 | 836 | 1492 | 164 | 列：时间 100/级别 64/消息 Fill；列头 26 + 行高 22×6；右上角 [F12 全量日志] |
| （Q4-B 导航条） | 0 | 952 | 1492 | 48 | 日志 164→116；图标+文字三项 |

**预算核对**（与概要 §2 相同口径）：水平 12+1078+12+384=1486≤1492 ✓；左列 52+470=522→530+110=640→648+180=828 ✓；右列 52+330=382→390+250=640→648+180=828 ✓；日志 836..1000 ✓；导航变体：日志 836..952（116=26+4×22+18）✓。

**过程卡内部（相对卡左上，1078×470，固定骨架 + 管路微调区）**：

| 元素 | 相对位置 | 说明 |
|---|---|---|
| 上管廊 | (0,10) 1078×26 | 主管横贯 y=16（主阀阀体 x=300）；供水短柱 x=468/714/960 ↓ |
| 水源子区 | (12,36) 300×360 | LevelTank (16,14) 210×170；水位数字 (16,196)；主管压力/流量 ParaBox (16,236/276)；主阀 USwitchW (16,320) |
| 泵卡×3 | (324/570/816, 36) 各 240×356 | 旋钮 (16,14) 56×56；PumpGraphic (96,8)；F/P/S 键值 (16,112) 行高 36；双徽章 (16,228)；支阀 (16,272) |
| 下管廊 | (0,392) 1078×50 | 汇管 y=416 + 出口短柱 ↓；主管出口 x≈1100 |
| 管路布线 | 管廊内按《详细设计书》§2.2 段表 | 方向逐段显式指定；五态验证为准（T4.2） |

### 4.2 自绘控件规格表（DependencyProperty 名与 Zhaoxi 对齐处加粗）

| 控件 | DP / 事件 | 视觉与状态 |
|---|---|---|
| 1 ParaBox | **Value**(double)、TagName、Unit、Decimals(默认由 Scale 推导) | 标签 12px TextSecondary + 数值 22px TextPrimary(tabular) + 单位 12px；越限（Min/Max 或报警联动）数值转 Alarm；高 32 |
| 2 USwitchW（阀） | **IsOn**、TagName、事件 UserToggled | 44×24 胶囊：On=Ok 底+圆点右，Off=Off 底+圆点左；**视觉 OneWay 绑 VM**（§3.5 层1） |
| 3 UJogSwitchW（球灯/状态） | **IsOn**、Mode=Display\|Control | Display 模式不挂手势（球灯纯显示语义，uJogSwitch7-10 对应物） |
| 4 URotarySwitchW（旋钮） | **SwitchState**、事件 UserToggled | 56×56 旋柄 + 90° 转角动画；点击切换 |
| 5 RingGauge | Value/Min/**Max**/Unit/Label | 100×100：暗环 #223047 270° + Accent 进度弧 + 中心数值 18px；越限→弧与数值 Alarm |
| 6 LevelTank | Level(0..1)、LowAt/HighAt、ValueText | 220×200 圆角槽 + 液位矩形（蓝 20% 透明 + 顶波动画）；越下/上限→液位 Alarm 色（阈值=AlarmConfig） |
| 7 PumpGraphic | RunState=Stopped\|Running\|Fault | 泵体 Path：Stopped=Off、Running=PipeRun 绿+叶轮 1s/圈旋转、Fault=Alarm 红闪烁（1.5s）；**值不变不重启动画**（B8 相位教训） |
| 8 PipeLine | **Active**、FlowSpeed、Direction、Orientation | 底管 Stroke #334155 6px + 流动层 #3FB950 虚线 DashArray"6 8"，Active 时 DashOffset 0→-14 / 0.6s 循环；**仅 Active 翻转时启停动画** |
| 9 StatusBadge | State、Label | 圆点 10px + Label 12px + 状态词 13px SemiBold（泵：运行/停止/故障；阀：开/关，修 P6） |
| 10 CardChrome | Title 附加属性 | Border 圆角 8 + 底 #131C2E + 边线 #223047 + 卡底标签条 #0E1626 高 26 |
| 11 WaterTrendChart | Points(ReadOnly)、YMin/YMax、ThresholdLow/High | Polyline 灰蓝 + 阈值红虚线 + Y 轴"m" + 图例；**系列缺失→Log ERROR**（B10） |
| 12 AlarmBadgeChip | Text、Tone=Alarm\|Ack\|Recover\|Off | 实心圆角 4，背景随 Tone，深色前景 12px（图①「▲温度告警」模式） |

### 4.3 字号 / 间距体系

标题 16 SemiBold｜卡头 13｜数值 22 tabular｜环形中心值 18｜标签/徽章/日志 12｜时钟 14。卡间距 8｜卡内边距 12｜圆角 8｜边线 1。深底正文对比 ≥4.5:1（概要 §4.4）。

### 4.4 MainWindow XAML 骨架（树状示意）

```
Window (WindowStyle=None, 1492×1000, Background=BgWindow)
└─ Grid
   ├─ 顶栏 Grid (Row0, 40)
   ├─ Canvas (Row1)                      ← 设计稿画布（D5）
   │  ├─ CardChrome "过程画面" → ProcessCanvas (UserControl, 管路+泵卡)
   │  ├─ 状态徽章带 StackPanel
   │  ├─ CardChrome "用水" / "趋势"
   │  ├─ CardChrome "实时报警" → AlarmPanel
   │  ├─ CardChrome "环境" (RingGauge×4)
   │  ├─ CardChrome "通信" (两态 ContentControl)
   │  └─ CardChrome "日志" (DataGrid)
   └─ (可选) 导航条 (Row2)
```

---

## 5. 新增测试用例总表（汇总）

| 文件 | 用例 | 条数 |
|---|---|---|
| Core.Tests/ValueFormatterTests | TC-F1~F5 | 5 |
| Core.Tests/DataPollingServiceTests（增） | ReconnectAttempts 退避序列 1/2/4/8/16/30 封顶 | 2 |
| Wpf.Tests/TagBindingRegistryTests | E09/E08/W01 逐码 + 去重 | 4 |
| Wpf.Tests/WriteBackGuardTests | TC-V1/V7/V8/V9 | 4 |
| Wpf.Tests/ToggleFlowTests | TC-V2/V3/V4/V5/V6 | 5 |
| Wpf.Tests/TrendWindowTests | TC-T1（301 点滚动） | 1 |
| Wpf.Tests/AppStateStoreTests | schema 往返 + 坏文件降级 | 2 |
| **合计新增** | | **23** |

---

## 6. git 操作模板（**每个任务一条分支，验收达标才并入 master**——用户裁定 2026-10-11）

| 节拍 | 规则 |
|---|---|
| 开任务 | 从**最新 master** 切出 `feature/T{编号}-{短名}`（例：`feature/T4.3-level-tank`）——**每个 T 任务一条分支，禁止跨任务共用** |
| 开发 | 任务内提交：`{T编号} {一句话}（证据：{测试输出行/截图名/日志行}）`，例：`T5.2 回写防护：TC-V1 轮询100轮写调用=0（dotnet test Passed! 4/4）` |
| 自验 | 完成后执行该任务"验收"栏全部条目，证据贴合并描述 |
| 合并 | **验收达标 → 才允许 merge 回 master**（merge commit 注明 T 编号 + 证据摘要）；未达标不得合并——修不动则整支废弃（`git branch -D`），不允许"先合后补" |
| 里程碑 | 阶段内全部 T 任务合并后打 tag `v0.x-*`（§2 各表末行） |
| 同步 | 下一任务必须基于最新 master 切出（防长分支漂移） |
| 禁止 | 直接向 master 提交；合并后补证据；跳过自验合并 |
