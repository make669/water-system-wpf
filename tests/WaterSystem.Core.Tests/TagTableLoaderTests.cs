using System.Text;
using Intelligent_water_storage_system.Model;
using Intelligent_water_storage_system.Utils;
using Xunit;

namespace IntelligentWaterStorage.Tests
{
    /// <summary>
    /// TagTableLoader + fail-fast 校验（模块 F 组 3，16 条；设计依据 docs/模块F-设计.md 7-组3）。
    /// 缺陷 D4 修复：全部用例自建临时 CSV（UTF-8 BOM），不依赖仓库 TagTable.csv；
    /// TestRunner 中同源断言保留为集成回归快照，此处为规范单测。
    /// </summary>
    public class TagTableLoaderTests : IDisposable
    {
        private readonly string _csvPath = Path.Combine(Path.GetTempPath(), $"tagtbl_{Guid.NewGuid():N}.csv");

        /// <summary>最小合法表（标准列序；[2..] = 表头 + 三行数据，供追加坏行做错误注入）。</summary>
        private static readonly string[] MinimalValid =
        {
            "# 注释行应被忽略",
            "TagName,Address,Type,Access,Scale,Unit,UnitId,Group,Description,Min,Max",
            "WaterLevel,32,float,R,0.1,m,1,工艺,当前储水池水位",
            "Speed,6,ushort,R,1.0,r/m,1,泵1,转速",
            "Pump1_Run,0,coil,RW,1.0,,1,泵1,启停",
        };

        public void Dispose()
        {
            try { if (File.Exists(_csvPath)) File.Delete(_csvPath); } catch { }
        }

        private string[] Write(params string[] lines)
        {
            File.WriteAllLines(_csvPath, lines, new UTF8Encoding(true));
            return lines;
        }

        private static (List<TagAddress> Tags, List<string> Errors, List<string> Warnings) Load(IEnumerable<string> lines)
        {
            var path = Path.Combine(Path.GetTempPath(), $"tagtbl_{Guid.NewGuid():N}.csv");
            try
            {
                File.WriteAllLines(path, lines, new UTF8Encoding(true));
                var tags = TagTableLoader.Load(path, out var errors, out var warnings);
                return (tags, errors, warnings);
            }
            finally
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
        }

        [Fact]
        public void Load_MinimalValidTable_ParsesByColumnName_IgnoresCommentsAndBlanks()
        {
            var (tags, errors, warnings) = Load(MinimalValid);

            Assert.Empty(errors);
            Assert.Empty(warnings);
            Assert.Equal(3, tags.Count);

            var water = tags.Single(t => t.TagName == "WaterLevel");
            Assert.Equal(32, water.Address);
            Assert.Equal(TagType.Float, water.Type);
            Assert.Equal(TagAccess.R, water.Access);
            Assert.Equal(0.1, water.Scale);
            Assert.Equal("m", water.Unit);
            Assert.Equal(1, water.UnitId);
            Assert.Equal("工艺", water.Group);

            var run = tags.Single(t => t.TagName == "Pump1_Run");
            Assert.Equal(TagType.Coil, run.Type);
            Assert.True(run.IsCoil);
        }

        [Fact]
        public void Load_ShuffledColumnOrder_ParsesByColumnName()
        {
            // 列顺序不敏感：乱序列头 + 空行，仍按列名解析
            var (tags, errors, warnings) = Load(new[]
            {
                "",
                "Type,Scale,TagName,Address,Access,Unit,UnitId,Group,Description",
                "float,0.1,WaterLevel,32,R,m,1,工艺,水位",
            });

            Assert.Empty(errors);
            Assert.Empty(warnings);
            var water = Assert.Single(tags);
            Assert.Equal(32, water.Address);
            Assert.Equal(TagType.Float, water.Type);
        }

        [Fact]
        public void Load_MissingFile_ReportsE01()
        {
            var tags = TagTableLoader.Load(
                Path.Combine(Path.GetTempPath(), $"no_such_{Guid.NewGuid():N}.csv"), out var errors, out _);

            Assert.Empty(tags);
            Assert.Contains(errors, e => e.Contains("[E01]"));
        }

        [Fact]
        public void Load_MissingRequiredColumn_ReportsE01()
        {
            var (_, errors, _) = Load(new[]
            {
                "TagName,Address,Type,Access",       // 缺 Scale 列
                "T1,0,ushort,R",
            });

            Assert.Contains(errors, e => e.Contains("[E01]") && e.Contains("Scale"));
        }

        [Fact]
        public void Load_DuplicateTagName_ReportsE02()
        {
            var (_, errors, _) = Load(MinimalValid[1..]
                .Append("Pump1_Run,5,coil,RW,1.0,,1,泵1,重复名称"));

            Assert.Contains(errors, e => e.Contains("[E02]") && e.Contains("Pump1_Run"));
        }

        [Theory]
        [InlineData(",40,ushort,R,1.0")]          // 空名
        [InlineData("9bad,40,ushort,R,1.0")]      // 数字开头
        [InlineData("has space,40,ushort,R,1.0")] // 含空格
        public void Load_InvalidTagName_ReportsE03(string row)
        {
            var (_, errors, _) = Load(MinimalValid.Skip(1).Append(row));

            Assert.Contains(errors, e => e.Contains("[E03]"));
        }

        [Theory]
        [InlineData("BadAddr,70000,ushort,R,1.0")]  // 越界
        [InlineData("BadAddr,abc,ushort,R,1.0")]    // 非数字
        public void Load_InvalidAddress_ReportsE04(string row)
        {
            var (_, errors, _) = Load(MinimalValid.Skip(1).Append(row));

            Assert.Contains(errors, e => e.Contains("[E04]"));
        }

        [Theory]
        [InlineData("BadUnit,40,ushort,R,1.0,,0")]    // UnitId=0
        [InlineData("BadUnit,40,ushort,R,1.0,,300")]  // UnitId>255
        public void Load_InvalidUnitId_ReportsE04(string row)
        {
            var (_, errors, _) = Load(MinimalValid.Skip(1).Append(row));

            Assert.Contains(errors, e => e.Contains("[E04]") && e.Contains("UnitId"));
        }

        [Theory]
        [InlineData("BadType,40,abc,R,1.0")]       // Type 非法
        [InlineData("BadAccess,40,ushort,X,1.0")]  // Access 非法
        public void Load_InvalidEnumField_ReportsE05(string row)
        {
            var (_, errors, _) = Load(MinimalValid.Skip(1).Append(row));

            Assert.Contains(errors, e => e.Contains("[E05]"));
        }

        [Fact]
        public void Load_FloatAtOddAddress_ReportsE06()
        {
            var (_, errors, _) = Load(MinimalValid.Skip(1).Append("BadAlign,51,float,R,0.1"));

            Assert.Contains(errors, e => e.Contains("[E06]"));
        }

        [Theory]
        [InlineData("BadScale,52,ushort,R,0")]     // 零缩放
        [InlineData("BadScale,53,ushort,R,abc")]   // 非数字
        public void Load_InvalidScale_ReportsE07(string row)
        {
            var (_, errors, _) = Load(MinimalValid.Skip(1).Append(row));

            Assert.Contains(errors, e => e.Contains("[E07]"));
        }

        [Fact]
        public void Load_OverlappingAddress_WarnsW02_ButLoads()
        {
            var (tags, errors, warnings) = Load(MinimalValid[1..]
                .Append("Overlap,6,ushort,R,1.0"));  // 与 Speed 的地址 6 重叠

            Assert.Empty(errors);
            Assert.Contains(warnings, w => w.Contains("[W02]"));
            Assert.Equal(4, tags.Count);    // 警告不阻塞加载
        }

        [Theory]
        [InlineData("OnlyMin,40,ushort,R,1.0,,1,,只填Min,-10,")]
        [InlineData("MinGtMax,41,ushort,R,1.0,,1,,Min大于Max,50,10")]
        [InlineData("NotNumeric,42,ushort,R,1.0,,1,,量程非数字,abc,50")]
        public void Load_InvalidMinMax_WarnsW03_AndIgnores(string row)
        {
            var (tags, errors, warnings) = Load(MinimalValid[1..].Append(row));

            Assert.Empty(errors);
            Assert.Contains(warnings, w => w.Contains("[W03]"));
            var bad = tags.Single(t => t.TagName == row.Split(',')[0]);
            Assert.Null(bad.MinValue);
            Assert.Null(bad.MaxValue);
        }

        [Fact]
        public void Load_ValidMinMax_AssignsRange()
        {
            var (tags, errors, warnings) = Load(MinimalValid[1..]
                .Append("WithRange,44,ushort,R,1.0,,1,,带量程,0,100"));

            Assert.Empty(errors);
            Assert.DoesNotContain(warnings, w => w.Contains("[W03]"));
            var tag = tags.Single(t => t.TagName == "WithRange");
            Assert.Equal(0, tag.MinValue);
            Assert.Equal(100, tag.MaxValue);
        }

        [Fact]
        public void Load_MultipleErrors_ReportedTogether_NotFailFast()
        {
            var (_, errors, _) = Load(MinimalValid[1..]
                .Append("9bad,40,ushort,R,1.0")          // E03
                .Append("BadAddr,70000,ushort,R,1.0")    // E04
                .Append("BadAlign,51,float,R,0.1"));     // E06

            Assert.Contains(errors, e => e.Contains("[E03]"));
            Assert.Contains(errors, e => e.Contains("[E04]"));
            Assert.Contains(errors, e => e.Contains("[E06]"));
            Assert.True(errors.Count >= 3, "fail-fast 语义：全部错误一次性报出");
        }

        [Fact]
        public void CheckUiBindings_ReportsE08E09W01()
        {
            var path = Path.Combine(Path.GetTempPath(), $"tagtbl_{Guid.NewGuid():N}.csv");
            try
            {
                // 两个 RW 标签，只给其中一个控制控件绑定 → 另一个触发 E08
                File.WriteAllLines(path, new[]
                {
                    MinimalValid[1],
                    MinimalValid[2],
                    MinimalValid[3],
                    "Valve1,10,coil,RW,1.0,,1,阀门,无绑定控制控件",
                }, new UTF8Encoding(true));
                var tags = TagTableLoader.Load(path, out _, out _);

                var errors = new List<string>();
                var warnings = new List<string>();
                TagTableLoader.CheckUiBindings(
                    tags,
                    controlBindings: new[] { "Pump1_Run" },                  // Valve1 无控制绑定 → E08
                    displayBindings: new[] { "Pump1_Run", "WaterLevel", "NotExistTag" },
                    errors, warnings);

                Assert.Contains(errors, e => e.Contains("[E08]") && e.Contains("Valve1"));
                Assert.Contains(errors, e => e.Contains("[E09]") && e.Contains("NotExistTag"));
                Assert.Contains(warnings, w => w.Contains("[W01]") && w.Contains("Speed")); // 表有 UI 无
            }
            finally
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
        }

        [Fact]
        public void CheckUiBindings_FullCoverage_NoErrors()
        {
            Write(MinimalValid);
            var tags = TagTableLoader.Load(_csvPath, out _, out _);
            var all = tags.Select(t => t.TagName).ToList();

            var errors = new List<string>();
            var warnings = new List<string>();
            TagTableLoader.CheckUiBindings(tags, all, all, errors, warnings);

            Assert.Empty(errors);
            Assert.Empty(warnings);
        }
    }
}
