using System.Windows.Media;
using Intelligent_water_storage_system.Services;
using WaterSystem.Wpf.Services;
using Xunit;

namespace WaterSystem.Wpf.Tests
{
    /// <summary>T6.1 验收：日志行着色唯一出处（色值=源 FrmMain.cs:268-294 平移，界面定稿 §3.1）。</summary>
    public class LogRowStylerTests
    {
        private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

        [Fact]
        public void ERROR_红()
        {
            var s = LogRowStyler.Resolve("ERROR", "任意");
            Assert.NotNull(s);
            Assert.Equal(C("#C00000"), s!.Fore);
            Assert.Equal(C("#FDECEC"), s.Back);
        }

        [Fact]
        public void WARN_发生_橙()
        {
            var s = LogRowStyler.Resolve("WARN", "主管压力过高: 12.0 MPa");
            Assert.NotNull(s);
            Assert.Equal(C("#B85C00"), s!.Fore);
            Assert.Equal(C("#FFF7EA"), s.Back);
        }

        [Theory]
        [InlineData("水位恢复正常")]
        [InlineData("2#泵故障已恢复")]
        public void WARN_恢复_绿(string message)
        {
            var s = LogRowStyler.Resolve("WARN", message);
            Assert.NotNull(s);
            Assert.Equal(C("#1B7A34"), s!.Fore);
            Assert.Equal(C("#EEF9F0"), s.Back);
        }

        [Theory]
        [InlineData("INFO")]
        [InlineData("DEBUG")]
        public void INFO_DEBUG_默认着色(string level)
            => Assert.Null(LogRowStyler.Resolve(level, "任意"));
    }
}
