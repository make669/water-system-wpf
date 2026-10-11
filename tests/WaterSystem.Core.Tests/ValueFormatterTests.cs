using Intelligent_water_storage_system.Services;
using Intelligent_water_storage_system.Utils;
using Xunit;

namespace IntelligentWaterStorage.Tests
{
    /// <summary>T3.2 验收：ValueFormatter 规格（TC-F1~F5，WPF实施细则 §3.7）。</summary>
    public class ValueFormatterTests
    {
        [Theory]
        [InlineData(3.59625473, 0.1, "3.6")]   // TC-F1：水位不再全精度直出（P1）
        [InlineData(12.0, 0.01, "12.00")]      // TC-F2：压力两位
        [InlineData(0, 1.0, "0")]              // TC-F3：零
        [InlineData(85.43989, 0.1, "85.4")]    // TC-F4：不再截断
        [InlineData(0.05, 0.1, "0.1")]         // TC-F5：AwayFromZero
        public void Format_按Scale有效位输出(double raw, double scale, string expected)
            => Assert.Equal(expected, ValueFormatter.Format(raw, scale));

        [Theory]
        [InlineData(1.0, 0)]
        [InlineData(0.1, 1)]
        [InlineData(0.01, 2)]
        [InlineData(0.001, 3)]
        [InlineData(0.0001, 3)]   // 封顶 3 位
        [InlineData(0, 0)]        // 非法 Scale 防御
        public void DecimalsOf_按Scale推导小数位(double scale, int expected)
            => Assert.Equal(expected, ValueFormatter.DecimalsOf(scale));
    }

    /// <summary>T3.2 验收：退避计数与退避延迟提纯（顶栏"第N次重连"数据源）。</summary>
    public class DataPollingBackoffTests
    {
        [Fact]
        public void 新建实例_ReconnectAttempts为0()
        {
            using var client = new ModbusClient("127.0.0.1", 1502);
            using var polling = new DataPollingService(client, 1000);
            Assert.Equal(0, polling.ReconnectAttempts);
        }

        [Theory]
        [InlineData(0, 1000)]
        [InlineData(1, 2000)]
        [InlineData(2, 4000)]
        [InlineData(3, 8000)]
        [InlineData(4, 16000)]
        [InlineData(5, 30000)]
        [InlineData(6, 30000)]   // 封顶
        public void BackoffDelayMs_指数退避30秒封顶(int attempts, int expected)
            => Assert.Equal(expected, DataPollingService.BackoffDelayMs(attempts));
    }
}
