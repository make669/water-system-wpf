using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
            WindowStartupLocation = WindowStartupLocation.Manual;
            FitToWorkArea();
            SourceInitialized += (_, _) => FitToWorkArea();
            Loaded += (_, _) => FitToWorkArea();
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.F12) { OpenLogViewer(); e.Handled = true; }
            };
        }

        /// <summary>Q1=B（Viewbox 矢量等比缩放）：窗口尺寸 = 设计稿 × min(工作区/设计稿, 1.0)。
        /// 居中放置在工作区内，顶部贴合不超出屏幕边界，标题栏/关闭钮任何屏幕可达（修 P17 实锤）。</summary>
        public void FitToWorkArea()
        {
            var work = SystemParameters.WorkArea;
            var (w, h) = SizeFor(work.Width, work.Height);
            Width = w;
            Height = h;
            Left = Math.Max(work.Left, work.Left + (work.Width - w) / 2);
            Top = Math.Max(work.Top, work.Top + (work.Height - h) / 2);
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
            {
                _logViewer = new Views.LogViewerWindow(App.Log, App.AlarmCenter) { Owner = this };
                _logViewer.Closed += (_, _) => _logViewer = null;
            }
            _logViewer.Show();
            _logViewer.Activate();
            App.Log?.Info("日志窗口已打开");
        }

        /// <summary>顶栏拖动（对应源 WmNclButtonDown/DragMove 语义，D6）。</summary>
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed && e.LeftButton == MouseButtonState.Pressed)
            {
                if (e.OriginalSource is DependencyObject d && IsInteractiveElement(d))
                    return;
                try { DragMove(); } catch { }
            }
        }

        /// <summary>允许在整个主窗空白背景区拖动无边框窗口。</summary>
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (e.ButtonState == MouseButtonState.Pressed && e.LeftButton == MouseButtonState.Pressed)
            {
                if (e.OriginalSource is DependencyObject d && IsInteractiveElement(d))
                    return;
                try { DragMove(); } catch { }
            }
        }

        private static bool IsInteractiveElement(DependencyObject? obj)
        {
            while (obj != null && obj is not Window)
            {
                if (obj is Button || obj is TextBox || obj is ListBox || obj is DataGrid ||
                    obj is ScrollViewer || obj is System.Windows.Controls.Primitives.ToggleButton ||
                    obj is Controls.AlarmBadgeChip || obj is Controls.USwitchW || obj is Controls.URotarySwitchW)
                    return true;

                if (obj is Visual || obj is System.Windows.Media.Media3D.Visual3D)
                    obj = VisualTreeHelper.GetParent(obj);
                else
                    obj = LogicalTreeHelper.GetParent(obj);
            }
            return false;
        }

        /// <summary>顶栏报警徽标点击：聚焦实时报警卡（修 P4/P12）。</summary>
        private void UnackBadge_Click(object sender, MouseButtonEventArgs e)
        {
            AlarmCard?.Focus();
        }

        /// <summary>报警列表双击行：确认选中报警（修 P12，ISA-18.2 确认链路）。</summary>
        private void AlarmListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is ViewModels.MainViewModel vm && vm.AlarmPanel?.AckSelectedCommand.CanExecute(null) == true)
            {
                vm.AlarmPanel.AckSelectedCommand.Execute(null);
            }
        }

        private void OpenLogViewer_Click(object sender, RoutedEventArgs e) => OpenLogViewer();

        /// <summary>顶栏 ⓘ 系统说明窗（设计书 §1.0 & §2.1，T4.10 验收）。</summary>
        private void SystemInfoButton_Click(object sender, RoutedEventArgs e)
        {
            var infoWin = new Views.SystemInfoWindow(DataContext as ViewModels.MainViewModel) { Owner = this };
            infoWin.ShowDialog();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        /// <summary>日志行着色（唯一出处：LogRowStyler，主卡与 F12 共用——T6.1）。</summary>
        private void LogGrid_LoadingRow(object sender, DataGridRowEventArgs e)
            => Services.LogRowStyler.Apply(e.Row, (LogEntry)e.Row.Item);

        /// <summary>是否在窗口关闭时弹窗确认（修 P14，默认 true；单测可设为 false 避免阻塞）。</summary>
        public bool ConfirmOnClose { get; set; } = true;

        /// <summary>
        /// 关闭生命周期（对应 FormClosing，FrmMain.cs:185-201）：弹退出确认（修 P14）→ 停轮询/停模拟/存状态。
        /// 轮询释放与状态持久化在 App.OnExit（M8 接线），此处记录会话结束。
        /// </summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            if (ConfirmOnClose && !ViewModels.ConfirmDialog.ConfirmExit(this))
            {
                e.Cancel = true;
                return;
            }

            App.Log?.Info("会话结束：停止数据采集");
            App.SaveSession();   // M8：开关语义 + 通信参数（显示值不存）
            base.OnClosing(e);
        }
    }
}
