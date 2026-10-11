using System.Threading;
using System.Windows;
using System.Windows;
using WaterSystem.Wpf;
using Xunit;

namespace WaterSystem.Wpf.Tests
{
    /// <summary>T2.3/T4 验收：主窗壳（Q1=B：Viewbox 矢量等比，窗口自适应工作区；设计稿画布 1492×1000 不变）。
    /// DependencyObject 属性必须在创建它的 STA 线程上读取。</summary>
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
    }
}
