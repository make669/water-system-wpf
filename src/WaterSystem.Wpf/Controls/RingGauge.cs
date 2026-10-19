using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Intelligent_water_storage_system.Utils;

namespace WaterSystem.Wpf.Controls
{
    /// <summary>
    /// 环形仪表控件（自绘，附录 B RingGauge，§4.2-5）：
    /// - 尺寸：默认 100×100（单元网格自适应）；
    /// - 环弧：270° 暗底环（#223047）+ Accent 进度弧（#3B82F6 / #34D399）；
    /// - 读数：顶部/中心 Label + 18px Tabular 数值 + 单位；
    /// - 越限联动：超出 Min/Max 或阈值时，进度弧与数值转 Alarm 红（#F87171）；
    /// - 量程：严格对应 TagTable.csv（温度 -10..50、土壤湿度 0..100、降水 0..100、用水 0..500）。
    /// </summary>
    public class RingGauge : ContentControl
    {
        #region Dependency Properties

        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value), typeof(double), typeof(RingGauge),
            new PropertyMetadata(0.0, OnVisualChanged));

        public static readonly DependencyProperty MinValueProperty = DependencyProperty.Register(
            nameof(MinValue), typeof(double), typeof(RingGauge),
            new PropertyMetadata(0.0, OnVisualChanged));

        public static readonly DependencyProperty MaxValueProperty = DependencyProperty.Register(
            nameof(MaxValue), typeof(double), typeof(RingGauge),
            new PropertyMetadata(100.0, OnVisualChanged));

        public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
            nameof(Label), typeof(string), typeof(RingGauge),
            new PropertyMetadata("", OnVisualChanged));

        public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
            nameof(Unit), typeof(string), typeof(RingGauge),
            new PropertyMetadata("", OnVisualChanged));

        public static readonly DependencyProperty DecimalsProperty = DependencyProperty.Register(
            nameof(Decimals), typeof(int), typeof(RingGauge),
            new PropertyMetadata(1, OnVisualChanged));

        public static readonly DependencyProperty AlarmProperty = DependencyProperty.Register(
            nameof(Alarm), typeof(bool), typeof(RingGauge),
            new PropertyMetadata(false, OnVisualChanged));

        public static readonly DependencyProperty LowThresholdProperty = DependencyProperty.Register(
            nameof(LowThreshold), typeof(double?), typeof(RingGauge),
            new PropertyMetadata(null, OnVisualChanged));

        public static readonly DependencyProperty HighThresholdProperty = DependencyProperty.Register(
            nameof(HighThreshold), typeof(double?), typeof(RingGauge),
            new PropertyMetadata(null, OnVisualChanged));

        public static readonly DependencyProperty ArcThicknessProperty = DependencyProperty.Register(
            nameof(ArcThickness), typeof(double), typeof(RingGauge),
            new PropertyMetadata(6.5, OnVisualChanged));

        public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public double MinValue { get => (double)GetValue(MinValueProperty); set => SetValue(MinValueProperty, value); }
        public double MaxValue { get => (double)GetValue(MaxValueProperty); set => SetValue(MaxValueProperty, value); }
        public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
        public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
        public int Decimals { get => (int)GetValue(DecimalsProperty); set => SetValue(DecimalsProperty, value); }
        public bool Alarm { get => (bool)GetValue(AlarmProperty); set => SetValue(AlarmProperty, value); }
        public double? LowThreshold { get => (double?)GetValue(LowThresholdProperty); set => SetValue(LowThresholdProperty, value); }
        public double? HighThreshold { get => (double?)GetValue(HighThresholdProperty); set => SetValue(HighThresholdProperty, value); }
        public double ArcThickness { get => (double)GetValue(ArcThicknessProperty); set => SetValue(ArcThicknessProperty, value); }

        #endregion

        #region Visual Elements

        private readonly Canvas _canvas = new();
        private readonly Path _backgroundArc = new();
        private readonly Path _progressArc = new();
        private readonly StackPanel _centerStack = new();
        private readonly TextBlock _txtLabel = new();
        private readonly TextBlock _txtValue = new();
        private readonly TextBlock _txtUnit = new();

        #endregion

        public RingGauge()
        {
            Width = 100;
            Height = 100;
            ClipToBounds = false;
            InitializeVisuals();
            SizeChanged += (_, _) => RefreshVisuals();
            RefreshVisuals();
        }

        private void InitializeVisuals()
        {
            _canvas.ClipToBounds = false;

            // 背景底环
            _backgroundArc.Stroke = ResourceBrush("Stroke", "#223047");
            _backgroundArc.StrokeStartLineCap = PenLineCap.Round;
            _backgroundArc.StrokeEndLineCap = PenLineCap.Round;
            _canvas.Children.Add(_backgroundArc);

            // 前景进度弧
            _progressArc.Stroke = ResourceBrush("Accent", "#3B82F6");
            _progressArc.StrokeStartLineCap = PenLineCap.Round;
            _progressArc.StrokeEndLineCap = PenLineCap.Round;
            _canvas.Children.Add(_progressArc);

            // 中心文字叠放
            _centerStack.Orientation = Orientation.Vertical;
            _centerStack.HorizontalAlignment = HorizontalAlignment.Center;
            _centerStack.VerticalAlignment = VerticalAlignment.Center;

            _txtLabel.FontSize = 10;
            _txtLabel.HorizontalAlignment = HorizontalAlignment.Center;
            _txtLabel.Foreground = ResourceBrush("TextSecondary", "#8FA3BF");

            _txtValue.FontSize = 18;
            _txtValue.FontWeight = FontWeights.SemiBold;
            _txtValue.HorizontalAlignment = HorizontalAlignment.Center;
            _txtValue.Foreground = ResourceBrush("TextPrimary", "#E5EDF5");
            _txtValue.Margin = new Thickness(0, 1, 0, 0);

            _txtUnit.FontSize = 10;
            _txtUnit.HorizontalAlignment = HorizontalAlignment.Center;
            _txtUnit.Foreground = ResourceBrush("TextSecondary", "#8FA3BF");
            _txtUnit.Margin = new Thickness(0, 1, 0, 0);

            _centerStack.Children.Add(_txtLabel);
            _centerStack.Children.Add(_txtValue);
            _centerStack.Children.Add(_txtUnit);

            _canvas.Children.Add(_centerStack);
            Content = _canvas;
        }

        private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((RingGauge)d).RefreshVisuals();
        }

        private void RefreshVisuals()
        {
            double w = ActualWidth > 0 ? ActualWidth : (Width > 0 ? Width : 100);
            double h = ActualHeight > 0 ? ActualHeight : (Height > 0 ? Height : 100);

            _canvas.Width = w;
            _canvas.Height = h;

            double cx = w / 2.0;
            double cy = h / 2.0;
            double thickness = ArcThickness;
            double radius = Math.Max(10, Math.Min(cx, cy) - thickness / 2.0 - 4);

            _backgroundArc.StrokeThickness = thickness;
            _progressArc.StrokeThickness = thickness;

            // 270° 弧线：135° 到 405°（45°），底端敞开 90°
            const double startAngle = 135.0;
            const double totalSweep = 270.0;

            // 1. 背景弧（整圈 270°）
            _backgroundArc.Data = CreateArcGeometry(cx, cy, radius, startAngle, totalSweep);

            // 2. 进度弧计算
            double range = MaxValue - MinValue;
            double fraction = range > 0.0001 ? Math.Clamp((Value - MinValue) / range, 0.0, 1.0) : 0.0;
            double sweep = fraction * totalSweep;

            if (sweep <= 0.5)
            {
                _progressArc.Visibility = Visibility.Collapsed;
            }
            else
            {
                _progressArc.Visibility = Visibility.Visible;
                _progressArc.Data = CreateArcGeometry(cx, cy, radius, startAngle, sweep);
            }

            // 3. 越限状态判定
            bool isAlarm = Alarm
                || (HighThreshold.HasValue && Value > HighThreshold.Value)
                || (LowThreshold.HasValue && Value < LowThreshold.Value)
                || Value > MaxValue
                || Value < MinValue;

            if (isAlarm)
            {
                _progressArc.Stroke = ResourceBrush("Alarm", "#F87171");
                _txtValue.Foreground = ResourceBrush("Alarm", "#F87171");
            }
            else
            {
                _progressArc.Stroke = ResourceBrush("Accent", "#3B82F6");
                _txtValue.Foreground = ResourceBrush("TextPrimary", "#E5EDF5");
            }

            // 4. 文字内容更新
            _txtLabel.Text = Label;
            _txtValue.Text = ValueFormatter.Format(Value, Math.Pow(10, -Decimals));
            _txtUnit.Text = Unit;

            // 居中定位
            _centerStack.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double sw = _centerStack.DesiredSize.Width;
            double sh = _centerStack.DesiredSize.Height;
            Canvas.SetLeft(_centerStack, cx - sw / 2.0);
            Canvas.SetTop(_centerStack, cy - sh / 2.0);
        }

        private static PathGeometry CreateArcGeometry(double cx, double cy, double r, double startAngleDeg, double sweepDeg)
        {
            double startRad = startAngleDeg * Math.PI / 180.0;
            double endRad = (startAngleDeg + sweepDeg) * Math.PI / 180.0;

            Point startPoint = new Point(cx + r * Math.Cos(startRad), cy + r * Math.Sin(startRad));
            Point endPoint = new Point(cx + r * Math.Cos(endRad), cy + r * Math.Sin(endRad));

            bool isLargeArc = sweepDeg > 180.0;

            var figure = new PathFigure
            {
                StartPoint = startPoint,
                IsClosed = false
            };
            figure.Segments.Add(new ArcSegment(endPoint, new Size(r, r), 0, isLargeArc, SweepDirection.Clockwise, true));

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            return geometry;
        }

        private static Brush ResourceBrush(string key, string fallbackHex)
        {
            if (Application.Current != null && Application.Current.TryFindResource(key) is Brush brush)
                return brush;
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallbackHex));
        }
    }
}
