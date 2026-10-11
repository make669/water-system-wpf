using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Intelligent_water_storage_system.Services;

namespace WaterSystem.Wpf.Views
{
    /// <summary>
    /// 日志详情弹窗（源 LogViewerForm.ShowDetail 平移，FrmMain.cs:1165-1228）：
    /// WARN 行匹配未关闭报警时提供【确认报警】→ AlarmCenter.Acknowledge（确认人=Windows 用户名，空则"操作员"）。
    /// </summary>
    public sealed class LogDetailDialog : Window
    {
        public LogDetailDialog(LogEntry entry, AlarmCenter? center)
        {
            Title = "日志详情";
            Width = 560;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = (Brush)FindResource("BgCard");

            Color text = (Color)ColorConverter.ConvertFromString("#E5EDF5");
            Color dim = (Color)ColorConverter.ConvertFromString("#8FA3BF");

            var info = new TextBlock
            {
                FontSize = 12, Foreground = new SolidColorBrush(dim), Margin = new Thickness(16, 14, 16, 4),
                Text = $"时间：{entry.Time:yyyy-MM-dd HH:mm:ss}\n级别：{entry.Level}",
            };
            var message = new TextBox
            {
                Text = entry.Message, FontSize = 13, Foreground = new SolidColorBrush(text),
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(16, 8, 16, 8), MaxHeight = 120,
            };
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 8, 16, 12),
            };

            var close = new Button { Content = "关闭", Width = 88, Height = 30, IsCancel = true };
            buttons.Children.Add(close);

            // 匹配未关闭报警 → 确认报警按钮（源语义：按消息匹配 OpenSnapshot）
            AlarmRecord? record = null;
            if (entry.Level == "WARN" && center != null)
                record = center.OpenSnapshot().FirstOrDefault(a => a.Message == entry.Message);
            if (record != null)
            {
                var by = string.IsNullOrWhiteSpace(Environment.UserName) ? "操作员" : Environment.UserName;
                var ack = new Button
                {
                    Content = record.NeedsAck ? "确认报警" : "已确认",
                    IsEnabled = record.NeedsAck,
                    Width = 96, Height = 30, Margin = new Thickness(0, 0, 8, 0),
                };
                ack.Click += (_, _) =>
                {
                    try
                    {
                        center!.Acknowledge(record.Id, by);
                        MessageBox.Show(this, $"报警已确认 [{record.Name}]（{by}）", "确认完成",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                        Close();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, $"确认失败：{ex.Message}", "错误",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };
                buttons.Children.Add(ack);
            }
            buttons.Children.Add(close);

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(info, 0); Grid.SetRow(message, 1); Grid.SetRow(buttons, 2);
            grid.Children.Add(info); grid.Children.Add(message); grid.Children.Add(buttons);
            Content = grid;
        }
    }
}
