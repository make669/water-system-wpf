using System.IO;
using System.Windows;
using System.Windows.Threading;
using Intelligent_water_storage_system.Model;
using Intelligent_water_storage_system.Services;
using Intelligent_water_storage_system.Utils;

namespace WaterSystem.Wpf
{
    /// <summary>
    /// 组合根：启动七步链（迁移方案 §3.1，对应 FrmMain_Load 平移）：
    /// 日志先行 → 契约 fail-fast（E01~E07/A01~A09）→ 报警中心(SQLite) → 通信配置与服务创建 → 主窗显示。
    /// E08/E09 绑定核对由 TagBindingRegistry 在主窗 InitializeComponent 后接入（T5.5）。
    /// </summary>
    public partial class App : Application
    {
        public static LogService Log { get; private set; } = null!;
        public static List<TagAddress> Tags { get; private set; } = new();
        public static ModbusClient Client { get; private set; } = null!;
        public static DataPollingService Polling { get; private set; } = null!;
        public static ControlService Control { get; private set; } = null!;
        public static AlarmService AlarmEval { get; private set; } = null!;
        public static AlarmCenter AlarmCenter { get; private set; } = null!;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += OnDispatcherException;

            // ① 日志先行（文件层立即可用）
            Log = new LogService();

            // ②③ 契约 fail-fast：全量报出、拒绝带病启动（对应 FrmMain.cs:157-167 语义）
            var pre = AppBootstrapper.PrecheckConfig(AppContext.BaseDirectory);
            foreach (var w in pre.Warnings) Log.Warn(w);
            if (!pre.Ok)
            {
                Log.Error($"启动校验失败，共 {pre.Errors.Count} 处错误，程序无法启动");
                new Views.FailFastWindow(pre.Errors).ShowDialog();
                Shutdown(1);
                return;
            }
            Tags = pre.Tags;
            Log.Info($"加载标签配置: {pre.Tags.Count} 个数据点");

            // ④ 报警中心（SQLite 持久化，存储失败降级为日志）
            AlarmCenter = new AlarmCenter(Path.Combine(AppContext.BaseDirectory, "data", "history.db"));
            AlarmCenter.OnError += m => Log.Error(m);

            // ⑤ 通信配置与服务创建（对应 FrmMain.LoadCommunicationConfig，FrmMain.cs:366-387）
            var comm = Utility.LoadCommunicationConfig(
                Path.Combine(AppContext.BaseDirectory, "configFiles", "Communication.txt"));
            Client = new ModbusClient(
                string.IsNullOrEmpty(comm.IP) ? "127.0.0.1" : comm.IP,
                comm.Port > 0 ? comm.Port : 502);
            Polling = new DataPollingService(Client, AppConstants.PollingIntervalMs);
            Polling.SetTags(Tags);
            Control = new ControlService(Client, Tags);
            Control.OnError += m => Log.Error(m);
            AlarmEval = new AlarmService(pre.Config!);
            var cfg = pre.Config!;
            Log.Info($"报警阈值: 水位 {cfg.WaterLevelLow:0.##}~{cfg.WaterLevelHigh:0.##} m，" +
                     $"流量 ≤{cfg.MaxFlow:0.##} m³，压力 ≤{cfg.MaxPressure:0.##} Mpa，泵 {cfg.PumpCount} 台");

            // ⑥⑦ 主窗显示（E08/E09 于 T5.5 在 InitializeComponent 后核对）
            var window = new MainWindow
            {
                DataContext = new ViewModels.MainViewModel(
                    Log, Client, Polling, Control, AlarmEval, Tags),
            };
            MainWindow = window;
            window.Show();
        }

        private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log?.Error($"未处理异常: {e.Exception.GetType().Name}: {e.Exception.Message}");
            MessageBox.Show($"发生未处理异常：{e.Exception.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        /// <summary>退出前生命周期（对应 FormClosing：停轮询/停模拟/存状态——M3/M8 接线，此处先停轮询）。</summary>
        protected override void OnExit(ExitEventArgs e)
        {
            Polling?.Dispose();
            base.OnExit(e);
        }
    }
}
