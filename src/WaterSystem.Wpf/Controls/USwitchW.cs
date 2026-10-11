using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace WaterSystem.Wpf.Controls
{
    /// <summary>
    /// 开关件（Zhaoxi USwitch 的 WPF 等价物，44×24 胶囊）。
    /// 视觉 = SwitchState DP（OneWay 绑 SwitchStates[tag]，回写防护层 1）；
    /// 手势 = 点击 → Command.Execute(TagName)（控制决策在 VM）。
    /// StateKey 决定配色（Ok/Off/Alarm 资源键）。
    /// </summary>
    public class USwitchW : System.Windows.Controls.ContentControl
    {
        public static readonly DependencyProperty TagNameProperty = DependencyProperty.Register(
            nameof(TagName), typeof(string), typeof(USwitchW), new PropertyMetadata(""));

        public static readonly DependencyProperty SwitchStateProperty = DependencyProperty.Register(
            nameof(SwitchState), typeof(bool), typeof(USwitchW), new PropertyMetadata(false, OnVisualChanged));

        public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
            nameof(Command), typeof(ICommand), typeof(USwitchW), new PropertyMetadata(null));

        public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.Register(
            nameof(CommandParameter), typeof(object), typeof(USwitchW), new PropertyMetadata(null));

        public static readonly DependencyProperty StateKeyProperty = DependencyProperty.Register(
            nameof(StateKey), typeof(string), typeof(USwitchW), new PropertyMetadata("Off", OnVisualChanged));

        private readonly Border _pill = new() { CornerRadius = new CornerRadius(11), Height = 22, Width = 44 };
        private readonly Ellipse _dot = new() { Width = 16, Height = 16, Fill = Brushes.Gainsboro, Margin = new Thickness(3) };

        public string TagName { get => (string)GetValue(TagNameProperty); set => SetValue(TagNameProperty, value); }
        public bool SwitchState { get => (bool)GetValue(SwitchStateProperty); set => SetValue(SwitchStateProperty, value); }
        public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
        public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }
        public string StateKey { get => (string)GetValue(StateKeyProperty); set => SetValue(StateKeyProperty, value); }

        public USwitchW()
        {
            Width = 44;
            Height = 24;
            var grid = new Grid();
            grid.Children.Add(_pill);
            grid.Children.Add(_dot);
            Content = grid;
            Cursor = Cursors.Hand;
            MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                Command?.Execute(CommandParameter);
            };
            Refresh();
        }

        private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((USwitchW)d).Refresh();

        private void Refresh()
        {
            bool on = SwitchState;
            _pill.Background = ResourceBrush(on ? "Ok" : "Off");
            _dot.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            _pill.Opacity = StateKey == "Off" && !on ? 0.9 : 1.0;
        }

        private static Brush ResourceBrush(string key)
            => Application.Current?.TryFindResource(key) as Brush
               ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));
    }
}
