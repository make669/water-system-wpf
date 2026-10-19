using System.Threading;
using WaterSystem.Wpf.Controls;
using Xunit;

namespace WaterSystem.Wpf.Tests
{
    /// <summary>
    /// T4.4 泵图控件单测：默认属性、三态翻转（Stopped/Running/Fault）、B8 动画防重启与数值驱动。
    /// </summary>
    [Collection("UiDispatcher")]
    public class PumpGraphicTests
    {
        [Fact]
        public void PumpGraphic_默认状态与初始值()
        {
            var t = new Thread(() =>
            {
                var pump = new PumpGraphic();
                Assert.Equal(PumpRunState.Stopped, pump.State);
                Assert.False(pump.IsRunning);
                Assert.False(pump.IsFault);
                Assert.True(pump.IsOnline);
                Assert.Equal(1.0, pump.ImpellerSpeed);
                Assert.Equal(1.0, pump.Opacity);
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            Assert.True(t.Join(5000), "STA 测试执行超时");
        }

        [Fact]
        public void PumpGraphic_三态转换与布尔驱动()
        {
            var t = new Thread(() =>
            {
                var pump = new PumpGraphic();

                // 启动水泵
                pump.IsRunning = true;
                Assert.Equal(PumpRunState.Running, pump.State);

                // 发生故障（Fault 优先级高于 Running）
                pump.IsFault = true;
                Assert.Equal(PumpRunState.Fault, pump.State);

                // 故障恢复，仍处于运行
                pump.IsFault = false;
                Assert.Equal(PumpRunState.Running, pump.State);

                // 停机
                pump.IsRunning = false;
                Assert.Equal(PumpRunState.Stopped, pump.State);
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            Assert.True(t.Join(5000), "STA 测试执行超时");
        }

        [Fact]
        public void PumpGraphic_数值驱动与重复赋值幂等()
        {
            var t = new Thread(() =>
            {
                var pump = new PumpGraphic();

                // 模拟 TagBinding 推入 1.0
                pump.RunValue = 1.0;
                Assert.True(pump.IsRunning);
                Assert.Equal(PumpRunState.Running, pump.State);

                // 轮询重复推入 1.0（B8 纪律：幂等不报错）
                pump.RunValue = 1.0;
                Assert.True(pump.IsRunning);
                Assert.Equal(PumpRunState.Running, pump.State);

                // 故障信号推入 1.0
                pump.FaultValue = 1.0;
                Assert.True(pump.IsFault);
                Assert.Equal(PumpRunState.Fault, pump.State);

                // 离线
                pump.IsOnline = false;
                Assert.Equal(0.5, pump.Opacity);
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            Assert.True(t.Join(5000), "STA 测试执行超时");
        }
    }
}
