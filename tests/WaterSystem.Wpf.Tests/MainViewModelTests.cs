using System.IO;
using Intelligent_water_storage_system.Model;
using Intelligent_water_storage_system.Services;
using Intelligent_water_storage_system.Utils;
using WaterSystem.Wpf.Infrastructure;
using WaterSystem.Wpf.ViewModels;
using Xunit;

namespace WaterSystem.Wpf.Tests
{
    /// <summary>T3.1 验收：数据应用→TagValues/徽章、断线复位（保留读数）、看门狗纯函数。
    /// 与 UiDispatcherTests 共用静态 Runner（同 Collection 串行）。</summary>
    [Collection("UiDispatcher")]
    public class MainViewModelTests : IDisposable
    {
        private readonly LogService _log = new();
        private readonly ModbusClient _client = new("127.0.0.1", 1502);
        private readonly DataPollingService _polling;
        private readonly List<TagAddress> _tags;
        private readonly Action<Action> _defaultRunner;

        public MainViewModelTests()
        {
            UiDispatcher.Runner = action => action();   // 测试内联封送
            _polling = new DataPollingService(_client, 1000);
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && d != null; i++, d = d.Parent)
            {
                var p = Path.Combine(d.FullName, "configFiles", "TagTable.csv");
                if (File.Exists(p)) { _tags = TagTableLoader.Load(p, out _, out _); return; }
            }
            throw new InvalidOperationException("未找到 configFiles/TagTable.csv");
        }

        public void Dispose() => UiDispatcher.Runner = null!;

        private MainViewModel NewVm(bool startWatchdog = false, Func<DateTime>? clock = null)
            => new(_log, _client, _polling,
                   new ControlService(_client, _tags),
                   new AlarmService(new AlarmConfig()),
                   _tags, startWatchdog, clock);

        private static Dictionary<string, DataPoint> Data(params (string Tag, double V)[] items)
            => items.ToDictionary(i => i.Tag, i => new DataPoint { Name = i.Tag, Value = i.V });

        [Fact]
        public void ApplyData_TagValues更新()
        {
            var vm = NewVm();
            vm.ApplyData(Data(("CurWaterPos", 2.5), ("MainOutPressure", 0.35)));
            Assert.Equal(2.5, vm.TagValues["CurWaterPos"], 9);
            Assert.Equal(0.35, vm.TagValues["MainOutPressure"], 9);
        }

        [Fact]
        public void 徽章_随数据三态变化()
        {
            var vm = NewVm();
            vm.ApplyData(Data(("Pump2_Run", 1), ("Pump3_Fault", 1), ("MainValve", 1), ("Valve2", 1)));
            Assert.Equal("● 运行", vm.Badges[1].Word);
            Assert.Equal("Ok", vm.Badges[1].ToneKey);
            Assert.Equal("● 故障", vm.Badges[2].Word);
            Assert.Equal("Alarm", vm.Badges[2].ToneKey);
            Assert.Equal("● 停止", vm.Badges[0].Word);
            Assert.Equal("● 开", vm.Badges[3].Word);
            Assert.Equal("● 开", vm.Badges[5].Word);   // Valve2=1
            Assert.Equal("○ 关", vm.Badges[6].Word);   // Valve3 未给 → 关
        }

        [Fact]
        public void 断线_徽章全灭但数值保留最后读数()
        {
            var vm = NewVm();
            vm.ApplyData(Data(("CurWaterPos", 2.5), ("Pump2_Run", 1)));
            vm.OnConnectionChanged(false);
            Assert.Equal(CommState.Disconnected, vm.ConnState);
            Assert.All(vm.Badges, b => Assert.Equal("Off", b.ToneKey));
            // 源语义：数值保留最后真实读数（FrmMain.cs:782-784），不得清零显示过期前的数据
            Assert.Equal(2.5, vm.TagValues["CurWaterPos"], 9);
        }

        [Fact]
        public void 质量中断_转Stale_恢复后回Connected()
        {
            var vm = NewVm();
            vm.ApplyData(Data(("CurWaterPos", 2.5)));
            vm.OnQualityChanged(false);
            Assert.Equal(CommState.Stale, vm.ConnState);
            Assert.Equal("通信中断", vm.ConnStateText);
            vm.OnQualityChanged(true);
            Assert.Equal(CommState.Connected, vm.ConnState);
            Assert.Equal("已连接", vm.ConnStateText);
        }

        [Fact]
        public void ResetDisplayValues_空态归零()
        {
            var vm = NewVm();
            vm.ApplyData(Data(("CurWaterPos", 2.5)));
            vm.ResetDisplayValues();
            Assert.Equal(0, vm.TagValues["CurWaterPos"], 9);
        }

        [Theory]
        [InlineData(true, true, true, 0, false)]     // 数据新鲜 → 不过期
        [InlineData(true, true, true, 4000, true)]   // 超过 3×周期 → 过期
        [InlineData(false, true, true, 4000, false)] // 已过过期态 → 不重复报
        [InlineData(true, false, true, 4000, false)] // 首包未同步 → 守卫兜底不误报
        [InlineData(true, true, false, 4000, false)] // 未连接 → 不归看门狗管
        public void ShouldMarkStale_过期判定(bool qualityOk, bool initialSync, bool connected,
            double elapsedMs, bool expected)
        {
            var now = DateTime.Now;
            var last = now.AddMilliseconds(-elapsedMs);
            Assert.Equal(expected, MainViewModel.ShouldMarkStale(qualityOk, initialSync, connected, last, now, 1000));
        }

        [Fact]
        public void 连接_IP格式无效_拒绝并提示()
        {
            var vm = NewVm();
            vm.IpText = "not_an_ip";
            vm.ConnectCommand.Execute(null);
            Assert.Equal("IP 格式无效", vm.ConnStateText);
        }

        [Fact]
        public void PipeLines_五态验证()
        {
            var vm = NewVm();
            // 态 1：全停（初始断开）
            Assert.False(vm.IsPipeMainActive);
            Assert.False(vm.IsPipePump1Active);
            Assert.False(vm.IsPipePump2Active);
            Assert.False(vm.IsPipePump3Active);
            Assert.False(vm.IsPipeCollectorActive);

            // 建立连接
            vm.OnConnectionChanged(true);
            Assert.Equal(CommState.Connected, vm.ConnState);

            // 态 2：泵转无主进水（MainValve=0, Pump1_Run=1, Valve1=1）——B8 因果律：无主进水，管路不流动
            vm.ApplyData(Data(("MainValve", 0), ("Pump1_Run", 1), ("Valve1", 1)));
            Assert.False(vm.IsPipeMainActive);
            Assert.False(vm.IsPipePump1Active);
            Assert.False(vm.IsPipePump2Active);
            Assert.False(vm.IsPipePump3Active);
            Assert.False(vm.IsPipeCollectorActive);

            // 态 3：开主阀但泵停（MainValve=1, Pump1~3=0, Valve1~3=1）——进水横管流动，泵支路与汇管不流动
            vm.ApplyData(Data(("MainValve", 1), ("Pump1_Run", 0), ("Pump2_Run", 0), ("Pump3_Run", 0),
                              ("Valve1", 1), ("Valve2", 1), ("Valve3", 1)));
            Assert.True(vm.IsPipeMainActive);
            Assert.False(vm.IsPipePump1Active);
            Assert.False(vm.IsPipePump2Active);
            Assert.False(vm.IsPipePump3Active);
            Assert.False(vm.IsPipeCollectorActive);

            // 态 3b：开主阀开泵但支阀关闭（MainValve=1, Pump1_Run=1, Valve1=0）——支阀切断，支路与汇管不流动
            vm.ApplyData(Data(("MainValve", 1), ("Pump1_Run", 1), ("Valve1", 0)));
            Assert.True(vm.IsPipeMainActive);
            Assert.False(vm.IsPipePump1Active);
            Assert.False(vm.IsPipeCollectorActive);

            // 态 4：开主阀+开支阀+开泵（MainValve=1, Pump1_Run=1, Valve1=1, Pump2=0）——主管、1#支路及汇管输出流动，2#3#支路不流动
            vm.ApplyData(Data(("MainValve", 1), ("Pump1_Run", 1), ("Valve1", 1), ("Pump2_Run", 0), ("Pump3_Run", 0)));
            Assert.True(vm.IsPipeMainActive);
            Assert.True(vm.IsPipePump1Active);
            Assert.False(vm.IsPipePump2Active);
            Assert.False(vm.IsPipePump3Active);
            Assert.True(vm.IsPipeCollectorActive);

            // 态 5：断线全停全灭（ResetProcessGraphics）——即使断线前开阀开泵，断线后管道全部停止流动
            vm.OnConnectionChanged(false);
            Assert.False(vm.IsPipeMainActive);
            Assert.False(vm.IsPipePump1Active);
            Assert.False(vm.IsPipePump2Active);
            Assert.False(vm.IsPipePump3Active);
            Assert.False(vm.IsPipeCollectorActive);
        }
    }
}
