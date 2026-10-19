using System;
using System.Linq;
using System.Threading;
using System.Windows.Controls;
using WaterSystem.Wpf.ViewModels;
using Xunit;

namespace WaterSystem.Wpf.Tests
{
    /// <summary>
    /// T4.9 / T5.3 验收：操作与退出确认弹窗（修 P14/P16：默认焦点在取消侧，上下文信息完整）。
    /// </summary>
    [Collection("UiDispatcher")]
    public class ConfirmDialogTests
    {
        [Fact]
        public void ConfirmDialog_控制操作确认_结构与默认取消焦点()
        {
            Exception? error = null;
            var t = new Thread(() =>
            {
                try
                {
                    var req = new ConfirmRequest(
                        Message: "即将启动 1# 水泵",
                        TagName: "Pump1_Run",
                        Description: "1#水泵启停控制",
                        CurrentText: "停止",
                        TargetText: "启动",
                        PlcText: "127.0.0.1:502 已连接");

                    var dlg = new ConfirmDialog(req);
                    Assert.Equal("操作确认", dlg.Title);
                    Assert.False(dlg.Confirmed);

                    var grid = Assert.IsType<Grid>(dlg.Content);
                    var textBlocks = grid.Children.OfType<TextBlock>().ToList();
                    Assert.Contains(textBlocks, tb => tb.Text.Contains("即将启动 1# 水泵"));
                    Assert.Contains(textBlocks, tb => tb.Text.Contains("Pump1_Run") && tb.Text.Contains("停止  →  目标：启动"));

                    var panel = Assert.IsType<StackPanel>(grid.Children[^1]);
                    var buttons = panel.Children.OfType<Button>().ToList();
                    Assert.Equal(2, buttons.Count);

                    var cancelBtn = buttons.First(b => (string)b.Content == "取消");
                    Assert.True(cancelBtn.IsDefault);
                    Assert.True(cancelBtn.IsCancel);

                    var okBtn = buttons.First(b => (string)b.Content == "确认执行");
                    Assert.False(okBtn.IsDefault);
                }
                catch (Exception ex) { error = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            Assert.True(t.Join(10_000), "ConfirmDialog 测试超时");
            Assert.Null(error);
        }

        [Fact]
        public void MainWindow_默认开启退出确认_修P14()
        {
            var t = new Thread(() =>
            {
                var window = new MainWindow();
                Assert.True(window.ConfirmOnClose);
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            Assert.True(t.Join(10_000), "MainWindow 构造超时");
        }
    }
}
