using System.Threading;
using System.Windows;
using WaterSystem.Wpf;
using Xunit;

namespace WaterSystem.Wpf.Tests
{
    /// <summary>T2.3 验收：主窗壳形态（1492×1000 固定、无边框、不缩放，Q1=A 裁决）。</summary>
    public class MainWindowShellTests
    {
        [Fact]
        public void 主窗壳_形态符合定稿()
        {
            MainWindow? window = null;
            Exception? error = null;
            var t = new Thread(() =>
            {
                try { window = new MainWindow(); }
                catch (Exception ex) { error = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            Assert.True(t.Join(10_000), "主窗构造超时");
            Assert.Null(error);
            Assert.NotNull(window);
            Assert.Equal(1492, window!.Width);
            Assert.Equal(1000, window.Height);
            Assert.Equal(WindowStyle.None, window.WindowStyle);
            Assert.Equal(ResizeMode.NoResize, window.ResizeMode);
            Assert.Equal(WindowStartupLocation.CenterScreen, window.WindowStartupLocation);
        }
    }
}
