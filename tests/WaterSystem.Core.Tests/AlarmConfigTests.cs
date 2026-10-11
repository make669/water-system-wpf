using Intelligent_water_storage_system.Model;
using Xunit;

namespace IntelligentWaterStorage.Tests
{
    /// <summary>报警阈值外置配置加载（fail-fast 语义与 TagTableLoader 对齐）。</summary>
    public class AlarmConfigTests
    {
        private static string TempPath(string name) =>
            Path.Combine(Path.GetTempPath(), $"alarmcfg_{Guid.NewGuid():N}_{name}");

        private static (AlarmConfig Cfg, List<string> Errors, List<string> Warnings) Load(
            string? content = null, bool deleteFirst = false)
        {
            var path = TempPath("t.json");
            if (deleteFirst && File.Exists(path)) File.Delete(path);
            if (content != null) File.WriteAllText(path, content);
            try
            {
                var cfg = AlarmConfig.Load(path, out var errors, out var warnings);
                return (cfg, errors, warnings);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void Load_MissingFile_DefaultsWithWarning_NoErrors()
        {
            var (cfg, errors, warnings) = Load(deleteFirst: true);

            Assert.Empty(errors);
            Assert.Contains(warnings, w => w.Contains("[AW1]"));
            Assert.Equal(1.0, cfg.WaterLevelLow);
            Assert.Equal(6.0, cfg.WaterLevelHigh);
            Assert.Equal(3, cfg.PumpCount);
        }

        [Fact]
        public void Load_ValidFile_OverridesAllValues()
        {
            var (cfg, errors, _) = Load(
                """{"WaterLevelLow":0.5,"WaterLevelHigh":5.5,"MaxFlow":800,"MaxPressure":8,"PumpCount":2}""");

            Assert.Empty(errors);
            Assert.Equal(0.5, cfg.WaterLevelLow);
            Assert.Equal(5.5, cfg.WaterLevelHigh);
            Assert.Equal(800, cfg.MaxFlow);
            Assert.Equal(8, cfg.MaxPressure);
            Assert.Equal(2, cfg.PumpCount);
        }

        [Fact]
        public void Load_PartialFile_KeepsDefaultsForMissingKeys()
        {
            var (cfg, errors, _) = Load("""{"WaterLevelHigh": 5.0}""");

            Assert.Empty(errors);
            Assert.Equal(5.0, cfg.WaterLevelHigh);      // 覆盖
            Assert.Equal(1.0, cfg.WaterLevelLow);       // 保持默认
            Assert.Equal(1000.0, cfg.MaxFlow);          // 保持默认
        }

        [Fact]
        public void Load_CommentsAndTrailingComma_Tolerated()
        {
            var (cfg, errors, _) = Load(
                """
                // 现场注释：水位上限 5m
                {
                  "WaterLevelHigh": 5.0,
                }
                """);

            Assert.Empty(errors);
            Assert.Equal(5.0, cfg.WaterLevelHigh);
        }

        [Fact]
        public void Load_InvalidJson_ReportsA01()
        {
            // 尾逗号/注释已被容忍；这里用真正非法的 JSON（缺引号）
            var (_, errors, _) = Load("""{ WaterLevelHigh: 6.0 }""");

            Assert.Contains(errors, e => e.Contains("[A01]"));
        }

        [Fact]
        public void Load_OutOfRangeValues_AllErrorsReportedTogether()
        {
            var (cfg, errors, _) = Load(
                """{"WaterLevelLow":-1,"WaterLevelHigh":-2,"MaxFlow":-1,"MaxPressure":0,"PumpCount":99}""");

            Assert.Contains(errors, e => e.Contains("[A03]"));   // Low <= 0
            Assert.Contains(errors, e => e.Contains("[A04]"));   // High <= Low
            Assert.Contains(errors, e => e.Contains("[A05]"));   // Flow <= 0
            Assert.Contains(errors, e => e.Contains("[A06]"));   // Pressure <= 0
            Assert.Contains(errors, e => e.Contains("[A07]"));   // PumpCount 1~10
            Assert.True(errors.Count >= 5, "fail-fast 语义：全部错误一次性报出");
        }

        [Fact]
        public void Load_DuplicateValveNames_ReportsA09()
        {
            var (_, errors, _) = Load(
                """{"ValveNames":["MainValve","Valve1","valve1"]}""");

            Assert.Contains(errors, e => e.Contains("[A09]"));
        }

        [Fact]
        public void Load_EmptyFile_ReportsA02()
        {
            var (_, errors, _) = Load("");

            Assert.Contains(errors, e => e.Contains("[A02]"));
        }
    }
}
