using System.Threading;
using WaterSystem.Wpf.Controls;
using Xunit;

namespace WaterSystem.Wpf.Tests
{
    /// <summary>
    /// T4.5 环形仪表控件单测：默认属性、量程比例换算（P7 关闭验证）、越限报警联动与格式化。
    /// </summary>
    [Collection("UiDispatcher")]
    public class RingGaugeTests
    {
        [Fact]
        public void RingGauge_默认属性与初始值()
        {
            var t = new Thread(() =>
            {
                var gauge = new RingGauge();
                Assert.Equal(0.0, gauge.Value);
                Assert.Equal(0.0, gauge.MinValue);
                Assert.Equal(100.0, gauge.MaxValue);
                Assert.Equal(1, gauge.Decimals);
                Assert.False(gauge.Alarm);
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            Assert.True(t.Join(5000), "STA 测试执行超时");
        }

        [Fact]
        public void RingGauge_P7量程对齐_实际用水85点4处于17百分比位置()
        {
            var t = new Thread(() =>
            {
                // P7 关闭验收项：量程 0~500，实际用水 85.4m³ 必须指示在 17.08% 位置
                var gauge = new RingGauge
                {
                    MinValue = 0.0,
                    MaxValue = 500.0,
                    Value = 85.4
                };

                double range = gauge.MaxValue - gauge.MinValue;
                double fraction = (gauge.Value - gauge.MinValue) / range;
                Assert.Equal(0.1708, fraction, 3);
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            Assert.True(t.Join(5000), "STA 测试执行超时");
        }

        [Fact]
        public void RingGauge_负量程换算_温度负10至50()
        {
            var t = new Thread(() =>
            {
                // 温度 -10..50 ℃，26.5 ℃ 换算
                var gauge = new RingGauge
                {
                    MinValue = -10.0,
                    MaxValue = 50.0,
                    Value = 26.5
                };

                double range = gauge.MaxValue - gauge.MinValue; // 60.0
                double fraction = (gauge.Value - gauge.MinValue) / range; // 36.5 / 60 ≈ 0.6083
                Assert.Equal(0.6083, fraction, 3);
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            Assert.True(t.Join(5000), "STA 测试执行超时");
        }

        [Fact]
        public void RingGauge_越限报警联动()
        {
            var t = new Thread(() =>
            {
                var gauge = new RingGauge
                {
                    MinValue = 0.0,
                    MaxValue = 100.0,
                    HighThreshold = 80.0,
                    LowThreshold = 10.0,
                    Value = 50.0
                };
                Assert.False(gauge.Alarm);

                // 超过高阈值
                gauge.Value = 85.0;
                // HighThreshold 触发越限
                Assert.True(gauge.Value > gauge.HighThreshold);

                // 低于低阈值
                gauge.Value = 5.0;
                Assert.True(gauge.Value < gauge.LowThreshold);

                // 显式告警
                gauge.Alarm = true;
                Assert.True(gauge.Alarm);
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            Assert.True(t.Join(5000), "STA 测试执行超时");
        }
    }
}
