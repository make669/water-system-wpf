using System;
using System.Linq;
using System.Threading;
using System.Windows.Controls;
using WaterSystem.Wpf.Views;
using Xunit;

namespace WaterSystem.Wpf.Tests
{
    /// <summary>
    /// T4.10 验收：系统说明窗（顶栏 ⓘ 入口，设计书 §1.0 自解释原则 4 问逐条详答，36 点契约摘要，Modbus TCP 通信）。
    /// </summary>
    [Collection("UiDispatcher")]
    public class SystemInfoWindowTests
    {
        [Fact]
        public void SystemInfoWindow_结构与自解释四问内容完整()
        {
            Exception? error = null;
            var t = new Thread(() =>
            {
                try
                {
                    var win = new SystemInfoWindow();
                    Assert.Equal("系统说明与运行信息", win.Title);

                    var mainGrid = Assert.IsType<Grid>(win.Content);
                    var scroll = Assert.IsType<ScrollViewer>(mainGrid.Children[1]);
                    var stack = Assert.IsType<StackPanel>(scroll.Content);

                    // 4 个卡片分区
                    var cards = stack.Children.OfType<Border>().ToList();
                    Assert.Equal(4, cards.Count);

                    var allTextBlocks = stack.Children
                        .OfType<Border>()
                        .SelectMany(b => FindLogicalChildren<TextBlock>(b))
                        .Select(tb => tb.Text)
                        .ToList();

                    // 验收设计书 §1.0 陌生人测试四问逐条回答
                    Assert.Contains(allTextBlocks, t => t.Contains("Q1: 这是什么系统？"));
                    Assert.Contains(allTextBlocks, t => t.Contains("Q2: 监控与操作哪些对象？"));
                    Assert.Contains(allTextBlocks, t => t.Contains("Q3: 现在系统状态正常吗？"));
                    Assert.Contains(allTextBlocks, t => t.Contains("Q4: 水往哪流？颜色分别代表什么意思？"));

                    // 验收 36 点契约与 Modbus TCP 摘要
                    Assert.Contains(allTextBlocks, t => t.Contains("36 个点位"));
                    Assert.Contains(allTextBlocks, t => t.Contains("Modbus TCP"));

                    // 底部按钮
                    var btnPanel = Assert.IsType<StackPanel>(mainGrid.Children[2]);
                    var buttons = btnPanel.Children.OfType<Button>().ToList();
                    Assert.Contains(buttons, b => (string)b.Content == "打开日志目录");
                    Assert.Contains(buttons, b => (string)b.Content == "关闭");
                }
                catch (Exception ex) { error = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            Assert.True(t.Join(10_000), "SystemInfoWindow 构造超时");
            Assert.Null(error);
        }

        private static System.Collections.Generic.IEnumerable<T> FindLogicalChildren<T>(System.Windows.DependencyObject depObj) where T : System.Windows.DependencyObject
        {
            if (depObj == null) yield break;
            foreach (var raw in System.Windows.LogicalTreeHelper.GetChildren(depObj))
            {
                if (raw is System.Windows.DependencyObject child)
                {
                    if (child is T t) yield return t;
                    foreach (var other in FindLogicalChildren<T>(child))
                        yield return other;
                }
            }
        }
    }
}
