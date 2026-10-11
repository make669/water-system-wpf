using Intelligent_water_storage_system.Services;
using Xunit;

namespace IntelligentWaterStorage.Tests
{
    /// <summary>CommStatistics 线程安全计数（模块 F 组 5，5 条；设计依据 docs/模块F-设计.md 7-组5）。</summary>
    public class CommStatisticsTests
    {
        [Fact]
        public void Counters_Accumulate()
        {
            var stats = new CommStatistics();

            stats.RecordRequest();
            stats.RecordRequest();
            stats.RecordRequest();
            stats.RecordSuccess(120);
            stats.RecordSuccess(80);
            stats.RecordFailure();
            stats.RecordSent(100);
            stats.RecordReceived(240);
            stats.RecordReconnect();

            Assert.Equal(3, stats.RequestCount);
            Assert.Equal(2, stats.SuccessCount);
            Assert.Equal(1, stats.FailCount);
            Assert.Equal(100, stats.TxBytes);
            Assert.Equal(240, stats.RxBytes);
            Assert.Equal(1, stats.ReconnectCount);
            Assert.Equal(80, stats.LastRttMs);
        }

        [Fact]
        public void SuccessRate_Computes_And_HandlesZeroRequests()
        {
            var empty = new CommStatistics();
            Assert.Equal(0, empty.SuccessRate);   // 除零 → 0

            var stats = new CommStatistics();
            stats.RecordRequest();
            stats.RecordRequest();
            stats.RecordSuccess(10);
            stats.RecordFailure();
            Assert.Equal(0.5, stats.SuccessRate, 6);
        }

        [Fact]
        public void AvgRtt_Computes_And_HandlesZeroSuccess()
        {
            var empty = new CommStatistics();
            Assert.Equal(0, empty.AvgRttMs);   // 无成功记录 → 0

            var stats = new CommStatistics();
            stats.RecordSuccess(100);
            stats.RecordSuccess(200);
            Assert.Equal(150, stats.AvgRttMs, 6);
        }

        [Fact]
        public void Reset_ClearsAllCounters()
        {
            var stats = new CommStatistics();
            stats.RecordRequest();
            stats.RecordSuccess(50);
            stats.RecordFailure();
            stats.RecordSent(10);
            stats.RecordReceived(20);
            stats.RecordReconnect();

            stats.Reset();

            Assert.Equal(0, stats.RequestCount);
            Assert.Equal(0, stats.SuccessCount);
            Assert.Equal(0, stats.FailCount);
            Assert.Equal(0, stats.TxBytes);
            Assert.Equal(0, stats.RxBytes);
            Assert.Equal(0, stats.ReconnectCount);
            Assert.Equal(0, stats.LastRttMs);
            Assert.Equal(0, stats.AvgRttMs);
        }

        [Fact]
        public void ConcurrentIncrements_DoNotLoseCounts()
        {
            var stats = new CommStatistics();
            const int threads = 4, perThread = 1000;

            Parallel.For(0, threads, _ =>
            {
                for (int i = 0; i < perThread; i++)
                    stats.RecordRequest();
            });

            Assert.Equal(threads * perThread, stats.RequestCount);   // Interlocked 实证
        }
    }
}
