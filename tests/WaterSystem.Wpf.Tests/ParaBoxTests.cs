using System.Threading;
using System.Windows;
using WaterSystem.Wpf.Controls;
using Xunit;

namespace WaterSystem.Wpf.Tests
{
    /// <summary>T3.4 验收：ParaBox 按 Decimals 走 ValueFormatter 格式化（修 P1），越限转 Alarm。</summary>
    [Collection("UiDispatcher")]
    public class ParaBoxTests
    {
        [Fact]
        public void ParaBox_按Decimals格式化并响应变化()
        {
            ParaBox? box = null;
            Exception? error = null;
            var t = new Thread(() =>
            {
                try
                {
                    box = new ParaBox { Decimals = 1 };
                    box.Value = 2.546;
                    Assert.Equal("2.5", box.DisplayText);        // 修 P1：不再 3.59625473…
                    box.Value = 3.59625473;
                    Assert.Equal("3.6", box.DisplayText);
                    box.Alarm = true;
                    Assert.True(box.Alarm);
                }
                catch (Exception ex) { error = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            Assert.True(t.Join(10_000), "ParaBox 构造超时");
            Assert.Null(error);
        }

        [Fact]
        public void ParaBox_超阈值报警变色()
        {
            ParaBox? box = null;
            Exception? error = null;
            var t = new Thread(() =>
            {
                try
                {
                    box = new ParaBox { Decimals = 2, HighThreshold = 10.0, LowThreshold = 1.0 };
                    box.Value = 5.0;
                    Assert.Equal("5.00", box.DisplayText);

                    // 超高限 12.0 > 10.0
                    box.Value = 12.0;
                    Assert.Equal("12.00", box.DisplayText);

                    // 低于低限 0.5 < 1.0
                    box.Value = 0.5;
                    Assert.Equal("0.50", box.DisplayText);
                }
                catch (Exception ex) { error = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            Assert.True(t.Join(10_000), "ParaBox 构造超时");
            Assert.Null(error);
        }
    }
}
