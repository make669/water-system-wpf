using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WaterSystem.Wpf.Views
{
    /// <summary>
    /// 启动校验失败窗（fail-fast）：错误全量列出、操作员确认后退出。
    /// 对应源 FrmMain 弹窗语义"程序无法启动"（FrmMain.cs:314-317/345-353）。
    /// </summary>
    public sealed class FailFastWindow : Window
    {
        public FailFastWindow(IReadOnlyList<string> errors)
        {
            Title = "启动校验失败";
            Width = 760;
            Height = 480;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#131C2E"));

            var head = new TextBlock
            {
                Text = $"启动校验失败，共 {errors.Count} 处错误，程序无法启动：",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F87171")),
                Margin = new Thickness(16, 14, 16, 8),
            };
            var list = new ListBox
            {
                ItemsSource = errors,
                FontSize = 12,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E5EDF5")),
                Margin = new Thickness(16, 0, 16, 8),
            };
            var hint = new TextBlock
            {
                Text = "请修正 configFiles 下的配置文件后重新启动程序。",
                FontSize = 12,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8FA3BF")),
                Margin = new Thickness(16, 4, 16, 8),
            };
            var exit = new Button { Content = "退出", Width = 96, Height = 30, Margin = new Thickness(0, 4, 16, 12), HorizontalAlignment = HorizontalAlignment.Right };
            exit.Click += (_, _) => Close();

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(head, 0); Grid.SetRow(list, 1); Grid.SetRow(hint, 2); Grid.SetRow(exit, 3);
            grid.Children.Add(head); grid.Children.Add(list); grid.Children.Add(hint); grid.Children.Add(exit);
            Content = grid;
        }
    }
}
