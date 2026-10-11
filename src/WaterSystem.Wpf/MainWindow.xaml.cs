using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Intelligent_water_storage_system.Services;

namespace WaterSystem.Wpf
{
    /// <summary>
    /// 主窗壳（对应 FrmMain 窗口形态：1492×1000 固定、无边框、顶栏拖动——界面定稿 §2.1，Q1=A 裁决）。
    /// 分区内容 M3 监视/M4 填充；本类管窗口生命周期与日志着色接线。
    /// </summary>
    public partial class MainWindow : Window
    {
        private Views.LogViewerWindow? _logViewer;
        private const double DesignWidth = 1492, DesignHeight = 1000;

        public MainWindow()
        {
            InitializeComponent();
            SourceInitialized += (_, _) => FitToWorkArea();
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.F12) { OpenLogViewer(); e.Handled = true; }
            };
        }

        /// <summary>Q1=B（Viewbox 矢量等比缩放）：窗口尺寸 = 设计稿 × min(工作区/设计稿, 1.0)。
        /// 不放大只缩小；内容在 Viewbox 内无损等比，标题栏/关闭钮任何屏幕可达（修 P17 实锤）。</summary>
        private void FitToWorkArea()
        {
            var work = SystemParameters.WorkArea;
            (Width, Height) = SizeFor(work.Width, work.Height);
        }

        /// <summary>尺寸计算纯函数（可测）：k = min(工作区宽/1492, 工作区高/1000, 1.0)，不放大只缩小。</summary>
        public static (double W, double H) SizeFor(double workWidth, double workHeight)
        {
            double k = Math.Min(Math.Min(workWidth / DesignWidth, workHeight / DesignHeight), 1.0);
            return (Math.Round(DesignWidth * k), Math.Round(DesignHeight * k));
        }

        /// <summary>F12 全量日志窗口（单例复用，对应源 OpenLogViewer FrmMain.cs:123-138）。</summary>
        private void OpenLogViewer()
        {
            if (_logViewer == null)
                _logViewer = new Views.LogViewerWindow(App.Log, App.AlarmCenter) { Owner = this };
            _logViewer.Show();
            _logViewer.Activate();
            App.Log?.Info("日志窗口已打开");
        }

        /// <summary>顶栏拖动（对应源 WmNclButtonDown/DragMove 语义，D6）。</summary>
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        /// <summary>日志行着色（唯一出处：LogRowStyler，主卡与 F12 共用——T6.1）。</summary>
        private void LogGrid_LoadingRow(object sender, DataGridRowEventArgs e)
            => Services.LogRowStyler.Apply(e.Row, (LogEntry)e.Row.Item);

        /// <summary>
        /// 关闭生命周期（对应 FormClosing，FrmMain.cs:185-201）：停轮询/停模拟/存状态。
        /// 轮询释放与状态持久化在 App.OnExit（M8 接线），此处记录会话结束。
        /// </summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            App.Log?.Info("会话结束：停止数据采集");
            App.SaveSession();   // M8：开关语义 + 通信参数（显示值不存）
            base.OnClosing(e);
        }
    }
}
