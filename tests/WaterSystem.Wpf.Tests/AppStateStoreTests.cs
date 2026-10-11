using System.IO;
using WaterSystem.Wpf.State;
using Xunit;

namespace WaterSystem.Wpf.Tests
{
    /// <summary>T8.1 验收：app_init.json v2 往返 + fail-soft（坏文件 → null 不阻断启动）。</summary>
    public class AppStateStoreTests
    {
        private static string TempPath() => Path.Combine(Path.GetTempPath(), $"state_{Guid.NewGuid():N}.json");

        [Fact]
        public void 往返_通信参数与开关状态()
        {
            string path = TempPath();
            var switches = new Dictionary<string, bool> { ["Pump1_Run"] = true, ["MainValve"] = false, ["Valve1"] = true };
            AppStateStore.Save(path, "127.0.0.1", 1502, switches);

            var state = AppStateStore.Load(path);
            Assert.NotNull(state);
            Assert.Equal("127.0.0.1", state!.Ip);
            Assert.Equal(1502, state.Port);
            Assert.True(state.Switches["Pump1_Run"]);
            Assert.False(state.Switches["MainValve"]);
            Assert.True(state.Switches["Valve1"]);
            File.Delete(path);
        }

        [Fact]
        public void 坏JSON_返回null不抛()
        {
            string path = TempPath();
            File.WriteAllText(path, "{ broken json");
            Assert.Null(AppStateStore.Load(path));
            File.Delete(path);
        }

        [Fact]
        public void 文件缺失_返回null()
            => Assert.Null(AppStateStore.Load(TempPath()));
    }
}
