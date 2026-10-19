using System.IO;
using Intelligent_water_storage_system.Model;
using Intelligent_water_storage_system.Services;
using Intelligent_water_storage_system.Utils;
using WaterSystem.Wpf.Controls;
using WaterSystem.Wpf.Infrastructure;
using WaterSystem.Wpf.ViewModels;
using Xunit;

namespace WaterSystem.Wpf.Tests
{
    /// <summary>T7.1 验收：300 点滚动缓冲 + 图表渲染点数（修 P10：阈值/单位/图例数据源齐备）。</summary>
    [Collection("UiDispatcher")]
    public class TrendWindowTests : IDisposable
    {
        private readonly LogService _log = new();
        private readonly ModbusClient _client = new("127.0.0.1", 1502);
        private readonly DataPollingService _polling = new(new ModbusClient("127.0.0.1", 1502), 1000);
        private readonly List<TagAddress> _tags;

        public TrendWindowTests()
        {
            UiDispatcher.Runner = action => action();
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && d != null; i++, d = d.Parent)
            {
                var p = Path.Combine(d.FullName, "configFiles", "TagTable.csv");
                if (File.Exists(p)) { _tags = TagTableLoader.Load(p, out _, out _); return; }
            }
            throw new InvalidOperationException("未找到 configFiles/TagTable.csv");
        }

        public void Dispose() => UiDispatcher.Runner = null!;

        private MainViewModel NewMain() => new(_log, _client, _polling,
            new ControlService(_client, _tags), new AlarmService(new AlarmConfig()),
            _tags, startWatchdog: false, config: new AlarmConfig());

        private static Dictionary<string, DataPoint> Data(double v)
            => new() { ["CurWaterPos"] = new DataPoint { Name = "CurWaterPos", Value = v } };

        [Fact]
        public void 趋势缓冲_300点滚动()
        {
            var vm = NewMain();
            for (int i = 1; i <= 305; i++)
                vm.ApplyData(Data(i));
            Assert.Equal(300, vm.TrendSnapshot.Length);   // 滚动窗口
            Assert.Equal(6, vm.TrendSnapshot[0]);          // 前 5 点被移除
            Assert.Equal(305, vm.TrendSnapshot[^1]);
        }

        [Fact]
        public void 趋势阈值_来自AlarmConfig()
        {
            var vm = NewMain();
            Assert.Equal(1.0, vm.ThresholdLow);
            Assert.Equal(6.0, vm.ThresholdHigh);
            Assert.Equal(6.5, vm.YMax);                    // 6 × 1.0833 余量
        }

        [Fact]
        public void 图表_渲染点数随快照()
        {
            WaterTrendChart? chart = null;
            Exception? error = null;
            var t = new Thread(() =>
            {
                try
                {
                    chart = new WaterTrendChart { YMax = 6.5, ThresholdLow = 1, ThresholdHigh = 6 };
                    Assert.Equal(0, chart.RenderedPointCount);          // 空态
                    chart.Snapshot = Enumerable.Range(0, 305).Select(i => (double)i).ToArray();
                    Assert.Equal(300, chart.RenderedPointCount);        // 截断到 300
                    chart.Snapshot = null;
                    Assert.Equal(0, chart.RenderedPointCount);          // 空态复位
                }
                catch (Exception ex) { error = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            Assert.True(t.Join(10_000), "图表构造超时");
            Assert.Null(error);
        }

        [Fact]
        public void T4_7_趋势卡阈值线单位图例_P10关闭()
        {
            Exception? error = null;
            var t = new Thread(() =>
            {
                try
                {
                    var chart = new WaterTrendChart
                    {
                        Width = 724,
                        Height = 150,
                        YMax = 6.5,
                        ThresholdLow = 1.0,
                        ThresholdHigh = 6.0,
                        Snapshot = new double[] { 2.5, 3.0, 3.5 },
                    };
                    chart.Measure(new System.Windows.Size(724, 150));
                    chart.Arrange(new System.Windows.Rect(0, 0, 724, 150));

                    var canvas = Assert.IsType<System.Windows.Controls.Canvas>(chart.Content);
                    var texts = canvas.Children.OfType<System.Windows.Controls.TextBlock>().Select(tb => tb.Text).ToList();

                    // 断言 Y 轴单位 "m"（P10 关闭）
                    Assert.Contains("m", texts);

                    // 断言图例包含 "● 水位"
                    Assert.Contains(texts, t => t.Contains("水位"));

                    // 断言阈值标注包含 "上限 6m" 与 "下限 1m"
                    Assert.Contains(texts, t => t.Contains("上限") && t.Contains("6"));
                    Assert.Contains(texts, t => t.Contains("下限") && t.Contains("1"));

                    // 断言红虚线阈值线数量 >= 2
                    var lines = canvas.Children.OfType<System.Windows.Shapes.Line>()
                        .Where(l => l.StrokeDashArray != null && l.StrokeDashArray.Count > 0)
                        .ToList();
                    Assert.True(lines.Count >= 2);
                }
                catch (Exception ex) { error = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            Assert.True(t.Join(10_000), "趋势图测试超时");
            Assert.Null(error);
        }
    }
}
