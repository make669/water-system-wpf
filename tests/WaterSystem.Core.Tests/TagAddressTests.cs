using Intelligent_water_storage_system.Model;
using Xunit;

namespace IntelligentWaterStorage.Tests
{
    /// <summary>TagAddress 模型派生属性与默认值（模块 F 组 9，2 条）。</summary>
    public class TagAddressTests
    {
        [Theory]
        [InlineData(TagType.Coil, 1)]
        [InlineData(TagType.UShort, 1)]
        [InlineData(TagType.Float, 2)]
        [InlineData(TagType.UInt, 2)]
        public void RegisterCount_DerivedByType(TagType type, int expected)
        {
            var tag = new TagAddress { Type = type };

            Assert.Equal(expected, tag.RegisterCount);
        }

        [Fact]
        public void IsCoil_AndDefaults_FollowContract()
        {
            var coil = new TagAddress { Type = TagType.Coil };
            var register = new TagAddress { Type = TagType.Float };

            Assert.True(coil.IsCoil);
            Assert.False(register.IsCoil);

            var tag = new TagAddress();
            Assert.Equal(1, tag.UnitId);        // 默认从站 1
            Assert.Equal(TagAccess.R, tag.Access); // 默认只读（安全默认）
            Assert.Equal(1.0, tag.Scale);       // 默认缩放 1.0
            Assert.Null(tag.MinValue);          // 量程可选列默认空
            Assert.Null(tag.MaxValue);
        }
    }
}
