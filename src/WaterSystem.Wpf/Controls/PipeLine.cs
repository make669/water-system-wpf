using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace WaterSystem.Wpf.Controls
{
    public enum PipeFlowDirection
    {
        LeftToRight,
        RightToLeft,
        TopToBottom,
        BottomToTop,
    }

    /// <summary>
    /// 工艺管道控件（自绘，附录 B PipeLine，§4.2-8）：
    /// 底管 Stroke #3D4E6B 6px + 流动层 #3FB950 虚线 DashArray "6 8"；
    /// Active 时 DashOffset 0→-14 / 0.6s 循环；
    /// 【关键纪律】仅 Active 翻转时启停动画（重复赋值不重启——B8 相位教训）；
    /// 离线时 Active 置 false 停流并全灭。
    /// </summary>
    public class PipeLine : ContentControl
    {
        public static readonly DependencyProperty ActiveProperty = DependencyProperty.Register(
            nameof(Active), typeof(bool), typeof(PipeLine),
            new PropertyMetadata(false, OnActiveChanged));

        public static readonly DependencyProperty DirectionProperty = DependencyProperty.Register(
            nameof(Direction), typeof(PipeFlowDirection), typeof(PipeLine),
            new PropertyMetadata(PipeFlowDirection.LeftToRight, OnVisualLayoutChanged));

        public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
            nameof(Orientation), typeof(Orientation), typeof(PipeLine),
            new PropertyMetadata(Orientation.Horizontal, OnVisualLayoutChanged));

        public static readonly DependencyProperty FlowSpeedProperty = DependencyProperty.Register(
            nameof(FlowSpeed), typeof(double), typeof(PipeLine),
            new PropertyMetadata(0.6, OnVisualLayoutChanged));

        public static readonly DependencyProperty PipeThicknessProperty = DependencyProperty.Register(
            nameof(PipeThickness), typeof(double), typeof(PipeLine),
            new PropertyMetadata(6.0, OnVisualLayoutChanged));

        private readonly Canvas _canvas = new();
        private readonly Line _baseLine = new();
        private readonly Line _flowLine = new();
        private DoubleAnimation? _anim;

        public bool Active { get => (bool)GetValue(ActiveProperty); set => SetValue(ActiveProperty, value); }
        public PipeFlowDirection Direction { get => (PipeFlowDirection)GetValue(DirectionProperty); set => SetValue(DirectionProperty, value); }
        public Orientation Orientation { get => (Orientation)GetValue(OrientationProperty); set => SetValue(OrientationProperty, value); }
        public double FlowSpeed { get => (double)GetValue(FlowSpeedProperty); set => SetValue(FlowSpeedProperty, value); }
        public double PipeThickness { get => (double)GetValue(PipeThicknessProperty); set => SetValue(PipeThicknessProperty, value); }

        public PipeLine()
        {
            ClipToBounds = true;
            _baseLine.Stroke = ResourceBrush("PipeIdle", "#3D4E6B");
            _baseLine.StrokeThickness = PipeThickness;

            _flowLine.Stroke = ResourceBrush("PipeRun", "#3FB950");
            _flowLine.StrokeThickness = PipeThickness;
            _flowLine.StrokeDashArray = new DoubleCollection { 6, 8 };
            _flowLine.Visibility = Visibility.Collapsed;

            _canvas.Children.Add(_baseLine);
            _canvas.Children.Add(_flowLine);
            Content = _canvas;

            SizeChanged += (_, _) => UpdateGeometry();
            Loaded += (_, _) =>
            {
                UpdateGeometry();
                if (Active) StartFlowAnimation();
            };
        }

        private static void OnActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue.Equals(e.NewValue)) return; // 仅翻转时启停动画（B8 相位教训）
            var pipe = (PipeLine)d;
            if ((bool)e.NewValue)
                pipe.StartFlowAnimation();
            else
                pipe.StopFlowAnimation();
        }

        private static void OnVisualLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var pipe = (PipeLine)d;
            pipe.UpdateGeometry();
            if (pipe.Active)
                pipe.StartFlowAnimation();
        }

        private void UpdateGeometry()
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w <= 0 || h <= 0) return;

            _baseLine.StrokeThickness = PipeThickness;
            _flowLine.StrokeThickness = PipeThickness;
            _baseLine.Stroke = ResourceBrush("PipeIdle", "#3D4E6B");
            _flowLine.Stroke = ResourceBrush("PipeRun", "#3FB950");

            if (Orientation == Orientation.Horizontal)
            {
                double y = h / 2.0;
                _baseLine.X1 = 0; _baseLine.Y1 = y; _baseLine.X2 = w; _baseLine.Y2 = y;
                _flowLine.X1 = 0; _flowLine.Y1 = y; _flowLine.X2 = w; _flowLine.Y2 = y;
            }
            else
            {
                double x = w / 2.0;
                _baseLine.X1 = x; _baseLine.Y1 = 0; _baseLine.X2 = x; _baseLine.Y2 = h;
                _flowLine.X1 = x; _flowLine.Y1 = 0; _flowLine.X2 = x; _flowLine.Y2 = h;
            }
        }

        private void StartFlowAnimation()
        {
            _flowLine.Visibility = Visibility.Visible;
            double speed = FlowSpeed > 0 ? FlowSpeed : 0.6;
            bool forward = Direction is PipeFlowDirection.LeftToRight or PipeFlowDirection.TopToBottom;
            double from = 0;
            double to = forward ? -14.0 : 14.0;

            _anim = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = TimeSpan.FromSeconds(speed),
                RepeatBehavior = RepeatBehavior.Forever,
            };
            _flowLine.BeginAnimation(Shape.StrokeDashOffsetProperty, _anim);
        }

        private void StopFlowAnimation()
        {
            _flowLine.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
            _flowLine.Visibility = Visibility.Collapsed;
        }

        private static Brush ResourceBrush(string key, string fallbackHex)
        {
            return Application.Current?.TryFindResource(key) as Brush
                ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallbackHex));
        }
    }
}
