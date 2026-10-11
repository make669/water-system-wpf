using Intelligent_water_storage_system.Model;
using Intelligent_water_storage_system.Utils;
using Xunit;

namespace IntelligentWaterStorage.Tests
{
    /// <summary>
    /// GroupPlanner 地址分组（模块 F 组 7，6 条；设计依据 docs/模块F-设计.md 7-组7、5.2 行为不变量）。
    /// 用例 4 是模块 A"按 RegisterCount 计算组末端"修复的回归锚点。
    /// </summary>
    public class GroupPlannerTests
    {
        private static TagAddress Tag(string name, int address, TagType type = TagType.UShort, byte unitId = 1) =>
            new() { TagName = name, Address = address, Type = type, UnitId = unitId, Access = TagAccess.R };

        [Fact]
        public void Group_MergesConsecutiveAddresses()
        {
            var tags = new List<TagAddress>
            {
                Tag("C", 2), Tag("A", 0), Tag("B", 1),   // 乱序输入
            };

            var groups = GroupPlanner.Group(tags);

            var group = Assert.Single(groups);
            Assert.Equal(0, group.Key);
            Assert.Equal(new[] { "A", "B", "C" }, group.Select(t => t.TagName)); // 组内地址升序
        }

        [Fact]
        public void Group_GapBoundary_MergesAt8_SplitsAt9()
        {
            // 间隔恰 8：8 ≤ 0+1+8 → 合并
            var merged = GroupPlanner.Group(new List<TagAddress> { Tag("A", 0), Tag("B", 9) });
            Assert.Single(merged);

            // 间隔 9：10 > 0+1+8 → 拆分
            var split = GroupPlanner.Group(new List<TagAddress> { Tag("A", 0), Tag("B", 10) });
            Assert.Equal(2, split.Count);
        }

        [Fact]
        public void Group_SeparatesUnitIds()
        {
            var tags = new List<TagAddress> { Tag("A", 0, unitId: 1), Tag("B", 1, unitId: 2) };

            var groups = GroupPlanner.Group(tags);

            Assert.Equal(2, groups.Count);
            Assert.All(groups, g => Assert.Single(g));
        }

        [Fact]
        public void Group_FloatEnd_AccountsRegisterCount()
        {
            // float@0 占 0~1 → 组末端=2；地址 10 ≤ 2+8 → 合并（旧 +1 逻辑：10 > 1+8 会错拆）
            var tags = new List<TagAddress> { Tag("F", 0, TagType.Float), Tag("B", 10) };

            var groups = GroupPlanner.Group(tags);

            var group = Assert.Single(groups);
            Assert.Equal(0, group.Key);
            Assert.Equal(2, group.Count());

            // 地址 11 > 2+8 → 拆分
            var split = GroupPlanner.Group(new List<TagAddress> { Tag("F", 0, TagType.Float), Tag("B", 11) });
            Assert.Equal(2, split.Count);
        }

        [Fact]
        public void Group_SplitsAtProtocolRegisterLimit()
        {
            // 起始 0、跨度 126 > 125（单次寄存器上限）→ 切两组
            var tags = new List<TagAddress> { Tag("A", 0), Tag("B", 125) };

            var groups = GroupPlanner.Group(tags);

            Assert.Equal(2, groups.Count);
            Assert.Equal(0, groups[0].Key);
            Assert.Equal(125, groups[1].Key);
        }

        [Fact]
        public void Group_EmptyInput_ReturnsEmpty()
        {
            var groups = GroupPlanner.Group(new List<TagAddress>());

            Assert.Empty(groups);
        }
    }
}
