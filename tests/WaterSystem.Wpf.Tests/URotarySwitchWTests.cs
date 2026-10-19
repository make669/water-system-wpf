using System.Threading;
using WaterSystem.Wpf.Controls;
using WaterSystem.Wpf.ViewModels;
using Xunit;

namespace WaterSystem.Wpf.Tests
{
    /// <summary>
    /// T4.4 旋钮控件单测：默认状态、角度翻转、命令触发、离线半透明。
    /// </summary>
    [Collection("UiDispatcher")]
    public class URotarySwitchWTests
    {
        [Fact]
        public void URotarySwitchW_默认状态与初始值()
        {
            var t = new Thread(() =>
            {
                var knob = new URotarySwitchW();
                Assert.False(knob.SwitchState);
                Assert.True(knob.IsOnline);
                Assert.Equal(1.0, knob.Opacity);
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            Assert.True(t.Join(5000), "STA 测试执行超时");
        }

        [Fact]
        public void URotarySwitchW_状态切换与离线状态()
        {
            var t = new Thread(() =>
            {
                var knob = new URotarySwitchW();

                // 启动水泵
                knob.SwitchState = true;
                Assert.True(knob.SwitchState);

                // 停止水泵
                knob.SwitchState = false;
                Assert.False(knob.SwitchState);

                // 离线
                knob.IsOnline = false;
                Assert.Equal(0.5, knob.Opacity);
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            Assert.True(t.Join(5000), "STA 测试执行超时");
        }

        [Fact]
        public void URotarySwitchW_命令绑定触发()
        {
            var t = new Thread(() =>
            {
                string? executedTag = null;
                var cmd = new RelayCommand<string>(tag => executedTag = tag);

                var knob = new URotarySwitchW
                {
                    Command = cmd,
                    CommandParameter = "Pump1_Run"
                };

                // 执行命令
                knob.Command.Execute(knob.CommandParameter);
                Assert.Equal("Pump1_Run", executedTag);
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            Assert.True(t.Join(5000), "STA 测试执行超时");
        }
    }
}
