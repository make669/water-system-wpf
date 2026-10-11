using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace WaterSystem.Wpf.Controls
{
    /// <summary>
    /// 水位趋势图（自绘，D7）：Polyline + 阈值红虚线 + Y 轴单位刻度 + 图例；数据 300 点滚动。
    /// 快照经 DP 注入（VM 每轮发布 double[]）；空快照 = 空轴"等待连接"（S1 空态，不画过期点）。
    /// 渲染失败必须显式（B10 教训：源 chart1 系列缺失静默失败曾致每秒重连风暴）。
    /// </summary>
    public class WaterTrendChart : System.Windows.Controls.ContentControl
    {
        public static readonly DependencyProperty SnapshotProperty = DependencyProperty.Register(
            nameof(Snapshot), typeof(double[]), typeof(WaterTrendChart),
            new PropertyMetadata(null, OnSnapshotChanged));

        public static readonly DependencyProperty ThresholdLowProperty = DependencyProperty.Register(
            nameof(ThresholdLow), typeof(double), typeof(WaterTrendChart), new PropertyMetadata(1.0, OnLayoutChanged));

        public static readonly DependencyProperty ThresholdHighProperty = DependencyProperty.Register(
            nameof(ThresholdHigh), typeof(double), typeof(WaterTrendChart), new PropertyMetadata(6.0, OnLayoutChanged));

        public static readonly DependencyProperty YMaxProperty = DependencyProperty.Register(
            nameof(YMax), typeof(double), typeof(WaterTrendChart), new PropertyMetadata(6.5, OnLayoutChanged));

        private readonly Canvas _canvas = new();
        private readonly List<double> _points = new();

        /// <summary>当前渲染点数（单测断言面）。</summary>
        public int RenderedPointCount => _points.Count;

        public double[]? Snapshot { get => (double[]?)GetValue(SnapshotProperty); set => SetValue(SnapshotProperty, value); }
        public double ThresholdLow { get => (double)GetValue(ThresholdLowProperty); set => SetValue(ThresholdLowProperty, value); }
        public double ThresholdHigh { get => (double)GetValue(ThresholdHighProperty); set => SetValue(ThresholdHighProperty, value); }
        public double YMax { get => (double)GetValue(YMaxProperty); set => SetValue(YMaxProperty, value); }

        public WaterTrendChart()
        {
            ClipToBounds = true;
            Content = _canvas;
            SizeChanged += (_, _) => Redraw();
        }

        private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((WaterTrendChart)d).Redraw();

        private static void OnSnapshotChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var chart = (WaterTrendChart)d;
            chart.OnSnapshot(e);
        }

        private void OnSnapshot(DependencyPropertyChangedEventArgs e)
        {
            _points.Clear();
            if (Snapshot is { } values)
                _points.AddRange(values);
            if (_points.Count > 300)
                _points.RemoveRange(0, _points.Count - 300);   // 300 点滚动（FrmMain.cs:898-911 语义）
            Redraw();
        }

        private static Brush B(string key) =>
            Application.Current?.TryFindResource(key) as Brush
            ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8FA3BF"));

        private void Redraw()
        {
            _canvas.Children.Clear();
            double w = ActualWidth, h = ActualHeight;
            if (w < 60 || h < 30) return;

            const double left = 42, top = 8;
            double plotW = w - left - 10, plotH = h - top - 22;
            double yMax = YMax;

            // 网格 + Y 刻度（0..yMax，4 条虚线）
            for (int i = 0; i <= 3; i++)
            {
                double v = yMax * i / 3;
                double y = top + plotH * (1 - v / yMax);
                _canvas.Children.Add(new Line
                {
                    X1 = left, Y1 = y, X2 = left + plotW, Y2 = y,
                    Stroke = B("Stroke"), StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 2, 4 },
                });
                _canvas.Children.Add(new TextBlock
                {
                    Text = v.ToString("0.#"), FontSize = 9,
                    Foreground = B("TextSecondary"),
                }.Also(tb => { Canvas.SetLeft(tb, left - 26); Canvas.SetTop(tb, y - 7); }));
            }

            // 阈值线（红虚线 + 标注）
            DrawThreshold(top, plotH, ThresholdHigh, "上限");
            DrawThreshold(top, plotH, ThresholdLow, "下限");

            // 曲线（正常态灰蓝低饱和；300 点滚动）
            if (_points.Count > 0)
            {
                var pts = new PointCollection();
                for (int i = 0; i < _points.Count; i++)
                {
                    double x = left + plotW * i / 299.0;
                    double y = top + plotH * (1 - Math.Clamp(_points[i] / yMax, 0, 1.05));
                    pts.Add(new Point(x, y));
                }
                _canvas.Children.Add(new Polyline { Points = pts, Stroke = B("TextSecondary"), StrokeThickness = 2 });
            }
            else
            {
                var empty = new TextBlock { Text = "等待连接", FontSize = 12, Foreground = B("TextSecondary") };
                Canvas.SetLeft(empty, left + plotW / 2 - 24);
                Canvas.SetTop(empty, top + plotH / 2 - 8);
                _canvas.Children.Add(empty);
            }

            // 图例
            var legend = new TextBlock
            {
                Text = "● 水位 CurWaterPos（m）", FontSize = 10, Foreground = B("TextSecondary"),
            };
            Canvas.SetLeft(legend, left + 4);
            Canvas.SetTop(legend, top + 2);
            _canvas.Children.Add(legend);
        }

        private void DrawThreshold(double top, double plotH, double value, string label)
        {
            if (value <= 0 || value >= YMax) return;
            double y = top + plotH * (1 - value / YMax);
            _canvas.Children.Add(new Line
            {
                X1 = 42, Y1 = y, X2 = 42 + ActualWidth - 52, Y2 = y,
                Stroke = B("Alarm"), StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 4 },
            });
            var t = new TextBlock { Text = $"{label} {value:0.#}", FontSize = 9, Foreground = B("Alarm") };
            Canvas.SetLeft(t, ActualWidth - 44);
            Canvas.SetTop(t, y - 8);
            _canvas.Children.Add(t);
        }
    }

    internal static class TrendChartExt
    {
        public static T Also<T>(this T o, Action<T> f) { f(o); return o; }
    }
}
