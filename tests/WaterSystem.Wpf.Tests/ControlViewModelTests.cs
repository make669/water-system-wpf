using System.IO;
using Intelligent_water_storage_system.Model;
using Intelligent_water_storage_system.Services;
using Intelligent_water_storage_system.Utils;
using WaterSystem.Wpf.Infrastructure;
using WaterSystem.Wpf.ViewModels;
using Xunit;

namespace WaterSystem.Wpf.Tests
{
    /// <summary>M5 验收（TC-V1~V8，WPF实施细则 §3.5）：控制决策接线 + 回写防护三层。</summary>
    [Collection("UiDispatcher")]
    public class ControlViewModelTests : IDisposable
    {
        private readonly LogService _log = new();
        private readonly ModbusClient _client = new("127.0.0.1", 1502);
        private readonly DataPollingService _polling;
        private readonly List<TagAddress> _tags;
        private readonly List<(string Tag, bool Value)> _writes = new();
        private readonly List<string> _notifies = new();
        private readonly Queue<bool> _confirmQueue = new();
        private bool _connected = true;
        private bool _writeResult = true;

        public ControlViewModelTests()
        {
            UiDispatcher.Runner = action => action();
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

        private MainViewModel NewMain() => new(_log, _client, _polling,
            new ControlService(_client, _tags), new AlarmService(new AlarmConfig()),
            _tags, startWatchdog: false);

        private ControlViewModel NewControl(MainViewModel main)
        {
            var control = new ControlViewModel(_log,
                new ControlService(_client, _tags), _client, _tags, main,
                confirm: _ => _confirmQueue.Count > 0 && _confirmQueue.Dequeue(),
                notify: m => _notifies.Add(m),
                isConnected: () => _connected)
            { WriteOverride = (tag, v) => { _writes.Add((tag, v)); return Task.FromResult(_writeResult); } };
            return control;
        }

        private static Dictionary<string, DataPoint> Data(params (string Tag, double V)[] items)
            => items.ToDictionary(i => i.Tag, i => new DataPoint { Name = i.Tag, Value = i.V });

        [Fact]
        public async Task TC_V2_确认后写线圈并乐观更新()
        {
            var main = NewMain();
            var control = NewControl(main);
            _confirmQueue.Enqueue(true);
            await control.ToggleTagAsync("Pump1_Run");
            Assert.Equal(1, control.WriteCalls);
            Assert.Equal(("Pump1_Run", true), _writes[0]);
            Assert.True(control.GetSwitch("Pump1_Run"));
            Assert.Contains(_log.Entries, e => e.Message.Contains("控制指令已下发 [Pump1_Run] → ON"));
        }

        [Fact]
        public async Task TC_V3_取消不写且状态不变()
        {
            var main = NewMain();
            var control = NewControl(main);
            _confirmQueue.Enqueue(false);
            await control.ToggleTagAsync("Pump1_Run");
            Assert.Equal(0, control.WriteCalls);
            Assert.False(control.GetSwitch("Pump1_Run"));
        }

        [Fact]
        public async Task TC_V4_写失败回滚()
        {
            var main = NewMain();
            var control = NewControl(main);
            _writeResult = false;
            _confirmQueue.Enqueue(true);
            await control.ToggleTagAsync("Pump1_Run");
            Assert.Equal(1, control.WriteCalls);
            Assert.False(control.GetSwitch("Pump1_Run"));   // 回滚到旧值
            Assert.Contains(_log.Entries, e => e.Message.Contains("控制失败已回滚 [Pump1_Run]"));
        }

        [Fact]
        public async Task TC_V5_未连接拒绝()
        {
            var main = NewMain();
            var control = NewControl(main);
            _connected = false;
            await control.ToggleTagAsync("Pump1_Run");
            Assert.Equal(0, control.WriteCalls);
            Assert.Contains(_notifies, m => m.Contains("PLC 未连接，无法执行控制指令。"));
        }

        [Fact]
        public async Task TC_V6_只读点位拒绝()
        {
            var main = NewMain();
            var control = NewControl(main);
            await control.ToggleTagAsync("Pump1_Fault");   // Access=R
            Assert.Equal(0, control.WriteCalls);
            Assert.Contains(_notifies, m => m.Contains("只读(R)，禁止写入"));
        }

        [Fact]
        public async Task TC_V1_V7_首包同步与轮询百轮零写()
        {
            var main = NewMain();
            var control = NewControl(main);
            // 100 轮轮询数据（泵状态每轮翻转，首轮=1）——数据路径不得产生任何写指令（回写风暴防护）
            for (int r = 1; r <= 100; r++)
                main.ApplyData(Data(("Pump2_Run", r % 2)));
            Assert.Equal(0, control.WriteCalls);            // TC-V1
            Assert.True(control.GetSwitch("Pump2_Run"));    // TC-V7：首包同步对齐 PLC（首轮=1）
        }

        [Fact]
        public async Task TC_V8_防重入_写入进行中忽略新手势()
        {
            var main = NewMain();
            var control = NewControl(main);
            _confirmQueue.Enqueue(true);
            var tcs = new TaskCompletionSource<bool>();
            var pending = 0;
            control.WriteOverride = (tag, v) =>
            {
                pending++;
                return tcs.Task;
            };
            var first = control.ToggleTagAsync("Pump1_Run");
            await control.ToggleTagAsync("Pump1_Run");      // busy 中 → 立即返回
            Assert.Equal(1, pending);
            tcs.SetResult(true);
            await first;
            Assert.Equal(1, pending);
            Assert.True(control.GetSwitch("Pump1_Run"));
        }
    }
}
