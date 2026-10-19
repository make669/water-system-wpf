using System.IO;
using Intelligent_water_storage_system.Model;
using Intelligent_water_storage_system.Services;
using Intelligent_water_storage_system.Utils;
using WaterSystem.Wpf.Infrastructure;
using WaterSystem.Wpf.ViewModels;
using Xunit;

namespace WaterSystem.Wpf.Tests
{
    /// <summary>T6.2 验收：报警流（Evaluate→增量→AlarmCenter）+ 面板四态/确认。</summary>
    [Collection("UiDispatcher")]
    public class AlarmFlowTests : IDisposable
    {
        private readonly LogService _log = new();
        private readonly ModbusClient _client = new("127.0.0.1", 1502);
        private readonly DataPollingService _polling = new(new ModbusClient("127.0.0.1", 1502), 1000);
        private readonly List<TagAddress> _tags;
        private readonly string _dbPath;

        public AlarmFlowTests()
        {
            UiDispatcher.Runner = action => action();
            _dbPath = Path.Combine(Path.GetTempPath(), $"alarmflow_{Guid.NewGuid():N}.db");
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && d != null; i++, d = d.Parent)
            {
                var p = Path.Combine(d.FullName, "configFiles", "TagTable.csv");
                if (File.Exists(p)) { _tags = TagTableLoader.Load(p, out _, out _); return; }
            }
            throw new InvalidOperationException("未找到 configFiles/TagTable.csv");
        }

        public void Dispose()
        {
            UiDispatcher.Runner = null!;
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
        }

        private MainViewModel NewMain(AlarmCenter center) => new(_log, _client, _polling,
            new ControlService(_client, _tags), new AlarmService(new AlarmConfig()),
            _tags, startWatchdog: false, alarmCenter: center);

        private static Dictionary<string, DataPoint> Data(params (string Tag, double V)[] items)
            => items.ToDictionary(i => i.Tag, i => new DataPoint { Name = i.Tag, Value = i.V });

        [Fact]
        public void 报警流_压力越限_Raise入库_恢复Clear()
        {
            var center = new AlarmCenter(_dbPath);
            var vm = NewMain(center);
            vm.ApplyData(Data(("MainOutPressure", 12.0)));   // > 10 Mpa 上限
            Assert.Contains(_log.Entries, e => e.Level == "WARN" && e.Message.Contains("主管压力过高"));
            Assert.Equal(1, center.OpenSnapshot().Count);
            Assert.Equal("主管压力过高: 12.0Mpa", center.OpenSnapshot()[0].Message.Contains("12.0") ? center.OpenSnapshot()[0].Message : center.OpenSnapshot()[0].Message);

            vm.ApplyData(Data(("MainOutPressure", 0.35)));   // 恢复（未确认 → 已恢复·未确认，仍占未关闭）
            Assert.Contains(_log.Entries, e => e.Level == "WARN" && e.Message.Contains("主管压力恢复正常"));
            Assert.Equal(1, center.OpenCount);               // ISA-18.2：恢复未确认仍需确认（源 TestRunner 同口径）
            Assert.False(center.OpenSnapshot()[0].IsActive);
            Assert.True(center.OpenSnapshot()[0].NeedsAck);
        }

        [Fact]
        public void 报警流_同名活跃去重_恢复后才可再触发()
        {
            var center = new AlarmCenter(_dbPath);
            var vm = NewMain(center);
            vm.ApplyData(Data(("MainOutPressure", 12.0)));
            vm.ApplyData(Data(("MainOutPressure", 12.5)));   // 仍越限 → 去重
            Assert.Equal(1, center.OpenCount);
        }

        [Fact]
        public void 面板_四态与确认()
        {
            var center = new AlarmCenter(_dbPath);
            var vm = NewMain(center);
            var panel = vm.AlarmPanel!;
            vm.ApplyData(Data(("MainOutPressure", 12.0)));   // 报警中
            Assert.Equal(1, panel.Rows.Count);
            Assert.Equal("报警中", panel.Rows[0].StateText);
            Assert.Equal("Alarm", panel.Rows[0].ToneKey);
            Assert.Equal(1, panel.UnackCount);

            center.Acknowledge(panel.Rows[0].Id, "测试员");  // 报警中·已确认
            panel.Refresh();
            Assert.Equal("Ack", panel.Rows[0].ToneKey);
            Assert.Equal(0, panel.UnackCount);

            vm.ApplyData(Data(("MainOutPressure", 0.35)));   // 恢复（已确认→关闭）
            panel.Refresh();
            Assert.Equal(0, panel.Rows.Count);
        }

        [Fact]
        public void 面板_恢复未确认_确认后关闭()
        {
            var center = new AlarmCenter(_dbPath);
            var vm = NewMain(center);
            var panel = vm.AlarmPanel!;
            vm.ApplyData(Data(("MainOutPressure", 12.0)));
            vm.ApplyData(Data(("MainOutPressure", 0.35)));   // 未确认恢复
            panel.Refresh();
            Assert.Equal("已恢复·未确认", panel.Rows[0].StateText);
            Assert.Equal("Recover", panel.Rows[0].ToneKey);

            panel.Selected = panel.Rows[0];
            panel.AckSelectedCommand.Execute(null);
            panel.Refresh();
            Assert.Equal(0, panel.Rows.Count);
        }

        [Fact]
        public void T4_6_Surge三处同时可见_顶栏报警卡过程图描红_P4关闭()
        {
            var center = new AlarmCenter(_dbPath);
            var vm = NewMain(center);
            var panel = vm.AlarmPanel!;

            // 1. 初始正常态：无报警，顶栏与报警卡为正常态，无过程图描红
            Assert.Equal(0, vm.UnackCount);
            Assert.Equal("● 正常", vm.UnackBadgeText);
            Assert.Equal(Controls.AlarmBadgeTone.Off, vm.UnackBadgeTone);
            Assert.False(vm.IsHighPressure);
            Assert.Equal(0, panel.Rows.Count);

            // 2. 模拟 surge 浪涌（压力 12.0 MPa > 10.0 MPa 阈值）
            vm.ApplyData(Data(("MainOutPressure", 12.0)));

            // 验收三处同时可见（P4 关闭）：
            // ① 顶栏徽标可见：未确认数=1，AlarmBadgeChip 显示 "⚠ 未确认 1"，Tone 为 Alarm 红
            Assert.Equal(1, vm.UnackCount);
            Assert.Equal("⚠ 未确认 1", vm.UnackBadgeText);
            Assert.Equal(Controls.AlarmBadgeTone.Alarm, vm.UnackBadgeTone);

            // ② 实时报警卡可见：卡头未确认=1，行列表包含 "主管压力过高"，状态为 "报警中"（ToneKey=Alarm）
            Assert.Equal(1, panel.UnackCount);
            Assert.Equal("⚠ 未确认 1", panel.UnackBadgeText);
            Assert.Equal(Controls.AlarmBadgeTone.Alarm, panel.UnackBadgeTone);
            Assert.Single(panel.Rows);
            Assert.Contains("主管压力过高", panel.Rows[0].Message);
            Assert.Equal("报警中", panel.Rows[0].StateText);
            Assert.Equal("Alarm", panel.Rows[0].ToneKey);

            // ③ 过程图描红可见：主管压力超限标志 IsHighPressure=true
            Assert.True(vm.IsHighPressure);

            // 3. 浪涌解除且确认后恢复正常
            vm.ApplyData(Data(("MainOutPressure", 0.35)));
            panel.Selected = panel.Rows[0];
            panel.AckSelectedCommand.Execute(null);
            panel.Refresh();

            Assert.False(vm.IsHighPressure);
            Assert.Equal(0, vm.UnackCount);
            Assert.Equal("● 正常", vm.UnackBadgeText);
            Assert.Equal(Controls.AlarmBadgeTone.Off, vm.UnackBadgeTone);
            Assert.Empty(panel.Rows);
        }
    }
}
