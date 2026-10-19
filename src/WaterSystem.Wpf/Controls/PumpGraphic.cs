using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace WaterSystem.Wpf.Controls
{
    public enum PumpRunState
    {
        Stopped,
        Running,
        Fault,
    }

    /// <summary>
    /// 工艺水泵动态自绘控件（附录 B PumpGraphic，§4.2-7 & §3.5）：
    /// - 泵体：工业离心泵造型（蜗壳 + 进出管法兰 + 泵座）；
    /// - 状态机：
    ///     Stopped: Off 灰蓝（#3D4E6B / #64748B），叶轮静止；
    ///     Running: PipeRun 绿（#3FB950），叶轮 1s/圈 连续旋转；
    ///     Fault:   Alarm 红（#F87171）+ 1.5s 周期呼吸闪烁，叶轮静止；
    ///     离线:    整体半透明 50%，动画停滞；
    /// - 【关键纪律 B8】：仅在状态翻转时启停动画，值不变不重启动画（防重绘重置相位与卡顿）。
    /// </summary>
    public class PumpGraphic : ContentControl
    {
        #region Dependency Properties

        public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
            nameof(State), typeof(PumpRunState), typeof(PumpGraphic),
            new PropertyMetadata(PumpRunState.Stopped, OnStateChanged));

        public static readonly DependencyProperty IsRunningProperty = DependencyProperty.Register(
            nameof(IsRunning), typeof(bool), typeof(PumpGraphic),
            new PropertyMetadata(false, OnFlagsChanged));

        public static readonly DependencyProperty IsFaultProperty = DependencyProperty.Register(
            nameof(IsFault), typeof(bool), typeof(PumpGraphic),
            new PropertyMetadata(false, OnFlagsChanged));

        public static readonly DependencyProperty IsOnlineProperty = DependencyProperty.Register(
            nameof(IsOnline), typeof(bool), typeof(PumpGraphic),
            new PropertyMetadata(true, OnVisualChanged));

        public static readonly DependencyProperty ImpellerSpeedProperty = DependencyProperty.Register(
            nameof(ImpellerSpeed), typeof(double), typeof(PumpGraphic),
            new PropertyMetadata(1.0, OnVisualChanged));

        public static readonly DependencyProperty RunValueProperty = DependencyProperty.Register(
            nameof(RunValue), typeof(double), typeof(PumpGraphic),
            new PropertyMetadata(0.0, (d, e) => ((PumpGraphic)d).OnNumericChanged()));

        public static readonly DependencyProperty FaultValueProperty = DependencyProperty.Register(
            nameof(FaultValue), typeof(double), typeof(PumpGraphic),
            new PropertyMetadata(0.0, (d, e) => ((PumpGraphic)d).OnNumericChanged()));

        public PumpRunState State { get => (PumpRunState)GetValue(StateProperty); set => SetValue(StateProperty, value); }
        public bool IsRunning { get => (bool)GetValue(IsRunningProperty); set => SetValue(IsRunningProperty, value); }
        public bool IsFault { get => (bool)GetValue(IsFaultProperty); set => SetValue(IsFaultProperty, value); }
        public double RunValue { get => (double)GetValue(RunValueProperty); set => SetValue(RunValueProperty, value); }
        public double FaultValue { get => (double)GetValue(FaultValueProperty); set => SetValue(FaultValueProperty, value); }
        public bool IsOnline { get => (bool)GetValue(IsOnlineProperty); set => SetValue(IsOnlineProperty, value); }
        public double ImpellerSpeed { get => (double)GetValue(ImpellerSpeedProperty); set => SetValue(ImpellerSpeedProperty, value); }

        private bool _isSyncing;

        private void OnNumericChanged()
        {
            if (_isSyncing) return;
            _isSyncing = true;
            try
            {
                IsFault = FaultValue > 0.5;
                IsRunning = RunValue > 0.5;
                PumpRunState state = IsFault ? PumpRunState.Fault : (IsRunning ? PumpRunState.Running : PumpRunState.Stopped);
                if (State != state)
                    State = state;
                else
                    UpdateVisualState(force: false);
            }
            finally
            {
                _isSyncing = false;
            }
        }

        #endregion

        #region Visual Elements

        private readonly Canvas _canvas = new();
        private readonly Path _voluteCasing = new();
        private readonly Rectangle _topFlange = new();
        private readonly Rectangle _bottomFlange = new();
        private readonly Rectangle _mountBase = new();
        private readonly Canvas _impellerCanvas = new();
        private readonly Path _impellerBlades = new();
        private readonly Ellipse _impellerHub = new();
        private readonly RotateTransform _impellerRotate = new();

        private PumpRunState _renderedState = PumpRunState.Stopped;
        private bool _isAnimatingRotation;
        private bool _isAnimatingFault;

        #endregion

        public PumpGraphic()
        {
            Width = 110;
            Height = 100;
            ClipToBounds = false;
            InitializeVisuals();
            UpdateVisualState(force: true);
        }

        private void InitializeVisuals()
        {
            _canvas.Width = 110;
            _canvas.Height = 100;
            _canvas.ClipToBounds = false;

            // 1. 泵座固定底脚
            _mountBase.Width = 56;
            _mountBase.Height = 6;
            _mountBase.RadiusX = 2;
            _mountBase.RadiusY = 2;
            _mountBase.Fill = ResourceBrush("Stroke", "#223047");
            Canvas.SetLeft(_mountBase, 27);
            Canvas.SetTop(_mountBase, 86);
            _canvas.Children.Add(_mountBase);

            // 2. 上进管/出管法兰 (y=4..12)
            _topFlange.Width = 24;
            _topFlange.Height = 6;
            _topFlange.RadiusX = 1.5;
            _topFlange.RadiusY = 1.5;
            _topFlange.Fill = ResourceBrush("PipeIdle", "#3D4E6B");
            Canvas.SetLeft(_topFlange, 43);
            Canvas.SetTop(_topFlange, 4);
            _canvas.Children.Add(_topFlange);

            // 3. 下管法兰 (y=80..86)
            _bottomFlange.Width = 24;
            _bottomFlange.Height = 5;
            _bottomFlange.RadiusX = 1.5;
            _bottomFlange.RadiusY = 1.5;
            _bottomFlange.Fill = ResourceBrush("PipeIdle", "#3D4E6B");
            Canvas.SetLeft(_bottomFlange, 43);
            Canvas.SetTop(_bottomFlange, 81);
            _canvas.Children.Add(_bottomFlange);

            // 4. 离心蜗壳泵体
            // 泵体中心约 (55, 48)，半径 36，顶部延伸管连接上法兰
            _voluteCasing.Data = Geometry.Parse(
                "M 45,10 L 65,10 L 65,22 " +
                "A 36,36 0 1,1 45,22 Z");
            _voluteCasing.StrokeThickness = 2.5;
            _voluteCasing.StrokeLineJoin = PenLineJoin.Round;
            _canvas.Children.Add(_voluteCasing);

            // 5. 叶轮组件（中心在 55, 52）
            _impellerCanvas.Width = 110;
            _impellerCanvas.Height = 100;
            _impellerRotate.CenterX = 55;
            _impellerRotate.CenterY = 52;
            _impellerCanvas.RenderTransform = _impellerRotate;

            // 4 叶弯曲叶片
            _impellerBlades.Data = BuildImpellerGeometry(55, 52, 24);
            _impellerBlades.StrokeThickness = 2;
            _impellerBlades.StrokeLineJoin = PenLineJoin.Round;
            _impellerCanvas.Children.Add(_impellerBlades);

            // 叶轮中心轴套
            _impellerHub.Width = 14;
            _impellerHub.Height = 14;
            Canvas.SetLeft(_impellerHub, 55 - 7);
            Canvas.SetTop(_impellerHub, 52 - 7);
            _impellerCanvas.Children.Add(_impellerHub);

            _canvas.Children.Add(_impellerCanvas);
            Content = _canvas;
        }

        private static Geometry BuildImpellerGeometry(double cx, double cy, double r)
        {
            var group = new GeometryGroup();
            for (int i = 0; i < 4; i++)
            {
                double angle = i * Math.PI / 2.0;
                double x0 = cx + Math.Cos(angle) * 6;
                double y0 = cy + Math.Sin(angle) * 6;
                double x1 = cx + Math.Cos(angle + 0.3) * (r * 0.6);
                double y1 = cy + Math.Sin(angle + 0.3) * (r * 0.6);
                double x2 = cx + Math.Cos(angle + 0.5) * r;
                double y2 = cy + Math.Sin(angle + 0.5) * r;

                var fig = new PathFigure { StartPoint = new Point(x0, y0), IsClosed = false };
                fig.Segments.Add(new BezierSegment(new Point(x0, y0), new Point(x1, y1), new Point(x2, y2), true));
                var pg = new PathGeometry();
                pg.Figures.Add(fig);
                group.Children.Add(pg);
            }
            return group;
        }

        #region State Updates & Animation

        private static void OnFlagsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var pump = (PumpGraphic)d;
            if (pump._isSyncing) return;
            pump._isSyncing = true;
            try
            {
                PumpRunState state;
                if (pump.IsFault)
                    state = PumpRunState.Fault;
                else if (pump.IsRunning)
                    state = PumpRunState.Running;
                else
                    state = PumpRunState.Stopped;

                if (pump.State != state)
                {
                    pump.State = state;
                }
                else
                {
                    pump.UpdateVisualState(force: false);
                }
            }
            finally
            {
                pump._isSyncing = false;
            }
        }

        private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var pump = (PumpGraphic)d;
            if (pump._isSyncing) return;
            pump._isSyncing = true;
            try
            {
                var st = (PumpRunState)e.NewValue;
                switch (st)
                {
                    case PumpRunState.Running:
                        pump.IsRunning = true;
                        pump.IsFault = false;
                        break;
                    case PumpRunState.Fault:
                        pump.IsFault = true;
                        break;
                    case PumpRunState.Stopped:
                        pump.IsRunning = false;
                        pump.IsFault = false;
                        break;
                }
                pump.UpdateVisualState(force: false);
            }
            finally
            {
                pump._isSyncing = false;
            }
        }

        private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((PumpGraphic)d).UpdateVisualState(force: true);
        }

        private void UpdateVisualState(bool force)
        {
            var targetState = State;
            if (!IsOnline)
            {
                Opacity = 0.5;
            }
            else
            {
                Opacity = 1.0;
            }

            // 【B8 保护】：状态不变且非强制更新时不重启任何动画
            if (!force && targetState == _renderedState)
                return;

            _renderedState = targetState;

            switch (targetState)
            {
                case PumpRunState.Running:
                    ApplyRunningVisuals();
                    break;

                case PumpRunState.Fault:
                    ApplyFaultVisuals();
                    break;

                case PumpRunState.Stopped:
                default:
                    ApplyStoppedVisuals();
                    break;
            }
        }

        private void ApplyRunningVisuals()
        {
            StopFaultBlink();

            // 绿光泵体
            _voluteCasing.Stroke = ResourceBrush("PipeRun", "#3FB950");
            _voluteCasing.Fill = new SolidColorBrush(Color.FromArgb(0x33, 0x3F, 0xB9, 0x50));
            _topFlange.Fill = ResourceBrush("PipeRun", "#3FB950");
            _bottomFlange.Fill = ResourceBrush("PipeRun", "#3FB950");

            // 亮绿叶轮
            _impellerBlades.Stroke = ResourceBrush("PipeRun", "#3FB950");
            _impellerHub.Fill = ResourceBrush("PipeRun", "#3FB950");

            StartImpellerRotation();
        }

        private void ApplyFaultVisuals()
        {
            StopImpellerRotation();

            // 报警红
            _voluteCasing.Stroke = ResourceBrush("Alarm", "#F87171");
            _voluteCasing.Fill = new SolidColorBrush(Color.FromArgb(0x44, 0xF8, 0x71, 0x71));
            _topFlange.Fill = ResourceBrush("Alarm", "#F87171");
            _bottomFlange.Fill = ResourceBrush("Alarm", "#F87171");

            _impellerBlades.Stroke = ResourceBrush("Alarm", "#F87171");
            _impellerHub.Fill = ResourceBrush("Alarm", "#F87171");

            StartFaultBlink();
        }

        private void ApplyStoppedVisuals()
        {
            StopImpellerRotation();
            StopFaultBlink();

            // 灰蓝/Off
            _voluteCasing.Stroke = ResourceBrush("PipeIdle", "#3D4E6B");
            _voluteCasing.Fill = new SolidColorBrush(Color.FromArgb(0x22, 0x3D, 0x4E, 0x6B));
            _topFlange.Fill = ResourceBrush("PipeIdle", "#3D4E6B");
            _bottomFlange.Fill = ResourceBrush("PipeIdle", "#3D4E6B");

            _impellerBlades.Stroke = ResourceBrush("Off", "#64748B");
            _impellerHub.Fill = ResourceBrush("Off", "#64748B");
        }

        private void StartImpellerRotation()
        {
            if (_isAnimatingRotation) return;
            _isAnimatingRotation = true;

            var rotateAnim = new DoubleAnimation
            {
                From = 0.0,
                To = 360.0,
                Duration = TimeSpan.FromSeconds(Math.Max(0.2, ImpellerSpeed)),
                RepeatBehavior = RepeatBehavior.Forever
            };
            _impellerRotate.BeginAnimation(RotateTransform.AngleProperty, rotateAnim);
        }

        private void StopImpellerRotation()
        {
            if (!_isAnimatingRotation) return;
            _isAnimatingRotation = false;
            _impellerRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            _impellerRotate.Angle = 0;
        }

        private void StartFaultBlink()
        {
            if (_isAnimatingFault) return;
            _isAnimatingFault = true;

            var blinkAnim = new DoubleAnimation
            {
                From = 1.0,
                To = 0.35,
                Duration = TimeSpan.FromMilliseconds(750), // 1.5s 周期 (来回 750ms * 2)
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            _voluteCasing.BeginAnimation(UIElement.OpacityProperty, blinkAnim);
        }

        private void StopFaultBlink()
        {
            if (!_isAnimatingFault) return;
            _isAnimatingFault = false;
            _voluteCasing.BeginAnimation(UIElement.OpacityProperty, null);
            _voluteCasing.Opacity = 1.0;
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
