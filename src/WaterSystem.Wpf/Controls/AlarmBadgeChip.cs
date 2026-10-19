using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WaterSystem.Wpf.Controls
{
    public enum AlarmBadgeTone
    {
        Off,
        Ok,
        Alarm,
        Ack,
        Recover,
    }

    /// <summary>
    /// 状态徽章胶囊（附录 B AlarmBadgeChip，§4.2-12 & §3.4）：
    /// - 视觉：实心圆角 4px 芯片，字体 11px；
    /// - 色调：
    ///     Alarm:   Alarm 红底 + 白字（「▲告警中」/「▲故障」模式）
    ///     Ack:     Ack 黄底 + 深字（「已确认」模式）
    ///     Recover: Recover 橙底 + 深字（「◐已恢复·未确认」模式）
    ///     Ok:      深绿底 + Ok 亮绿字（「●运行」模式）
    ///     Off:     深蓝灰底 + 灰字（「●停止」/「●正常」/「已关闭」模式）
    /// - 支持通过 IsActive 或 Value (double > 0.5) 在 ActiveText/InactiveText 间自动切换。
    /// </summary>
    public class AlarmBadgeChip : ContentControl
    {
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
            nameof(Text), typeof(string), typeof(AlarmBadgeChip),
            new PropertyMetadata("", OnVisualChanged));

        public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(
            nameof(Tone), typeof(AlarmBadgeTone), typeof(AlarmBadgeChip),
            new PropertyMetadata(AlarmBadgeTone.Off, OnVisualChanged));

        public static readonly DependencyProperty ActiveTextProperty = DependencyProperty.Register(
            nameof(ActiveText), typeof(string), typeof(AlarmBadgeChip),
            new PropertyMetadata(null, OnDualStateChanged));

        public static readonly DependencyProperty InactiveTextProperty = DependencyProperty.Register(
            nameof(InactiveText), typeof(string), typeof(AlarmBadgeChip),
            new PropertyMetadata(null, OnDualStateChanged));

        public static readonly DependencyProperty ActiveToneProperty = DependencyProperty.Register(
            nameof(ActiveTone), typeof(AlarmBadgeTone), typeof(AlarmBadgeChip),
            new PropertyMetadata(AlarmBadgeTone.Ok, OnDualStateChanged));

        public static readonly DependencyProperty InactiveToneProperty = DependencyProperty.Register(
            nameof(InactiveTone), typeof(AlarmBadgeTone), typeof(AlarmBadgeChip),
            new PropertyMetadata(AlarmBadgeTone.Off, OnDualStateChanged));

        public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
            nameof(IsActive), typeof(bool), typeof(AlarmBadgeChip),
            new PropertyMetadata(false, OnDualStateChanged));

        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value), typeof(double), typeof(AlarmBadgeChip),
            new PropertyMetadata(0.0, (d, e) =>
            {
                var chip = (AlarmBadgeChip)d;
                chip.IsActive = (double)e.NewValue > 0.5;
            }));

        public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
        public AlarmBadgeTone Tone { get => (AlarmBadgeTone)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }
        public string? ActiveText { get => (string?)GetValue(ActiveTextProperty); set => SetValue(ActiveTextProperty, value); }
        public string? InactiveText { get => (string?)GetValue(InactiveTextProperty); set => SetValue(InactiveTextProperty, value); }
        public AlarmBadgeTone ActiveTone { get => (AlarmBadgeTone)GetValue(ActiveToneProperty); set => SetValue(ActiveToneProperty, value); }
        public AlarmBadgeTone InactiveTone { get => (AlarmBadgeTone)GetValue(InactiveToneProperty); set => SetValue(InactiveToneProperty, value); }
        public bool IsActive { get => (bool)GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }
        public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

        private readonly Border _border = new();
        private readonly TextBlock _textBlock = new();

        public AlarmBadgeChip()
        {
            _border.CornerRadius = new CornerRadius(4);
            _border.Padding = new Thickness(6, 2, 6, 2);
            _border.HorizontalAlignment = HorizontalAlignment.Center;
            _border.VerticalAlignment = VerticalAlignment.Center;

            _textBlock.FontSize = 11;
            _textBlock.FontWeight = FontWeights.SemiBold;
            _textBlock.HorizontalAlignment = HorizontalAlignment.Center;
            _textBlock.VerticalAlignment = VerticalAlignment.Center;

            _border.Child = _textBlock;
            Content = _border;
            Refresh();
        }

        private static void OnDualStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var chip = (AlarmBadgeChip)d;
            if (chip.ActiveText != null && chip.InactiveText != null)
            {
                chip.Text = chip.IsActive ? chip.ActiveText : chip.InactiveText;
                chip.Tone = chip.IsActive ? chip.ActiveTone : chip.InactiveTone;
            }
            chip.Refresh();
        }

        private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((AlarmBadgeChip)d).Refresh();
        }

        private void Refresh()
        {
            _textBlock.Text = Text;

            switch (Tone)
            {
                case AlarmBadgeTone.Alarm:
                    _border.Background = ResourceBrush("Alarm", "#F87171");
                    _textBlock.Foreground = Brushes.White;
                    break;

                case AlarmBadgeTone.Ack:
                    _border.Background = ResourceBrush("Ack", "#FBBF24");
                    _textBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x0B, 0x12, 0x20));
                    break;

                case AlarmBadgeTone.Recover:
                    _border.Background = ResourceBrush("Recover", "#FB923C");
                    _textBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x0B, 0x12, 0x20));
                    break;

                case AlarmBadgeTone.Ok:
                    _border.Background = new SolidColorBrush(Color.FromArgb(0x44, 0x34, 0xD3, 0x99));
                    _textBlock.Foreground = ResourceBrush("Ok", "#34D399");
                    break;

                case AlarmBadgeTone.Off:
                default:
                    _border.Background = ResourceBrush("BgHover", "#1A2740");
                    _textBlock.Foreground = ResourceBrush("TextSecondary", "#8FA3BF");
                    break;
            }
        }

        private static Brush ResourceBrush(string key, string fallbackHex)
        {
            if (Application.Current != null && Application.Current.TryFindResource(key) is Brush brush)
                return brush;
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallbackHex));
        }
    }
}
