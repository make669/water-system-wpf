using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace WaterSystem.Wpf.Controls
{
    /// <summary>
    /// 工业水泵旋钮控件（附录 B URotarySwitchW，§4.2-4 & §3.3）：
    /// - 尺寸：56×56 旋钮盘；
    /// - 角度状态：0° 为停止（Off），90° 为启动（On）；
    /// - 动画：状态翻转时 150ms 平滑旋转过渡；
    /// - 交互：点击 → Command.Execute(CommandParameter)（经过 VM 三层回写保护）；
    /// - 离线：半透明 50%，但允许手势触发以弹出“PLC 未连接”预检提示（源行为对齐）。
    /// </summary>
    public class URotarySwitchW : ContentControl
    {
        public static readonly DependencyProperty SwitchStateProperty = DependencyProperty.Register(
            nameof(SwitchState), typeof(bool), typeof(URotarySwitchW),
            new PropertyMetadata(false, OnSwitchStateChanged));

        public static readonly DependencyProperty TagNameProperty = DependencyProperty.Register(
            nameof(TagName), typeof(string), typeof(URotarySwitchW),
            new PropertyMetadata(""));

        public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
            nameof(Command), typeof(ICommand), typeof(URotarySwitchW),
            new PropertyMetadata(null));

        public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.Register(
            nameof(CommandParameter), typeof(object), typeof(URotarySwitchW),
            new PropertyMetadata(null));

        public static readonly DependencyProperty IsOnlineProperty = DependencyProperty.Register(
            nameof(IsOnline), typeof(bool), typeof(URotarySwitchW),
            new PropertyMetadata(true, OnOnlineChanged));

        public bool SwitchState { get => (bool)GetValue(SwitchStateProperty); set => SetValue(SwitchStateProperty, value); }
        public string TagName { get => (string)GetValue(TagNameProperty); set => SetValue(TagNameProperty, value); }
        public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
        public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }
        public bool IsOnline { get => (bool)GetValue(IsOnlineProperty); set => SetValue(IsOnlineProperty, value); }

        private readonly Canvas _canvas = new();
        private readonly Ellipse _dialBase = new();
        private readonly Canvas _knobCanvas = new();
        private readonly Ellipse _knobCore = new();
        private readonly Border _pointerHandle = new();
        private readonly RotateTransform _knobRotate = new();

        public URotarySwitchW()
        {
            Width = 56;
            Height = 56;
            Cursor = Cursors.Hand;
            InitializeVisuals();
            UpdateVisuals(animate: false);

            MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                Command?.Execute(CommandParameter ?? TagName);
            };
        }

        private void InitializeVisuals()
        {
            _canvas.Width = 56;
            _canvas.Height = 56;

            // 1. 底盘刻度外圈
            _dialBase.Width = 56;
            _dialBase.Height = 56;
            _dialBase.Stroke = ResourceBrush("Stroke", "#223047");
            _dialBase.StrokeThickness = 2;
            _dialBase.Fill = new SolidColorBrush(Color.FromRgb(0x10, 0x19, 0x2A));
            _canvas.Children.Add(_dialBase);

            // 刻度标字（左上为 0 停，右上为 1 启）
            var txtOff = new TextBlock
            {
                Text = "停",
                FontSize = 9,
                Foreground = ResourceBrush("TextSecondary", "#8FA3BF")
            };
            Canvas.SetLeft(txtOff, 7);
            Canvas.SetTop(txtOff, 12);
            _canvas.Children.Add(txtOff);

            var txtOn = new TextBlock
            {
                Text = "启",
                FontSize = 9,
                Foreground = ResourceBrush("TextSecondary", "#8FA3BF")
            };
            Canvas.SetRight(txtOn, 7);
            Canvas.SetTop(txtOn, 12);
            _canvas.Children.Add(txtOn);

            // 2. 旋钮主体
            _knobCanvas.Width = 56;
            _knobCanvas.Height = 56;
            _knobRotate.CenterX = 28;
            _knobRotate.CenterY = 28;
            _knobCanvas.RenderTransform = _knobRotate;

            _knobCore.Width = 34;
            _knobCore.Height = 34;
            _knobCore.Stroke = ResourceBrush("Stroke", "#223047");
            _knobCore.StrokeThickness = 1.5;
            _knobCore.Fill = new SolidColorBrush(Color.FromRgb(0x1A, 0x27, 0x40));
            Canvas.SetLeft(_knobCore, 11);
            Canvas.SetTop(_knobCore, 11);
            _knobCanvas.Children.Add(_knobCore);

            // 旋钮凸棱指示条
            _pointerHandle.Width = 6;
            _pointerHandle.Height = 26;
            _pointerHandle.CornerRadius = new CornerRadius(3);
            _pointerHandle.Background = ResourceBrush("Off", "#64748B");
            Canvas.SetLeft(_pointerHandle, 25);
            Canvas.SetTop(_pointerHandle, 8);
            _knobCanvas.Children.Add(_pointerHandle);

            _canvas.Children.Add(_knobCanvas);
            Content = _canvas;
        }

        private static void OnSwitchStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((URotarySwitchW)d).UpdateVisuals(animate: true);
        }

        private static void OnOnlineChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((URotarySwitchW)d).UpdateVisuals(animate: false);
        }

        private void UpdateVisuals(bool animate)
        {
            Opacity = IsOnline ? 1.0 : 0.5;

            // 0° 为垂直偏左（-45° 为停，+45° 为启，跨度 90° 旋钮）
            double targetAngle = SwitchState ? 45.0 : -45.0;
            _pointerHandle.Background = SwitchState ? ResourceBrush("Ok", "#34D399") : ResourceBrush("Off", "#64748B");

            if (animate)
            {
                var anim = new DoubleAnimation
                {
                    To = targetAngle,
                    Duration = TimeSpan.FromMilliseconds(150),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                _knobRotate.BeginAnimation(RotateTransform.AngleProperty, anim);
            }
            else
            {
                _knobRotate.BeginAnimation(RotateTransform.AngleProperty, null);
                _knobRotate.Angle = targetAngle;
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
