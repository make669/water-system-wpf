using System.Threading;
using WaterSystem.Wpf.Controls;
using Xunit;

namespace WaterSystem.Wpf.Tests
{
    /// <summary>
    /// T4.3 水池液位控件单测：属性初始值、Value与Level双向联动、越限报警判定、波浪动画状态。
    /// </summary>
    public class LevelTankTests
    {
        [Fact]
        public void LevelTank_默认属性与量程()
        {
            var t = new Thread(() =>
            {
                var tank = new LevelTank();
                Assert.Equal(0.0, tank.MinValue);
                Assert.Equal(6.0, tank.MaxValue);
                Assert.Equal(1.0, tank.LowAt);
                Assert.Equal(6.0, tank.HighAt);
                Assert.Equal("m", tank.Unit);
                Assert.True(tank.ShowRuler);
                Assert.Equal(2.0, tank.WaveSpeed);
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            Assert.True(t.Join(5000), "STA 测试执行超时");
        }

        [Fact]
        public void LevelTank_Value与Level双向同步与截断()
        {
            var t = new Thread(() =>
            {
                var tank = new LevelTank();

                // 赋值 Value 自动折算 Level
                tank.Value = 3.0; // 3.0 / 6.0 = 0.5
                Assert.Equal(0.5, tank.Level, 3);
                Assert.False(tank.IsAlarm);

                // 赋值 Level 自动折算 Value
                tank.Level = 0.75; // 0.75 * 6.0 = 4.5
                Assert.Equal(4.5, tank.Value, 3);
                Assert.False(tank.IsAlarm);

                // 越界值截断
                tank.Level = 1.5;
                Assert.Equal(1.0, tank.Level);

                tank.Level = -0.5;
                Assert.Equal(0.0, tank.Level);
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            Assert.True(t.Join(5000), "STA 测试执行超时");
        }

        [Fact]
        public void LevelTank_越限报警联动三态()
        {
            var t = new Thread(() =>
            {
                var tank = new LevelTank
                {
                    MinValue = 0.0,
                    MaxValue = 6.0,
                    LowAt = 1.0,
                    HighAt = 6.0
                };

                // 正常区间
                tank.Value = 3.5;
                Assert.False(tank.IsAlarm);

                // 低水位越限 (< 1.0)
                tank.Value = 0.8;
                Assert.True(tank.IsAlarm);

                // 恢复正常
                tank.Value = 2.0;
                Assert.False(tank.IsAlarm);

                // 高水位越限 (> 6.0)
                tank.Value = 6.3;
                Assert.True(tank.IsAlarm);

                // 恢复正常
                tank.Value = 5.9;
                Assert.False(tank.IsAlarm);
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            Assert.True(t.Join(5000), "STA 测试执行超时");
        }

        [Fact]
        public void LevelTank_动态阈值切换()
        {
            var t = new Thread(() =>
            {
                var tank = new LevelTank { Value = 1.5, LowAt = 1.0 };
                Assert.False(tank.IsAlarm);

                // 调高低限阈值至 2.0m，1.5m 立即触发报警
                tank.LowAt = 2.0;
                Assert.True(tank.IsAlarm);

                // 调回低限至 0.5m，恢复正常
                tank.LowAt = 0.5;
                Assert.False(tank.IsAlarm);
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            Assert.True(t.Join(5000), "STA 测试执行超时");
        }
    }
}
