using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using System.Windows.Input;
using Intelligent_water_storage_system.Model;
using Intelligent_water_storage_system.Services;
using Intelligent_water_storage_system.Simulation;
using Intelligent_water_storage_system.Utils;
using WaterSystem.Wpf.Infrastructure;

namespace WaterSystem.Wpf.ViewModels
{
    /// <summary>通信状态（对应源 label3 三态 + 重连/中断细分，P13）。</summary>
    public enum CommState { Disconnected, Connecting, Connected, Reconnecting, Stale }

    /// <summary>状态徽章行（Label 固定，Word/Tone 随数据变化——修 P6"灯文字随状态变"）。</summary>
    public sealed class BadgeVm : INotifyPropertyChanged
    {
        private string _word = "○ 停止";
        private string _toneKey = "Off";

        public string Label { get; }
        public string Word { get => _word; set { _word = value; PropertyChanged?.Invoke(this, new(nameof(Word))); } }
        public string ToneKey { get => _toneKey; set { _toneKey = value; PropertyChanged?.Invoke(this, new(nameof(ToneKey))); } }
        public event PropertyChangedEventHandler? PropertyChanged;

        public BadgeVm(string label) => Label = label;
    }

    /// <summary>
    /// 主视图模型：状态唯一属主 + 行为编排（L5）。订阅轮询四事件，UiDispatcher 封送（D9）；
    /// PLC 数据同步只写 TagValueStore/徽章（回写防护层 1+2，迁移方案 §2.4）；
    /// 看门狗与空态复位对应 FrmMain.cs:568-625/927-950/761-794。
    /// </summary>
    public sealed class MainViewModel : INotifyPropertyChanged
    {
        private const int SimPort = 1502;

        private readonly LogService _log;
        private readonly ModbusClient _client;
        private readonly DataPollingService _polling;
        private readonly ControlService _control;
        private readonly AlarmService _alarmEval;
        private readonly List<TagAddress> _tags;
        private readonly Func<DateTime> _clock;

        private bool _initialSyncDone;
        private bool _qualityOk = true;
        private CancellationTokenSource? _simCts;
        private Task? _simTask;
        private DataStore? _simStore;
        private bool _connecting;

        public TagValueStore TagValues { get; } = new();
        public ObservableCollection<BadgeVm> Badges { get; }
        public LogService Log { get; }

        /// <summary>首包同步事件（ControlViewModel 订阅以对齐开关状态；只触发一次）。</summary>
        public event Action<Dictionary<string, DataPoint>>? InitialSync;

        /// <summary>控制链路视图模型（组合根注入；开关视觉绑定经 ControlVm.SwitchStates）。</summary>
        public ControlViewModel? ControlVm { get; set; }

        #region 通知属性

        private CommState _connState = CommState.Disconnected;
        public CommState ConnState
        {
            get => _connState;
            private set
            {
                if (_connState == value) return;
                _connState = value;
                PropertyChanged?.Invoke(this, new(nameof(ConnState)));
                PropertyChanged?.Invoke(this, new(nameof(ConnStateText)));
                PropertyChanged?.Invoke(this, new(nameof(ConnToneKey)));
            }
        }

        private string _connStateText = "等待连接";
        public string ConnStateText
        {
            get => _connStateText;
            private set { _connStateText = value; PropertyChanged?.Invoke(this, new(nameof(ConnStateText))); }
        }

        private string _connToneKey = "Off";
        public string ConnToneKey
        {
            get => _connToneKey;
            private set { _connToneKey = value; PropertyChanged?.Invoke(this, new(nameof(ConnToneKey))); }
        }

        private string _ipText = "127.0.0.1";
        public string IpText { get => _ipText; set { _ipText = value; PropertyChanged?.Invoke(this, new(nameof(IpText))); } }

        private string _portText = "502";
        public string PortText { get => _portText; set { _portText = value; PropertyChanged?.Invoke(this, new(nameof(PortText))); } }

        private bool _simRunning;
        public bool SimRunning
        {
            get => _simRunning;
            private set
            {
                _simRunning = value;
                SimButtonText = value ? "停止模拟" : "模拟";
                PropertyChanged?.Invoke(this, new(nameof(SimRunning)));
            }
        }

        private string _simButtonText = "模拟";
        public string SimButtonText
        {
            get => _simButtonText;
            private set { _simButtonText = value; PropertyChanged?.Invoke(this, new(nameof(SimButtonText))); }
        }

        #endregion

        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ICommand ToggleSimCommand { get; }

        public event PropertyChangedEventHandler? PropertyChanged;

        public MainViewModel(LogService log, ModbusClient client, DataPollingService polling,
            ControlService control, AlarmService alarmEval, List<TagAddress> tags,
            bool startWatchdog = true, Func<DateTime>? clock = null)
        {
            _log = log;
            Log = log;
            _client = client;
            _polling = polling;
            _control = control;
            _alarmEval = alarmEval;
            _tags = tags;
            _clock = clock ?? (() => DateTime.Now);

            Badges = new ObservableCollection<BadgeVm>
            {
                new("1#水泵"), new("2#水泵"), new("3#水泵"),
                new("总阀门"), new("1#阀门"), new("2#阀门"), new("3#阀门"),
            };

            _polling.OnDataReceived += data => UiDispatcher.Post(() => ApplyData(data));
            _polling.OnConnectionChanged += connected => UiDispatcher.Post(() => OnConnectionChanged(connected));
            _polling.OnQualityChanged += ok => UiDispatcher.Post(() => OnQualityChanged(ok));
            _polling.OnError += m => UiDispatcher.Post(() => _log.Error(m));

            ConnectCommand = new RelayCommand(() => _ = ConnectAsync(), () => !_connecting && !_polling.IsConnected);
            DisconnectCommand = new RelayCommand(Disconnect, () => _polling.IsConnected || SimRunning);
            ToggleSimCommand = new RelayCommand(() => _ = ToggleSimulationAsync(), () => !_connecting);

            if (startWatchdog)
            {
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AppConstants.UiPollIntervalMs) };
                timer.Tick += (_, _) => WatchdogTick();
                timer.Start();
            }
        }

        // ═══════════════ 监视流：数据应用（对应 OnPollDataReceived，FrmMain.cs:568-625） ═══════════════

        /// <summary>轮询数据应用（public 供单测；事件处理器经 UiDispatcher 封送后调用）。</summary>
        public void ApplyData(Dictionary<string, DataPoint> data)
        {
            TagValues.Update(data.Select(kv => new KeyValuePair<string, double>(kv.Key, kv.Value.Value)));

            if (!_qualityOk)
            {
                _qualityOk = true;
                SetState(CommState.Connected, "已连接", "Ok");
                _log.Info("数据质量恢复正常");
            }

            if (!_initialSyncDone)
            {
                _initialSyncDone = true;
                _log.Info("首包同步：界面开关状态对齐 PLC");
                InitialSync?.Invoke(data);
            }
            UpdateBadges(data);
        }

        /// <summary>首包前的空态（对应 ResetDisplayValues：设计期样例值是假数据）。</summary>
        public void ResetDisplayValues() => TagValues.ResetToZero();

        // ═══════════════ 状态流 ═══════════════

        public void OnConnectionChanged(bool connected)
        {
            _log.Info(connected ? "PLC 已连接" : "PLC 已断开");
            if (connected)
            {
                if (!_qualityOk) return; // 质量事件负责恢复
                SetState(CommState.Connected, $"已连接 {_client.IpAddress}:{_client.Port}", "Ok");
            }
            else
            {
                SetState(CommState.Disconnected, SimRunning ? "模拟已停止" : "等待连接", "Off");
                ResetBadges();
            }
        }

        public void OnQualityChanged(bool ok)
        {
            if (ok)
            {
                _qualityOk = true;
                SetState(CommState.Connected, "已连接", "Ok");
                _log.Info("数据质量恢复正常");
            }
            else
            {
                _qualityOk = false;
                SetState(CommState.Stale, "通信中断", "Alarm");
                _log.Warn("数据中断（通信异常，界面数据已过期）");
                ResetBadges();
            }
        }

        private void SetState(CommState state, string text, string toneKey)
        {
            ConnState = state;
            ConnStateText = text;
            ConnToneKey = toneKey;
            CommandManager.InvalidateRequerySuggested();   // 按钮互斥启用即时刷新（T3.3）
        }

        // ═══════════════ 状态徽章带（修 P5/P6：灯文字随状态变） ═══════════════

        private void UpdateBadges(Dictionary<string, DataPoint> data)
        {
            for (int i = 1; i <= 3; i++)
            {
                bool fault = data.TryGetValue($"Pump{i}_Fault", out var f) && f.Value != 0;
                bool run = !fault && data.TryGetValue($"Pump{i}_Run", out var r) && r.Value != 0;
                var b = Badges[i - 1];
                b.Word = fault ? "● 故障" : run ? "● 运行" : "● 停止";
                b.ToneKey = fault ? "Alarm" : run ? "Ok" : "Off";
            }
            SetValveBadge(Badges[3], data, "MainValve");
            SetValveBadge(Badges[4], data, "Valve1");
            SetValveBadge(Badges[5], data, "Valve2");
            SetValveBadge(Badges[6], data, "Valve3");
        }

        private static void SetValveBadge(BadgeVm b, Dictionary<string, DataPoint> data, string tag)
        {
            bool on = data.TryGetValue(tag, out var v) && v.Value != 0;
            b.Word = on ? "● 开" : "○ 关";
            b.ToneKey = on ? "Ok" : "Off";
        }

        /// <summary>断线/数据过期：徽章全灭（ResetProcessGraphics 语义——不得显示过期状态）。</summary>
        private void ResetBadges()
        {
            foreach (var b in Badges)
            {
                b.Word = b.Label.Contains("泵") ? "○ 停止" : "○ 关";
                b.ToneKey = "Off";
            }
        }

        // ═══════════════ 看门狗（200ms，数据过期兜底——FrmMain.cs:940-947 平移） ═══════════════

        /// <summary>过期判定纯函数（可测）：已连接且首包已同步、距上次成功轮询 > 3×周期 → 过期。</summary>
        public static bool ShouldMarkStale(bool qualityOk, bool initialSyncDone, bool isConnected,
            DateTime lastGoodPollAt, DateTime now, int intervalMs)
            => qualityOk && initialSyncDone && isConnected
               && (now - lastGoodPollAt).TotalMilliseconds > 3 * intervalMs;

        public void WatchdogTick()
        {
            if (ShouldMarkStale(_qualityOk, _initialSyncDone, _polling.IsConnected,
                    _polling.LastGoodPollAt, _clock(), AppConstants.PollingIntervalMs))
            {
                _qualityOk = false;
                SetState(CommState.Stale, "通信中断", "Alarm");
                _log.Warn("数据中断（通信异常，界面数据已过期）");
                ResetBadges();
            }
        }

        // ═══════════════ 通信命令（对应 ConnectAsync/DisconnectPlc，FrmMain.cs:389-436） ═══════════════

        private async Task ConnectAsync()
        {
            if (_connecting) return;
            string ip = IpText.Trim();
            if (!IPAddress.TryParse(ip, out _))
            {
                SetState(ConnState, "IP 格式无效", "Alarm");
                _log.Error("IP 格式无效");
                return;
            }
            int port = int.TryParse(PortText.Trim(), out var p) ? p : 502;
            _connecting = true;
            SetState(CommState.Connecting, "连接中…", "Accent");
            _log.Info($"正在连接 PLC ({ip}:{port}) ...");
            try
            {
                _client.UpdateConnection(ip, port);
                bool ok = await _polling.ConnectAsync();
                if (ok)
                {
                    _polling.Start();
                    _initialSyncDone = false;
                    _qualityOk = true;
                    SetState(CommState.Connected, $"已连接 {ip}:{port}", "Ok");
                    _log.Info("PLC 连接成功，开始轮询数据");
                }
                else
                {
                    SetState(CommState.Disconnected, "连接失败", "Alarm");
                    _log.Error($"连接失败: {_client.LastError}");
                }
            }
            catch (Exception ex)
            {
                SetState(CommState.Disconnected, "连接错误", "Alarm");
                _log.Error($"连接异常: {ex.Message}");
            }
            finally
            {
                _connecting = false;
            }
        }

        private void Disconnect()
        {
            _polling.Disconnect();
            _initialSyncDone = false;
            ResetBadges();
            SetState(CommState.Disconnected, SimRunning ? "模拟已停止" : "等待连接", "Off");
            _log.Info("已主动断开 PLC 连接");
        }

        // ═══════════════ 模拟模式（对应 ToggleSimulationAsync，FrmMain.cs:467-531；同一套代码路径） ═══════════════

        private async Task ToggleSimulationAsync()
        {
            if (SimRunning) { StopSimulation(); return; }
            if (_polling.IsConnected) Disconnect();

            try
            {
                _simStore = new DataStore(_tags);
                var engine = new ScenarioEngine(_simStore);
                _simCts = new CancellationTokenSource();
                var cts = _simCts;
                var server = new ModbusServer(_simStore, new SimulatorOptions(), engine);
                _simTask = Task.Run(async () =>
                {
                    try { await server.RunAsync(SimPort, cts.Token); }
                    catch (Exception ex) { _log.Error($"模拟器服务异常: {ex.Message}"); }
                });

                IpText = "127.0.0.1";
                PortText = SimPort.ToString();
                await Task.Delay(300);
                await ConnectAsync();

                if (_polling.IsConnected)
                {
                    SimRunning = true;
                    _log.Info($"模拟模式已启动：进程内从站 127.0.0.1:{SimPort}，全链路走真实 Modbus TCP");
                }
                else
                {
                    _log.Error("模拟模式启动失败：未能连接到本机模拟器");
                    StopSimulation();
                }
            }
            catch (Exception ex)
            {
                _log.Error($"启动模拟器失败: {ex.Message}");
                StopSimulation();
            }
        }

        private void StopSimulation()
        {
            try { _simCts?.Cancel(); } catch { }
            _simCts = null;
            _simTask = null;
            _simStore = null;
            if (_polling.IsConnected) Disconnect();
            SimRunning = false;
            SetState(CommState.Disconnected, "等待连接", "Off");
            _log.Info("模拟模式已停止");
        }
    }
}
