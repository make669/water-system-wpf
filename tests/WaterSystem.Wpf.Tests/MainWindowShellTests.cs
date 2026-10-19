using System.Threading;
using System.Windows;
using WaterSystem.Wpf;
using WaterSystem.Wpf.Controls;
using Xunit;

namespace WaterSystem.Wpf.Tests
{
    /// <summary>T2.3/T4 验收：主窗壳（Q1=B：Viewbox 矢量等比，窗口自适应工作区；设计稿画布 1492×1000 不变）。
    /// DependencyObject 属性必须在创建它的 STA 线程上读取。</summary>
    [Collection("UiDispatcher")]
    public class MainWindowShellTests
    {
        [Theory]
        [InlineData(1536, 825.6, 1232, 826)]    // 本机 1920×1080@125%（WPF DIP 工作区）：k=0.8256
        [InlineData(1920, 1080, 1492, 1000)]  // 大屏：封顶 1.0 不放大
        [InlineData(1366, 728, 1086, 728)]    // 小屏 100%：高约束
        public void SizeFor_自适应工作区(double workW, double workH, double expW, double expH)
        {
            var (w, h) = MainWindow.SizeFor(workW, workH);
            Assert.Equal(expW, w);
            Assert.Equal(expH, h);
        }

        [Fact]
        public void 主窗壳_自适应工作区且设计稿不变()
        {
            (bool ok, string detail)? result = null;
            var t = new Thread(() =>
            {
                try
                {
                    var window = new MainWindow();
                    var (w, h) = MainWindow.SizeFor(SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height);
                    Assert.True(w <= SystemParameters.WorkArea.Width + 1, $"宽溢出: {w}");
                    Assert.True(h <= SystemParameters.WorkArea.Height + 1, $"高溢出: {h}");
                    var vb = Assert.IsType<System.Windows.Controls.Viewbox>(window.Content);
                    var canvas = Assert.IsType<System.Windows.Controls.Canvas>(vb.Child);
                    Assert.Equal(1492, canvas.Width);
                    Assert.Equal(1000, canvas.Height);
                    Assert.Equal(System.Windows.Media.Stretch.Uniform, vb.Stretch);

                    // T4.2 验收：工艺总览卡内包含 11 段管道
                    var pipes = FindLogicalChildren<PipeLine>(canvas).ToList();
                    Assert.Equal(11, pipes.Count);

                    // T4.3 验收：工艺总览卡内包含 LevelTank 水池
                    var tank = Assert.Single(FindLogicalChildren<LevelTank>(canvas));
                    Assert.NotNull(tank);

                    // T4.4 验收：包含 3 组泵卡自绘控件（3 个 PumpGraphic，3 个 URotarySwitchW）
                    var pumps = FindLogicalChildren<PumpGraphic>(canvas).ToList();
                    Assert.Equal(3, pumps.Count);
                    var knobs = FindLogicalChildren<URotarySwitchW>(canvas).ToList();
                    Assert.Equal(3, knobs.Count);

                    // T4.5 验收：包含 5 个 RingGauge 环形仪表（环境卡 4 个 + 用水卡 1 个）
                    var rings = FindLogicalChildren<RingGauge>(canvas).ToList();
                    Assert.Equal(5, rings.Count);

                    // T4.6 验收：包含 7 个 AlarmBadgeChip（顶栏未确认徽标 1 个 + 3 组泵卡运行/故障双徽标 6 个）
                    var badges = FindLogicalChildren<AlarmBadgeChip>(canvas).ToList();
                    Assert.Equal(7, badges.Count);
                    result = (true, "");
                }
                catch (Exception ex)
                {
                    result = (false, ex.GetType().Name + ": " + ex.Message);
                }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            Assert.True(t.Join(10_000), "主窗构造超时");
            Assert.NotNull(result);
            Assert.True(result!.Value.ok, result.Value.detail);
        }

        private static IEnumerable<T> FindLogicalChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj == null) yield break;
            foreach (var raw in LogicalTreeHelper.GetChildren(depObj))
            {
                if (raw is DependencyObject child)
                {
                    if (child is T t) yield return t;
                    foreach (var other in FindLogicalChildren<T>(child))
                        yield return other;
                }
            }
        }
    }
}
