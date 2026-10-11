using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace WaterSystem.Wpf
{
    /// <summary>
    /// 主窗壳（对应 FrmMain 窗口形态：1492×1000 固定、无边框、顶栏拖动——界面定稿 §2.1，Q1=A 裁决）。
    /// 分区内容 M4 填充；本类只管窗口生命周期。
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        /// <summary>顶栏拖动（对应源 WmNclButtonDown/DragMove 语义，D6）。</summary>
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        /// <summary>
        /// 关闭生命周期（对应 FormClosing，FrmMain.cs:185-201）：停轮询/停模拟/存状态。
        /// 轮询释放与状态持久化在 App.OnExit（M3/M8 接线），此处记录会话结束。
        /// </summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            App.Log?.Info("会话结束：停止数据采集");
            base.OnClosing(e);
        }
    }
}
