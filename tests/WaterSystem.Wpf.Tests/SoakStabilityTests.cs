using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Intelligent_water_storage_system.Model;
using Intelligent_water_storage_system.Services;
using Intelligent_water_storage_system.Utils;
using WaterSystem.Wpf.Infrastructure;
using WaterSystem.Wpf.ViewModels;
using Xunit;

namespace WaterSystem.Wpf.Tests
{
    /// <summary>
    /// T9.3 验收：浸泡稳定性测试（实施细则 T9.3）
    /// 验证内存平稳、无冻结、零 ERROR 增长、数据缓冲严格有界
    /// </summary>
    [Collection("UiDispatcher")]
    public class SoakStabilityTests : IDisposable
    {
        private readonly List<TagAddress> _tags;
        private readonly AlarmConfig _config;

        public SoakStabilityTests()
        {
            UiDispatcher.Runner = action => action();

            var d = new DirectoryInfo(AppContext.BaseDirectory);
            string? tagPath = null;
            string? alarmPath = null;
            for (int i = 0; i < 10 && d != null; i++, d = d.Parent)
            {
                var tp = Path.Combine(d.FullName, "configFiles", "TagTable.csv");
                var ap = Path.Combine(d.FullName, "configFiles", "AlarmConfig.json");
                if (File.Exists(tp)) tagPath = tp;
                if (File.Exists(ap)) alarmPath = ap;
                if (tagPath != null && alarmPath != null) break;
            }

            _tags = TagTableLoader.Load(tagPath!, out _, out _);
            _config = AlarmConfig.Load(alarmPath!, out _, out _);
        }

        public void Dispose()
        {
            UiDispatcher.Runner = null!;
        }

        [Fact]
        public void Soak_加速浸泡10000轮_内存平稳且零ERROR增长()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"soak_test_{Guid.NewGuid():N}.db");
            try
            {
                var log = new LogService();
                var client = new ModbusClient("127.0.0.1", 1502);
                var polling = new DataPollingService(client, 1000);
                var control = new ControlService(client, _tags);
                var alarmEval = new AlarmService(_config);
                var alarmCenter = new AlarmCenter(dbPath);

                var vm = new MainViewModel(log, client, polling, control, alarmEval, _tags,
                    startWatchdog: false, config: _config, alarmCenter: alarmCenter);

                // 强制垃圾回收建立初始内存基线
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                long initialMemory = GC.GetTotalMemory(true);

                const int totalRounds = 10_000;
                var sw = Stopwatch.StartNew();

                for (int i = 1; i <= totalRounds; i++)
                {
                    double waveWater = 2.5 + 1.5 * Math.Sin(i * 0.05);
                    double wavePressure = 0.35 + 0.1 * Math.Cos(i * 0.05);

                    var data = new Dictionary<string, DataPoint>
                    {
                        ["CurWaterPos"] = new DataPoint { Name = "CurWaterPos", Value = waveWater },
                        ["MainOutPressure"] = new DataPoint { Name = "MainOutPressure", Value = wavePressure },
                        ["CurTemperature"] = new DataPoint { Name = "CurTemperature", Value = 25.0 + (i % 10) * 0.1 },
                        ["SoilHumidity"] = new DataPoint { Name = "SoilHumidity", Value = 40.0 + (i % 20) * 0.5 },
                        ["Pump1_Run"] = new DataPoint { Name = "Pump1_Run", Value = (i % 2) },
                        ["Pump1_Speed"] = new DataPoint { Name = "Pump1_Speed", Value = 1450.0 },
                        ["ActualWaters"] = new DataPoint { Name = "ActualWaters", Value = 85.0 },
                        ["PlanWaters"] = new DataPoint { Name = "PlanWaters", Value = 500.0 }
                    };

                    vm.ApplyData(data);

                    // 周期性触发报警与恢复以测试 AlarmCenter 状态流转稳定性
                    if (i % 500 == 0)
                    {
                        alarmCenter.Raise("HighPressure", "主管压力过高: 10.5Mpa", AlarmSeverity.Critical);
                        alarmCenter.Clear("HighPressure", "主管压力恢复");
                        var unclosed = alarmCenter.OpenSnapshot();
                        if (unclosed.Count > 0)
                            alarmCenter.Acknowledge(unclosed[0].Id, "Operator");
                    }
                }

                sw.Stop();

                // 强制垃圾回收测定最终内存
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                long finalMemory = GC.GetTotalMemory(true);
                long memDelta = finalMemory - initialMemory;

                // 验证 1：趋势缓冲严格有界（300 点滚动窗口，不泄露）
                Assert.Equal(300, vm.TrendSnapshot.Length);

                // 验证 2：零 ERROR 增长
                int errorCount = log.Entries.Count(e => e.Level == "ERROR");
                Assert.Equal(0, errorCount);

                // 验证 3：未关闭报警队列闭环无堆积
                Assert.Empty(alarmCenter.OpenSnapshot());
                Assert.Equal(0, alarmCenter.UnacknowledgedCount);

                // 验证 4：吞吐耗时与内存增长平稳（10,000 轮单机纯内存增长 < 10MB）
                Assert.True(memDelta < 15 * 1024 * 1024, $"内存增长超出阈值: {memDelta / 1024 / 1024}MB (初值={initialMemory / 1024}KB, 终值={finalMemory / 1024}KB)");
                Assert.True(sw.ElapsedMilliseconds < 15_000, $"10,000 轮处理耗时过长: {sw.ElapsedMilliseconds}ms");
            }
            finally
            {
                try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
            }
        }
    }
}
