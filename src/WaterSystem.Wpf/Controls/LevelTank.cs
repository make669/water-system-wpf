using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace WaterSystem.Wpf.Controls
{
    /// <summary>
    /// 蓄水池液位自绘控件（附录 B LevelTank，§4.2-6）：
    /// - 槽体：圆角槽 + 深色工业背景 + 左侧计量标尺（0~6m）；
    /// - 液位层：蓝底透明渐变 + 顶波动画（Wave 顶浪，持续平滑平移，B8 纪律不因数据刷新重置相位）；
    /// - 阈值线：LowAt (1.0m) / HighAt (6.0m) 虚线指示；
    /// - 越限变色：CurWaterPos 低于 LowAt 或高于 HighAt 时，液位转 Alarm 红并提示；
    /// - 数值中心显：百分比 + 实际米数（或自定义 ValueText）。
    /// </summary>
    public class LevelTank : ContentControl
    {
        #region Dependency Properties

        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value), typeof(double), typeof(LevelTank),
            new PropertyMetadata(0.0, OnValueChanged));

        public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
            nameof(Level), typeof(double), typeof(LevelTank),
            new PropertyMetadata(0.0, OnLevelChanged, CoerceLevel));

        private static object CoerceLevel(DependencyObject d, object baseValue)
        {
            if (baseValue is double val)
                return Math.Clamp(val, 0.0, 1.0);
            return baseValue;
        }

        public static readonly DependencyProperty MinValueProperty = DependencyProperty.Register(
            nameof(MinValue), typeof(double), typeof(LevelTank),
            new PropertyMetadata(0.0, OnBoundsChanged));

        public static readonly DependencyProperty MaxValueProperty = DependencyProperty.Register(
            nameof(MaxValue), typeof(double), typeof(LevelTank),
            new PropertyMetadata(6.0, OnBoundsChanged));

        public static readonly DependencyProperty LowAtProperty = DependencyProperty.Register(
            nameof(LowAt), typeof(double), typeof(LevelTank),
            new PropertyMetadata(1.0, OnThresholdChanged));

        public static readonly DependencyProperty HighAtProperty = DependencyProperty.Register(
            nameof(HighAt), typeof(double), typeof(LevelTank),
            new PropertyMetadata(6.0, OnThresholdChanged));

        public static readonly DependencyProperty ValueTextProperty = DependencyProperty.Register(
            nameof(ValueText), typeof(string), typeof(LevelTank),
            new PropertyMetadata(null, OnVisualPropertyChanged));

        public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
            nameof(Unit), typeof(string), typeof(LevelTank),
            new PropertyMetadata("m", OnVisualPropertyChanged));

        public static readonly DependencyProperty IsAlarmProperty = DependencyProperty.Register(
            nameof(IsAlarm), typeof(bool), typeof(LevelTank),
            new PropertyMetadata(false, OnVisualPropertyChanged));

        public static readonly DependencyProperty ShowRulerProperty = DependencyProperty.Register(
            nameof(ShowRuler), typeof(bool), typeof(LevelTank),
            new PropertyMetadata(true, OnVisualPropertyChanged));

        public static readonly DependencyProperty WaveSpeedProperty = DependencyProperty.Register(
            nameof(WaveSpeed), typeof(double), typeof(LevelTank),
            new PropertyMetadata(2.0, OnWaveSpeedChanged));

        public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public double Level { get => (double)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
        public double MinValue { get => (double)GetValue(MinValueProperty); set => SetValue(MinValueProperty, value); }
        public double MaxValue { get => (double)GetValue(MaxValueProperty); set => SetValue(MaxValueProperty, value); }
        public double LowAt { get => (double)GetValue(LowAtProperty); set => SetValue(LowAtProperty, value); }
        public double HighAt { get => (double)GetValue(HighAtProperty); set => SetValue(HighAtProperty, value); }
        public string? ValueText { get => (string?)GetValue(ValueTextProperty); set => SetValue(ValueTextProperty, value); }
        public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
        public bool IsAlarm { get => (bool)GetValue(IsAlarmProperty); set => SetValue(IsAlarmProperty, value); }
        public bool ShowRuler { get => (bool)GetValue(ShowRulerProperty); set => SetValue(ShowRulerProperty, value); }
        public double WaveSpeed { get => (double)GetValue(WaveSpeedProperty); set => SetValue(WaveSpeedProperty, value); }

        #endregion

        #region Visual Elements

        private readonly Border _outerBorder = new();
        private readonly Canvas _tankCanvas = new();
        private readonly Canvas _rulerCanvas = new();
        private readonly Canvas _thresholdCanvas = new();

        // 液体层与顶波
        private readonly Canvas _liquidContainer = new();
        private readonly Rectangle _liquidRect = new();
        private readonly Path _wavePath = new();
        private readonly TranslateTransform _waveTransform = new();

        // 中心数值面板
        private readonly Border _readoutCard = new();
        private readonly TextBlock _txtPercent = new();
        private readonly TextBlock _txtValue = new();
        private readonly TextBlock _txtAlarm = new();

        private bool _isUpdating;
        private DoubleAnimation? _waveAnimation;

        #endregion

        public LevelTank()
        {
            ClipToBounds = true;
            InitializeVisualTree();
            Loaded += (_, _) => StartWaveAnimation();
            Unloaded += (_, _) => StopWaveAnimation();
            SizeChanged += (_, _) => RefreshVisuals();
            RefreshVisuals();
        }

        private void InitializeVisualTree()
        {
            // 槽体外框
            _outerBorder.CornerRadius = new CornerRadius(8);
            _outerBorder.BorderThickness = new Thickness(1.5);
            _outerBorder.BorderBrush = ResourceBrush("Stroke", "#223047");
            _outerBorder.Background = new SolidColorBrush(Color.FromRgb(0x10, 0x19, 0x2A));
            _outerBorder.ClipToBounds = true;

            var rootGrid = new Grid { ClipToBounds = true };
            _outerBorder.Child = rootGrid;

            // 画布层
            _tankCanvas.ClipToBounds = true;
            rootGrid.Children.Add(_tankCanvas);

            // 液体容器（内含波浪和液体矩形）
            _liquidContainer.ClipToBounds = true;
            _tankCanvas.Children.Add(_liquidContainer);

            // 顶浪 Path
            _wavePath.RenderTransform = _waveTransform;
            _wavePath.Data = BuildWaveGeometry();
            _liquidContainer.Children.Add(_wavePath);

            // 液体主体矩形
            _liquidContainer.Children.Add(_liquidRect);

            // 标尺层
            _rulerCanvas.ClipToBounds = false;
            rootGrid.Children.Add(_rulerCanvas);

            // 阈值线层
            _thresholdCanvas.ClipToBounds = true;
            rootGrid.Children.Add(_thresholdCanvas);

            // 中心读数卡
            _readoutCard.Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x0E, 0x16, 0x26));
            _readoutCard.BorderBrush = ResourceBrush("Stroke", "#223047");
            _readoutCard.BorderThickness = new Thickness(1);
            _readoutCard.CornerRadius = new CornerRadius(6);
            _readoutCard.Padding = new Thickness(8, 4, 8, 4);
            _readoutCard.HorizontalAlignment = HorizontalAlignment.Center;
            _readoutCard.VerticalAlignment = VerticalAlignment.Center;

            var readoutStack = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center };
            _txtPercent.FontSize = 20;
            _txtPercent.FontWeight = FontWeights.Bold;
            _txtPercent.HorizontalAlignment = HorizontalAlignment.Center;
            _txtPercent.Foreground = ResourceBrush("TextPrimary", "#E5EDF5");

            _txtValue.FontSize = 11;
            _txtValue.HorizontalAlignment = HorizontalAlignment.Center;
            _txtValue.Foreground = ResourceBrush("TextSecondary", "#8FA3BF");
            _txtValue.Margin = new Thickness(0, 1, 0, 0);

            _txtAlarm.FontSize = 10;
            _txtAlarm.FontWeight = FontWeights.SemiBold;
            _txtAlarm.HorizontalAlignment = HorizontalAlignment.Center;
            _txtAlarm.Foreground = ResourceBrush("Alarm", "#F87171");
            _txtAlarm.Visibility = Visibility.Collapsed;
            _txtAlarm.Margin = new Thickness(0, 2, 0, 0);

            readoutStack.Children.Add(_txtPercent);
            readoutStack.Children.Add(_txtValue);
            readoutStack.Children.Add(_txtAlarm);
            _readoutCard.Child = readoutStack;

            rootGrid.Children.Add(_readoutCard);

            Content = _outerBorder;
        }

        private static Geometry BuildWaveGeometry()
        {
            // 构造无缝循环的正弦波形（周期 60px，宽 540px，高 6px）
            var pathFigure = new PathFigure
            {
                StartPoint = new Point(0, 6),
                IsClosed = true,
                IsFilled = true
            };

            const double waveLength = 60.0;
            const double waveHeight = 6.0;
            const int cycles = 9; // 9 * 60 = 540px
            double currentX = 0;

            for (int i = 0; i < cycles; i++)
            {
                // 用两段三次贝塞尔平滑逼近一段正弦波
                var p1 = new Point(currentX + waveLength * 0.25, 0);
                var p2 = new Point(currentX + waveLength * 0.25, 0);
                var mid = new Point(currentX + waveLength * 0.5, waveHeight * 0.5);
                pathFigure.Segments.Add(new BezierSegment(p1, p2, mid, true));

                var p3 = new Point(currentX + waveLength * 0.75, waveHeight);
                var p4 = new Point(currentX + waveLength * 0.75, waveHeight);
                var end = new Point(currentX + waveLength, waveHeight * 0.5);
                pathFigure.Segments.Add(new BezierSegment(p3, p4, end, true));

                currentX += waveLength;
            }

            pathFigure.Segments.Add(new LineSegment(new Point(currentX, 14), true));
            pathFigure.Segments.Add(new LineSegment(new Point(0, 14), true));

            var geo = new PathGeometry();
            geo.Figures.Add(pathFigure);
            return geo;
        }

        private void StartWaveAnimation()
        {
            if (_waveAnimation != null) return;

            // 保持无缝平移（X: 0 -> -60），周期 WaveSpeed 秒，无限循环
            _waveAnimation = new DoubleAnimation
            {
                From = 0.0,
                To = -60.0,
                Duration = TimeSpan.FromSeconds(Math.Max(0.5, WaveSpeed)),
                RepeatBehavior = RepeatBehavior.Forever
            };
            _waveTransform.BeginAnimation(TranslateTransform.XProperty, _waveAnimation);
        }

        private void StopWaveAnimation()
        {
            _waveTransform.BeginAnimation(TranslateTransform.XProperty, null);
            _waveAnimation = null;
        }

        #region Property Change Handlers

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var tank = (LevelTank)d;
            if (tank._isUpdating) return;
            tank._isUpdating = true;
            try
            {
                var val = (double)e.NewValue;
                var range = tank.MaxValue - tank.MinValue;
                tank.Level = range > 0.0001 ? Math.Clamp((val - tank.MinValue) / range, 0.0, 1.0) : 0.0;
                tank.EvaluateAlarm();
                tank.RefreshVisuals();
            }
            finally
            {
                tank._isUpdating = false;
            }
        }

        private static void OnLevelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var tank = (LevelTank)d;
            if (tank._isUpdating) return;
            tank._isUpdating = true;
            try
            {
                var lvl = Math.Clamp((double)e.NewValue, 0.0, 1.0);
                var range = tank.MaxValue - tank.MinValue;
                tank.Value = tank.MinValue + lvl * range;
                tank.EvaluateAlarm();
                tank.RefreshVisuals();
            }
            finally
            {
                tank._isUpdating = false;
            }
        }

        private static void OnBoundsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var tank = (LevelTank)d;
            var range = tank.MaxValue - tank.MinValue;
            tank.Level = range > 0.0001 ? Math.Clamp((tank.Value - tank.MinValue) / range, 0.0, 1.0) : 0.0;
            tank.EvaluateAlarm();
            tank.RefreshVisuals();
        }

        private static void OnThresholdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var tank = (LevelTank)d;
            tank.EvaluateAlarm();
            tank.RefreshVisuals();
        }

        private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((LevelTank)d).RefreshVisuals();
        }

        private static void OnWaveSpeedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var tank = (LevelTank)d;
            tank.StopWaveAnimation();
            tank.StartWaveAnimation();
        }

        private void EvaluateAlarm()
        {
            // 越限判断：低于低水位阈值或高于高水位阈值
            bool alarm = Value < LowAt || Value > HighAt;
            if (IsAlarm != alarm)
            {
                IsAlarm = alarm;
            }
        }

        #endregion

        #region Visual Refresh

        private void RefreshVisuals()
        {
            double w = ActualWidth > 0 ? ActualWidth : (Width > 0 ? Width : 210);
            double h = ActualHeight > 0 ? ActualHeight : (Height > 0 ? Height : 150);

            // 考虑边框与内边距
            double innerW = Math.Max(10, w - 3.0);
            double innerH = Math.Max(10, h - 3.0);

            _tankCanvas.Width = innerW;
            _tankCanvas.Height = innerH;
            _thresholdCanvas.Width = innerW;
            _thresholdCanvas.Height = innerH;

            // 1. 液位高度与波浪定位
            double clampedLevel = Math.Clamp(Level, 0.0, 1.0);
            double liquidHeight = clampedLevel * innerH;

            _liquidContainer.Width = innerW;
            _liquidContainer.Height = innerH;

            if (liquidHeight <= 0.01)
            {
                _liquidRect.Visibility = Visibility.Collapsed;
                _wavePath.Visibility = Visibility.Collapsed;
            }
            else
            {
                _liquidRect.Visibility = Visibility.Visible;
                _wavePath.Visibility = Visibility.Visible;

                double waveTop = innerH - liquidHeight;

                Canvas.SetLeft(_wavePath, 0);
                Canvas.SetTop(_wavePath, Math.Max(0, waveTop - 3.0));

                Canvas.SetLeft(_liquidRect, 0);
                Canvas.SetTop(_liquidRect, waveTop + 3.0);
                _liquidRect.Width = innerW;
                _liquidRect.Height = Math.Max(0, liquidHeight - 3.0);
            }

            // 2. 配色应用（正常蓝 vs 越限报警红）
            Brush liquidBrush;
            if (IsAlarm)
            {
                var grad = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(0, 1)
                };
                grad.GradientStops.Add(new GradientStop(Color.FromArgb(0xBB, 0xF8, 0x71, 0x71), 0.0));
                grad.GradientStops.Add(new GradientStop(Color.FromArgb(0xEE, 0xDC, 0x26, 0x26), 1.0));
                liquidBrush = grad;
                _outerBorder.BorderBrush = ResourceBrush("Alarm", "#F87171");
            }
            else
            {
                var grad = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(0, 1)
                };
                grad.GradientStops.Add(new GradientStop(Color.FromArgb(0x66, 0x3B, 0x82, 0xF6), 0.0));
                grad.GradientStops.Add(new GradientStop(Color.FromArgb(0xAA, 0x1D, 0x4E, 0xD8), 1.0));
                liquidBrush = grad;
                _outerBorder.BorderBrush = ResourceBrush("Stroke", "#223047");
            }

            _liquidRect.Fill = liquidBrush;
            _wavePath.Fill = liquidBrush;

            // 3. 标尺绘制（0~6m）
            DrawRuler(innerW, innerH);

            // 4. 阈值线绘制（LowAt / HighAt）
            DrawThresholds(innerW, innerH);

            // 5. 中心数值与报警状态
            _txtPercent.Text = $"{clampedLevel * 100:F0}%";
            _txtPercent.Foreground = IsAlarm ? ResourceBrush("Alarm", "#F87171") : ResourceBrush("TextPrimary", "#E5EDF5");

            string displayValue = ValueText ?? $"{Value:F1} {Unit} / {MaxValue:F1} {Unit}";
            _txtValue.Text = displayValue;

            if (IsAlarm)
            {
                _txtAlarm.Visibility = Visibility.Visible;
                if (Value < LowAt)
                    _txtAlarm.Text = "▲ 液位过低";
                else if (Value > HighAt)
                    _txtAlarm.Text = "▲ 液位过高";
                else
                    _txtAlarm.Text = "▲ 液位异常";
            }
            else
            {
                _txtAlarm.Visibility = Visibility.Collapsed;
            }
        }

        private void DrawRuler(double width, double height)
        {
            _rulerCanvas.Children.Clear();
            if (!ShowRuler) return;

            double rulerX = 8.0;
            var rulerStroke = new SolidColorBrush(Color.FromArgb(0x66, 0x8F, 0xA3, 0xBF));
            var tickStroke = new SolidColorBrush(Color.FromArgb(0x88, 0x8F, 0xA3, 0xBF));
            var textBrush = ResourceBrush("TextSecondary", "#8FA3BF");

            // 标尺纵轴线
            var axis = new Line
            {
                X1 = rulerX,
                Y1 = 8,
                X2 = rulerX,
                Y2 = height - 8,
                Stroke = rulerStroke,
                StrokeThickness = 1
            };
            _rulerCanvas.Children.Add(axis);

            int maxInt = (int)Math.Ceiling(MaxValue);
            double range = MaxValue - MinValue;
            if (range <= 0.0001) return;

            for (int i = (int)Math.Floor(MinValue); i <= maxInt; i++)
            {
                double fraction = (i - MinValue) / range;
                if (fraction < 0 || fraction > 1.0) continue;

                double y = height - 8 - fraction * (height - 16);

                // 主刻度线
                var tick = new Line
                {
                    X1 = rulerX,
                    Y1 = y,
                    X2 = rulerX + 5,
                    Y2 = y,
                    Stroke = tickStroke,
                    StrokeThickness = 1
                };
                _rulerCanvas.Children.Add(tick);

                // 刻度数值标签
                var txt = new TextBlock
                {
                    Text = i.ToString(CultureInfo.InvariantCulture),
                    FontSize = 9,
                    Foreground = textBrush,
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                Canvas.SetLeft(txt, rulerX + 7);
                Canvas.SetTop(txt, y - 6);
                _rulerCanvas.Children.Add(txt);

                // 半刻度线（中间 0.5）
                if (i < maxInt)
                {
                    double halfFraction = (i + 0.5 - MinValue) / range;
                    if (halfFraction >= 0 && halfFraction <= 1.0)
                    {
                        double halfY = height - 8 - halfFraction * (height - 16);
                        var halfTick = new Line
                        {
                            X1 = rulerX,
                            Y1 = halfY,
                            X2 = rulerX + 3,
                            Y2 = halfY,
                            Stroke = tickStroke,
                            StrokeThickness = 0.8
                        };
                        _rulerCanvas.Children.Add(halfTick);
                    }
                }
            }
        }

        private void DrawThresholds(double width, double height)
        {
            _thresholdCanvas.Children.Clear();
            double range = MaxValue - MinValue;
            if (range <= 0.0001) return;

            // 低限虚线
            if (LowAt > MinValue && LowAt <= MaxValue)
            {
                double lowFraction = (LowAt - MinValue) / range;
                double lowY = height - 8 - lowFraction * (height - 16);

                var lowLine = new Line
                {
                    X1 = 26,
                    Y1 = lowY,
                    X2 = width - 4,
                    Y2 = lowY,
                    Stroke = ResourceBrush("WarnText", "#FBBF24"),
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 3, 3 },
                    Opacity = 0.75
                };
                _thresholdCanvas.Children.Add(lowLine);

                var lblLow = new TextBlock
                {
                    Text = $"低 {LowAt:F1}m",
                    FontSize = 9,
                    Foreground = ResourceBrush("WarnText", "#FBBF24"),
                    Opacity = 0.85
                };
                Canvas.SetRight(lblLow, 6);
                Canvas.SetTop(lblLow, lowY - 12);
                _thresholdCanvas.Children.Add(lblLow);
            }

            // 高限虚线
            if (HighAt >= MinValue && HighAt <= MaxValue)
            {
                double highFraction = (HighAt - MinValue) / range;
                double highY = height - 8 - highFraction * (height - 16);

                var highLine = new Line
                {
                    X1 = 26,
                    Y1 = highY,
                    X2 = width - 4,
                    Y2 = highY,
                    Stroke = ResourceBrush("Alarm", "#F87171"),
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 3, 3 },
                    Opacity = 0.75
                };
                _thresholdCanvas.Children.Add(highLine);

                var lblHigh = new TextBlock
                {
                    Text = $"高 {HighAt:F1}m",
                    FontSize = 9,
                    Foreground = ResourceBrush("Alarm", "#F87171"),
                    Opacity = 0.85
                };
                Canvas.SetRight(lblHigh, 6);
                Canvas.SetTop(lblHigh, Math.Max(2, highY - 12));
                _thresholdCanvas.Children.Add(lblHigh);
            }
        }

        #endregion

        private static Brush ResourceBrush(string key, string fallbackHex)
        {
            if (Application.Current != null && Application.Current.TryFindResource(key) is Brush brush)
                return brush;
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallbackHex));
        }
    }
}
