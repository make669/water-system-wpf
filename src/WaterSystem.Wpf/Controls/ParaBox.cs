using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Intelligent_water_storage_system.Utils;

namespace WaterSystem.Wpf.Controls
{
    /// <summary>
    /// 参数数值显示件（Zhaoxi ParaTextBox 的 WPF 等价物，迁移方案附录 B）。
    /// Value 经 TagBindingExtension 绑定到 VM TagValues 索引器（OneWay——回写防护层 1），
    /// 显示文本由 ValueFormatter 按 Decimals 格式化（修 P1）；越限（Min/Max DP）转 Alarm 色（修 P7 的显示面）。
    /// </summary>
    public class ParaBox : System.Windows.Controls.ContentControl
    {
        public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
            nameof(Title), typeof(string), typeof(ParaBox), new PropertyMetadata("", OnTextPartsChanged));

        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value), typeof(double), typeof(ParaBox), new PropertyMetadata(0.0, OnTextPartsChanged));

        public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
            nameof(Unit), typeof(string), typeof(ParaBox), new PropertyMetadata("", OnTextPartsChanged));

        public static readonly DependencyProperty DecimalsProperty = DependencyProperty.Register(
            nameof(Decimals), typeof(int), typeof(ParaBox), new PropertyMetadata(0, OnTextPartsChanged));

        public static readonly DependencyProperty AlarmProperty = DependencyProperty.Register(
            nameof(Alarm), typeof(bool), typeof(ParaBox), new PropertyMetadata(false, OnTextPartsChanged));

        public static readonly DependencyProperty AlarmValueProperty = DependencyProperty.Register(
            nameof(AlarmValue), typeof(double), typeof(ParaBox),
            new PropertyMetadata(0.0, (d, e) =>
            {
                var p = (ParaBox)d;
                p.Alarm = (double)e.NewValue > 0.5;
            }));

        public static readonly DependencyProperty HighThresholdProperty = DependencyProperty.Register(
            nameof(HighThreshold), typeof(double?), typeof(ParaBox), new PropertyMetadata(null, OnTextPartsChanged));

        public static readonly DependencyProperty LowThresholdProperty = DependencyProperty.Register(
            nameof(LowThreshold), typeof(double?), typeof(ParaBox), new PropertyMetadata(null, OnTextPartsChanged));

        private readonly TextBlock _title = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _value = new() { FontSize = 20, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _unit = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 2, 0, 0) };

        public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
        public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
        public int Decimals { get => (int)GetValue(DecimalsProperty); set => SetValue(DecimalsProperty, value); }
        public bool Alarm { get => (bool)GetValue(AlarmProperty); set => SetValue(AlarmProperty, value); }
        public double AlarmValue { get => (double)GetValue(AlarmValueProperty); set => SetValue(AlarmValueProperty, value); }
        public double? HighThreshold { get => (double?)GetValue(HighThresholdProperty); set => SetValue(HighThresholdProperty, value); }
        public double? LowThreshold { get => (double?)GetValue(LowThresholdProperty); set => SetValue(LowThresholdProperty, value); }

        /// <summary>格式化后的显示文本（单测断言面）。</summary>
        public string DisplayText => _value.Text;

        public ParaBox()
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _title.Width = 64;
            panel.Children.Add(_title);
            panel.Children.Add(_value);
            panel.Children.Add(_unit);
            Content = panel;
            Refresh();
        }

        private static void OnTextPartsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((ParaBox)d).Refresh();

        private void Refresh()
        {
            _title.Text = Title;
            _title.Foreground = ResourceBrush("TextSecondary");
            // ValueFormatter：小数位 = 10^-Decimals 对应的 Scale 有效位（T3.2 纯函数复用）
            _value.Text = ValueFormatter.Format(Value, Math.Pow(10, -Decimals));

            bool isAlarm = Alarm;
            if (HighThreshold.HasValue && Value > HighThreshold.Value) isAlarm = true;
            if (LowThreshold.HasValue && Value < LowThreshold.Value) isAlarm = true;

            _value.Foreground = ResourceBrush(isAlarm ? "Alarm" : "TextPrimary");
            _unit.Text = Unit;
            _unit.Foreground = ResourceBrush("TextSecondary");
        }

        private static Brush ResourceBrush(string key)
            => Application.Current?.TryFindResource(key) as Brush
               ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8FA3BF"));
    }
}
