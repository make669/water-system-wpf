using System.IO;
using WaterSystem.Wpf;
using Xunit;

namespace WaterSystem.Wpf.Tests
{
    /// <summary>
    /// 启动契约校验（T2.2 验收）：9 类错误注入逐一拒绝启动（对应源验收标准 T1.3 口径）。
    /// E01 删表头列 / E02 重复名 / E03 空名 / E04 地址越界 / E05 Type 非法 / E06 float 奇地址 /
    /// E07 Scale=0 / A01 坏 JSON / A04 阈值倒挂。E08/E09 属绑定核对，在 T5.5 接线后覆盖。
    /// </summary>
    public class AppBootstrapperTests
    {
        static string? FindRepoFile(string file)
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && d != null; i++, d = d.Parent)
            {
                var p = Path.Combine(d.FullName, "configFiles", file);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        static string WriteConfigDir(string tagCsv, string alarmJson)
        {
            string baseDir = Path.Combine(Path.GetTempPath(), "precheck_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(baseDir, "configFiles"));
            File.WriteAllText(Path.Combine(baseDir, "configFiles", "TagTable.csv"), tagCsv);
            File.WriteAllText(Path.Combine(baseDir, "configFiles", "AlarmConfig.json"), alarmJson);
            return baseDir;
        }

        static string ValidTags() => File.ReadAllText(FindRepoFile("TagTable.csv")
            ?? throw new InvalidOperationException("未找到仓库内 configFiles/TagTable.csv"));
        static string ValidAlarm() => File.ReadAllText(FindRepoFile("AlarmConfig.json")
            ?? throw new InvalidOperationException("未找到仓库内 configFiles/AlarmConfig.json"));

        [Fact]
        public void 正常配置_预检通过且36点()
        {
            string baseDir = WriteConfigDir(ValidTags(), ValidAlarm());
            var r = AppBootstrapper.PrecheckConfig(baseDir);
            Assert.True(r.Ok, string.Join(" | ", r.Errors));
            Assert.Equal(36, r.Tags.Count);
            Assert.NotNull(r.Config);
        }

        [Theory]
        [InlineData("Pressure-1,40,float,R,0.01,Mpa,1,分管1,重复名称", null, "E02")]                    // E02 重复名
        [InlineData(",40,ushort,R,1.0,,1,泵1,空名称", null, "E03")]                                   // E03 空名
        [InlineData("BadAddr,70000,ushort,R,1.0,,1,泵1,地址越界", null, "E04")]                        // E04 地址越界
        [InlineData("BadType,40,abc,R,1.0,,1,泵1,类型非法", null, "E05")]                              // E05 Type 非法
        [InlineData("BadAlign,51,float,R,0.1,,1,泵1,奇地址浮点", null, "E06")]                         // E06 未偶对齐
        [InlineData("BadScale,52,ushort,R,0,,1,泵1,零缩放", null, "E07")]                              // E07 Scale=0
        public void 标签表错误注入_拒绝启动并报错误码(string append, string? header, string code)
        {
            var lines = ValidTags().Split('\n').ToList();
            lines.Add(append.TrimEnd('\r'));
            if (header != null)
            {
                int idx = lines.FindIndex(l => !l.Trim().StartsWith("#") && l.Trim().StartsWith("TagName"));
                lines[idx] = header;
            }
            string baseDir = WriteConfigDir(string.Join('\n', lines), ValidAlarm());
            var r = AppBootstrapper.PrecheckConfig(baseDir);
            Assert.False(r.Ok);
            Assert.Contains(r.Errors, e => e.Contains($"[{code}]"));
        }

        [Fact]
        public void E01_表头缺列_拒绝启动()
        {
            var lines = ValidTags().Split('\n').ToList();
            int idx = lines.FindIndex(l => !l.Trim().StartsWith("#") && l.Trim().StartsWith("TagName"));
            lines[idx] = "TagName,Address,Type";
            string baseDir = WriteConfigDir(string.Join('\n', lines), ValidAlarm());
            var r = AppBootstrapper.PrecheckConfig(baseDir);
            Assert.False(r.Ok);
            Assert.Contains(r.Errors, e => e.Contains("[E01]"));
        }

        [Fact]
        public void A01_报警配置坏JSON_拒绝启动()
        {
            string baseDir = WriteConfigDir(ValidTags(), "{ this is not json");
            var r = AppBootstrapper.PrecheckConfig(baseDir);
            Assert.False(r.Ok);
            Assert.Contains(r.Errors, e => e.Contains("[A01]"));
        }

        [Fact]
        public void A04_报警阈值倒挂_拒绝启动()
        {
            string json = ValidAlarm().Replace("\"WaterLevelHigh\": 6.0", "\"WaterLevelHigh\": 0.5");
            string baseDir = WriteConfigDir(ValidTags(), json);
            var r = AppBootstrapper.PrecheckConfig(baseDir);
            Assert.False(r.Ok);
            Assert.Contains(r.Errors, e => e.Contains("[A04]"));
        }

        [Fact]
        public void 缺失配置文件_E01拒绝并给AW1警告()
        {
            string baseDir = Path.Combine(Path.GetTempPath(), "precheck_empty_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(baseDir);
            var r = AppBootstrapper.PrecheckConfig(baseDir);
            Assert.False(r.Ok);
            Assert.Contains(r.Errors, e => e.Contains("[E01]"));
            Assert.Contains(r.Warnings, w => w.Contains("AW1"));
        }
    }
}
