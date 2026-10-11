using Intelligent_water_storage_system.Services;
using Xunit;

namespace IntelligentWaterStorage.Tests
{
    /// <summary>
    /// AlarmCenter 生命周期 + SQLite 持久化（模块 F 组 4，12 条；设计依据 docs/模块F-设计.md 7-组4）。
    /// 自 TestRunner 迁入重组扩充；每用例唯一临时 db（xUnit 每用例新建类实例 → Dispose 独立清理）；
    /// 时间断言用下限容差（D5：DateTime.Now 非确定性）。
    /// </summary>
    public class AlarmCenterTests : IDisposable
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"alarms_{Guid.NewGuid():N}.db");

        public void Dispose()
        {
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
        }

        [Fact]
        public void Raise_IncrementsUnacknowledgedAndOpen()
        {
            var center = new AlarmCenter(_dbPath);

            center.Raise("HighPressure", "主管压力过高: 12.0Mpa", AlarmSeverity.Critical);

            Assert.Equal(1, center.UnacknowledgedCount);
            Assert.Equal(1, center.OpenCount);
        }

        [Fact]
        public void Raise_DuplicateActiveName_Deduplicates()
        {
            var center = new AlarmCenter(_dbPath);
            center.Raise("HighPressure", "第一次", AlarmSeverity.Critical);

            center.Raise("HighPressure", "第二次", AlarmSeverity.Critical); // 同名活跃 → 去重

            Assert.Equal(1, center.OpenCount);
        }

        [Fact]
        public void Clear_WithoutAck_KeepsOpenInClearedState()
        {
            var center = new AlarmCenter(_dbPath);
            center.Raise("HighPressure", "主管压力过高", AlarmSeverity.Critical);

            center.Clear("HighPressure", "主管压力恢复正常");

            var record = Assert.Single(center.OpenSnapshot());
            Assert.False(record.IsActive);
            Assert.True(record.NeedsAck);
            Assert.Equal("已恢复·未确认", record.StateText);
            Assert.Equal(1, center.UnacknowledgedCount);   // 恢复后仍需确认
        }

        [Fact]
        public void Acknowledge_ClearedRecord_ClosesIt()
        {
            var center = new AlarmCenter(_dbPath);
            center.Raise("HighPressure", "主管压力过高", AlarmSeverity.Critical);
            center.Clear("HighPressure", "恢复");
            var id = center.OpenSnapshot()[0].Id;

            center.Acknowledge(id, "测试员");

            Assert.Equal(0, center.OpenCount);
            Assert.Equal(0, center.UnacknowledgedCount);
        }

        [Fact]
        public void Acknowledge_ActiveRecord_KeepsOpenAsAcked()
        {
            var center = new AlarmCenter(_dbPath);
            center.Raise("Pump2Fault", "2#故障", AlarmSeverity.Critical);
            var id = center.OpenSnapshot()[0].Id;

            center.Acknowledge(id, "测试员");

            var record = Assert.Single(center.OpenSnapshot());
            Assert.True(record.IsActive);
            Assert.False(record.NeedsAck);
            Assert.Equal("报警中·已确认", record.StateText);
            Assert.Equal("测试员", record.AckedBy);
        }

        [Fact]
        public void AcknowledgeAll_ClearsUnacknowledged()
        {
            var center = new AlarmCenter(_dbPath);
            center.Raise("Pump2Fault", "2#故障", AlarmSeverity.Critical);
            center.Raise("HighFlow", "主管流量过高", AlarmSeverity.Warning);

            center.AcknowledgeAll();

            Assert.Equal(0, center.UnacknowledgedCount);
            Assert.Equal(2, center.OpenCount);   // 仍活跃的确认后保留在未关闭列表
        }

        [Fact]
        public void Acknowledge_Twice_IsIdempotent()
        {
            var center = new AlarmCenter(_dbPath);
            center.Raise("HighPressure", "压力", AlarmSeverity.Critical);
            var id = center.OpenSnapshot()[0].Id;
            center.Acknowledge(id);
            var ackedAt = center.OpenSnapshot()[0].AckedAt;

            center.Acknowledge(id);   // 二次确认：无变化不抛

            Assert.Equal(ackedAt, center.OpenSnapshot()[0].AckedAt);
        }

        [Fact]
        public void Duration_ActiveRecord_LowerBoundTolerance()
        {
            var center = new AlarmCenter(_dbPath);
            center.Raise("HighFlow", "流量", AlarmSeverity.Warning);

            Thread.Sleep(80);
            var duration = center.OpenSnapshot()[0].Duration;

            Assert.True(duration.TotalMilliseconds >= 50, $"实际 {duration.TotalMilliseconds}ms");
        }

        [Theory]
        [InlineData("Pump1Fault", AlarmSeverity.Critical)]
        [InlineData("Pump3Fault", AlarmSeverity.Critical)]
        [InlineData("HighPressure", AlarmSeverity.Critical)]
        [InlineData("LowWaterLevel", AlarmSeverity.Critical)]
        [InlineData("HighFlow", AlarmSeverity.Warning)]
        [InlineData("HighWaterLevel", AlarmSeverity.Warning)]
        public void MapSeverity_FollowsContract(string name, AlarmSeverity expected)
        {
            Assert.Equal(expected, AlarmCenter.MapSeverity(name));
        }

        [Fact]
        public void Persistence_SurvivesReopen_QueryHistoryFindsClosedRecord()
        {
            long id;
            var center = new AlarmCenter(_dbPath);
            center.Raise("HighPressure", "主管压力过高: 12.0Mpa", AlarmSeverity.Critical);
            id = center.OpenSnapshot()[0].Id;
            center.Clear("HighPressure", "主管压力恢复正常");
            center.Acknowledge(id, "测试员");
            // （AlarmCenter 无 Dispose；模拟重启 = 丢弃实例另开新连接）
            var reopened = new AlarmCenter(_dbPath);
            var history = reopened.QueryHistory(DateTime.Now.AddHours(-1), DateTime.Now, null, null);

            var record = Assert.Single(history);
            Assert.Equal("HighPressure", record.Name);
            Assert.False(record.IsActive);   // 已恢复
            Assert.False(record.NeedsAck);   // 已确认
            Assert.Equal("测试员", record.AckedBy);
        }

        [Fact]
        public void QueryHistory_FiltersBySeverityAndKeyword()
        {
            var center = new AlarmCenter(_dbPath);
            center.Raise("HighPressure", "主管压力过高", AlarmSeverity.Critical);
            center.Raise("HighFlow", "主管流量过高", AlarmSeverity.Warning);
            center.Raise("Pump2Fault", "2#故障", AlarmSeverity.Critical);

            var warnings = center.QueryHistory(null, null, AlarmSeverity.Warning, null);
            Assert.Single(warnings);
            Assert.Equal("HighFlow", warnings[0].Name);

            var byKeyword = center.QueryHistory(null, null, null, "故障");
            Assert.Single(byKeyword);
            Assert.Equal("Pump2Fault", byKeyword[0].Name);

            var future = center.QueryHistory(DateTime.Now.AddHours(1), null, null, null);
            Assert.Empty(future);   // 时间窗过滤
        }

        [Fact]
        public void StorageFailure_DegradesToMemoryLifecycle()
        {
            var errors = new List<string>();
            // 非法路径字符 → EnsureDatabase/Insert 失败 → 降级为内存生命周期 + OnError
            var center = new AlarmCenter(Path.Combine(Path.GetTempPath(), "a<b", "x.db"));
            center.OnError += msg => errors.Add(msg);

            center.Raise("HighPressure", "压力", AlarmSeverity.Critical);
            center.Clear("HighPressure", "恢复");

            Assert.NotEmpty(errors);                     // 落库失败降级为 OnError
            Assert.Equal(1, center.OpenCount);           // 内存生命周期照常（已恢复未关闭）
            Assert.Equal(1, center.UnacknowledgedCount); // 未确认（恢复不等于确认）
        }
    }
}
