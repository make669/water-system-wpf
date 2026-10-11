using WaterSystem.Wpf.Infrastructure;
using Xunit;

namespace WaterSystem.Wpf.Tests
{
    /// <summary>T2.4 验收：UiDispatcher 注入 Runner 后，Post 在封送目标上执行且保持顺序。</summary>
    public class UiDispatcherTests
    {
        [Fact]
        public void Post_经注入Runner执行并保持顺序()
        {
            var executed = new List<int>();
            UiDispatcher.Runner = action =>
            {
                int marker = executed.Count;   // 先占位标记，动作执行后置 1——不能用 ^1（动作可能追加元素）
                executed.Add(0);
                action();
                executed[marker] = 1;
            };
            try
            {
                UiDispatcher.Post(() => executed.Add(100));
                UiDispatcher.Post(() => executed.Add(200));
                Assert.Equal(new[] { 1, 100, 1, 200 }, executed);
            }
            finally
            {
                UiDispatcher.Runner = null!; // 恢复默认，避免污染其他测试
            }
        }

        [Fact]
        public void Post_注入Runner抛异常时动作未执行()
        {
            var ran = false;
            UiDispatcher.Runner = _ => throw new InvalidOperationException("封送失败");
            try
            {
                Assert.Throws<InvalidOperationException>(() => UiDispatcher.Post(() => ran = true));
                Assert.False(ran);
            }
            finally
            {
                UiDispatcher.Runner = null!;
            }
        }
    }
}
