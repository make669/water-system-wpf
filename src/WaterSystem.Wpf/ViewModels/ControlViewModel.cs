using System.Collections.ObjectModel;
using System.Windows.Input;
using System.ComponentModel;
using System.IO;
using Intelligent_water_storage_system.Model;
using Intelligent_water_storage_system.Services;
using WaterSystem.Wpf.Binding;
using WaterSystem.Wpf.Infrastructure;

namespace WaterSystem.Wpf.ViewModels
{
    /// <summary>控制操作项（UI 列表用）：State 属性背后是 SwitchStateStore（按点 INPC 转发）。
    /// 构造即以 Control 类别登记 Tag（E08 数据源）。</summary>
    public sealed class ControlItemVm : INotifyPropertyChanged
    {
        private readonly SwitchStateStore _store;

        public string Label { get; }
        public string Tag { get; }

        /// <summary>开关视觉状态（OneWay 绑定；写入经 store → ControlViewModel 决策）。</summary>
        public bool State
        {
            get => _store[Tag];
            set => _store.Set(Tag, value);
        }

        public ControlItemVm(string label, string tag, SwitchStateStore store)
        {
            Label = label;
            Tag = tag;
            _store = store;
            store.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == $"SwitchStates[{Tag}]")
                    PropertyChanged?.Invoke(this, new(nameof(State)));
            };
            TagBindingRegistry.Register(tag, TagBindingKind.Control);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>
    /// 控制链路视图模型（T5.1~T5.4）：HandleToggle 唯一入口的 WPF 平移（FrmMain.cs:811-892）。
    /// 三阶段决策复用 ControlDecision 纯函数；确认/通知/连接态/写入均可注入（TC-V* 单测面）。
    /// 回写防护（迁移方案 §2.4）：层1=开关视觉 OneWay 绑 SwitchStates；层2=PLC 同步只走
    /// OnDataArrived（首包）不触发任何命令；层3=本方法乐观翻转 + 取消/失败回写旧值。
    /// 自翻转语义：WPF 中所有开关统一走"Switch 语义"（命令先置目标值，失败/取消回滚）——
    /// 当前态=未翻转旧值传入 ControlDecision，行为与源逐字等价。
    /// </summary>
    public sealed class ControlViewModel
    {
        private readonly LogService _log;
        private readonly ControlService _control;
        private readonly ModbusClient _client;
        private readonly List<TagAddress> _tags;
        private readonly Func<ConfirmRequest, bool> _confirm;
        private readonly Action<string> _notify;
        private readonly Func<bool> _isConnected;
        private bool _busy;
        private bool _syncDone;

        public SwitchStateStore SwitchStates { get; } = new();
        public int WriteCalls { get; private set; }
        public Func<string, bool, Task<bool>>? WriteOverride { get; set; }
        public ObservableCollection<ControlItemVm> ControlItems { get; }
        public ICommand ToggleCommand { get; }

        public ControlViewModel(LogService log, ControlService control, ModbusClient client,
            List<TagAddress> tags, MainViewModel main,
            Func<ConfirmRequest, bool>? confirm = null,
            Action<string>? notify = null,
            Func<bool>? isConnected = null)
        {
            _log = log;
            _control = control;
            _client = client;
            _tags = tags;
            _confirm = confirm ?? DefaultConfirm;
            _notify = notify ?? DefaultNotify;
            _isConnected = isConnected ?? DefaultIsConnected;

            ControlItems = new ObservableCollection<ControlItemVm>
            {
                new("1#水泵 启停", "Pump1_Run", SwitchStates),
                new("2#水泵 启停", "Pump2_Run", SwitchStates),
                new("3#水泵 启停", "Pump3_Run", SwitchStates),
                new("1# 手动/自动", "Pump1_Mode", SwitchStates),
                new("2# 手动/自动", "Pump2_Mode", SwitchStates),
                new("3# 手动/自动", "Pump3_Mode", SwitchStates),
                new("主阀门", "MainValve", SwitchStates),
                new("1# 支阀", "Valve1", SwitchStates),
                new("2# 支阀", "Valve2", SwitchStates),
                new("3# 支阀", "Valve3", SwitchStates),
            };
            ToggleCommand = new RelayCommand<string>(t => _ = ToggleTagAsync(t!));

            // 首包同步（回写防护层 2）：PLC 状态只在此处进开关视觉，不触发任何命令（TC-V7）
            main.InitialSync += data =>
            {
                if (_syncDone) return;
                _syncDone = true;
                foreach (var item in ControlItems)
                    if (data.TryGetValue(item.Tag, out var dp))
                        SetSwitch(item.Tag, dp.Value != 0);
                _log.Info("开关状态已同步 PLC（首包）");
            };
        }

        public bool GetSwitch(string tag) => SwitchStates[tag];

        private void SetSwitch(string tag, bool value) => SwitchStates.Set(tag, value);

        /// <summary>控制手势唯一入口（public 供单测与 UI 命令）。</summary>
        public async Task ToggleTagAsync(string tagName)
        {
            if (_busy) return;   // 防重入（源 _handlingToggle，FrmMain.cs:71,865）
            _busy = true;
            try
            {
                var tag = _tags.FirstOrDefault(t => t.TagName.Equals(tagName, StringComparison.OrdinalIgnoreCase));
                if (tag == null)
                {
                    _log.Error($"找不到标签: {tagName}");
                    return;
                }

                bool current = GetSwitch(tagName);
                var pre = ControlDecision.Precheck(_isConnected(), tag, ToggleControlKind.Switch, current);
                switch (pre.Action)
                {
                    case PlanAction.RejectNotConnected:
                        _notify("PLC 未连接，无法执行控制指令。");
                        return;
                    case PlanAction.RejectReadOnly:
                        _notify($"点位 {tagName} 为只读(R)，禁止写入。");
                        return;
                }

                bool isPump = tagName.Contains("Pump", StringComparison.OrdinalIgnoreCase);
                bool confirmed = _confirm(new ConfirmRequest(
                    Message: $"确定要{ControlDecision.DescribeAction(tagName, pre.TargetValue)} [{tagName}] 吗？",
                    TagName: tagName,
                    Description: tag.Description,
                    CurrentText: Describe(current, isPump),
                    TargetText: Describe(pre.TargetValue, isPump),
                    PlcText: $"{_client.IpAddress}:{_client.Port}"));
                var plan = ControlDecision.ResolveConfirmation(confirmed, ToggleControlKind.Switch, current);
                if (plan.Action == PlanAction.CancelNoop) return;

                SetSwitch(tagName, plan.TargetValue);   // 乐观翻转（自翻转等价，层 3）
                try
                {
                    bool ok = WriteOverride != null
                        ? await WriteOverride(tagName, plan.TargetValue)
                        : await _control.WriteCoilAsync(tagName, plan.TargetValue);
                    WriteCalls++;   // 取证计数（TC-V2/TC-V1）
                    if (ControlDecision.PostWrite(ok) == PlanAction.Rollback)
                    {
                        SetSwitch(tagName, current);
                        _log.Error($"控制失败已回滚 [{tagName}] → {current}");
                    }
                    else
                    {
                        _log.Info($"控制指令已下发 [{tagName}] → {(plan.TargetValue ? "ON" : "OFF")}");
                    }
                }
                catch (Exception ex)
                {
                    SetSwitch(tagName, current);
                    _log.Error($"控制异常已回滚 [{tagName}]: {ex.Message}");
                }
            }
            finally
            {
                _busy = false;
            }
        }

        private static string Describe(bool on, bool isPump)
            => isPump ? (on ? "运行" : "停止") : (on ? "开" : "关");

        private bool DefaultIsConnected() => _client.IsConnected;
        private static bool DefaultConfirm(ConfirmRequest r) => ViewModels.ConfirmDialog.Show(r);
        private static void DefaultNotify(string m)
            => System.Windows.MessageBox.Show(m, "提示", System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
    }
}
