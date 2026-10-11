using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WaterSystem.Wpf.ViewModels
{
    /// <summary>控制操作确认请求（P16 增强：主问句 + 上下文行；文案出处=ControlDecision.DescribeAction）。</summary>
    public sealed record ConfirmRequest(
        string Message, string TagName, string Description,
        string CurrentText, string TargetText, string PlcText);

    /// <summary>
    /// 控制确认弹窗（修 P16）：默认焦点在"取消"——回车不执行；Esc 取消。
    /// 语义对应源 HandleToggle 的 MessageBox.YesNo（FrmMain.cs:854-856），上下文行为【新增】。
    /// </summary>
    public sealed class ConfirmDialog : Window
    {
        public bool Confirmed { get; private set; }

        public ConfirmDialog(ConfirmRequest r)
        {
            Title = "操作确认";
            Width = 560;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#131C2E"));

            Color text = (Color)ColorConverter.ConvertFromString("#E5EDF5");
            Color dim = (Color)ColorConverter.ConvertFromString("#8FA3BF");
            Color alarm = (Color)ColorConverter.ConvertFromString("#F87171");

            var message = new TextBlock
            {
                Text = r.Message, FontSize = 15, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(text), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16, 16, 16, 8),
            };
            var detail = new TextBlock
            {
                FontSize = 12, Foreground = new SolidColorBrush(dim), Margin = new Thickness(16, 0, 16, 8),
                Text = $"点位：{r.TagName}（{r.Description}）\n当前：{r.CurrentText}  →  目标：{r.TargetText}\nPLC：{r.PlcText}",
            };
            var cancel = new Button { Content = "取消", Width = 96, Height = 30, IsDefault = true, IsCancel = true, Margin = new Thickness(0, 8, 8, 12) };
            var ok = new Button
            {
                Content = "确认执行", Width = 96, Height = 30, Margin = new Thickness(0, 8, 16, 12),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6")),
                Foreground = new SolidColorBrush(text), BorderThickness = new Thickness(0),
            };
            ok.Click += (_, _) => { Confirmed = true; Close(); };

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(message, 0); Grid.SetRow(detail, 1); Grid.SetRow(buttons, 2);
            grid.Children.Add(message); grid.Children.Add(detail); grid.Children.Add(buttons);
            Content = grid;

            Loaded += (_, _) => cancel.Focus();
        }

        public static bool Show(ConfirmRequest request)
        {
            var w = new ConfirmDialog(request);
            w.ShowDialog();
            return w.Confirmed;
        }
    }
}
