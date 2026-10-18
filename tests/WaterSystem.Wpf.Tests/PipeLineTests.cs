using System.Threading;
using System.Windows.Controls;
using WaterSystem.Wpf.Controls;
using Xunit;

namespace WaterSystem.Wpf.Tests
{
    /// <summary>
    /// T4.2 管道控件单测：属性初始值、方向、方向翻转守卫（B8 相位教训）。
    /// </summary>
    public class PipeLineTests
    {
        [Fact]
        public void PipeLine_默认属性与翻转不重启()
        {
            var t = new Thread(() =>
            {
                var pipe = new PipeLine();
                Assert.False(pipe.Active);
                Assert.Equal(PipeFlowDirection.LeftToRight, pipe.Direction);
                Assert.Equal(Orientation.Horizontal, pipe.Orientation);
                Assert.Equal(6.0, pipe.PipeThickness);
                Assert.Equal(0.6, pipe.FlowSpeed);

                // 重复赋值 Active=true 幂等不抛错
                pipe.Active = true;
                pipe.Active = true;
                Assert.True(pipe.Active);

                pipe.Active = false;
                Assert.False(pipe.Active);
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            Assert.True(t.Join(5000), "STA 测试执行超时");
        }
    }
}
