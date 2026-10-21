using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Intelligent_water_storage_system.Model;
using Intelligent_water_storage_system.Services;
using WaterSystem.Wpf.ViewModels;
using WaterSystem.Wpf.Views;
using Xunit;

namespace WaterSystem.Wpf.Tests
{
    public class PanelScreenshotTests
    {
        private static string GetDocsDir()
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null)
            {
                if (Directory.Exists(Path.Combine(d.FullName, "docs")) &&
                    File.Exists(Path.Combine(d.FullName, "configFiles", "TagTable.csv")))
                {
                    return Path.Combine(d.FullName, "docs");
                }
                d = d.Parent;
            }
            throw new InvalidOperationException("未找到 docs 目录");
        }

        private static void EnsureAppResources()
        {
            if (Application.Current == null)
            {
                var app = new Application();
                try
                {
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("pack://application:,,,/HandyControl;component/Themes/SkinDefault.xaml", UriKind.Absolute)
                    });
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("pack://application:,,,/HandyControl;component/Themes/Theme.xaml", UriKind.Absolute)
                    });
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("pack://application:,,,/WaterSystem.Wpf;component/Themes/Dark.xaml", UriKind.Absolute)
                    });
                }
                catch
                {
                }
            }
        }

        private static void RenderElementToPng(FrameworkElement element, string outputPath, int width, int height, Brush? bg = null)
        {
            element.Width = width;
            element.Height = height;

            var container = new Border
            {
                Width = width,
                Height = height,
                Background = bg ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F172A")),
                Child = element
            };

            container.Measure(new Size(width, height));
            container.Arrange(new Rect(0, 0, width, height));
            container.UpdateLayout();

            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(container);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(outputPath);
            encoder.Save(fs);
        }

        [Fact]
        public void Generate_LogViewer_Screenshot()
        {
            string docsDir = GetDocsDir();
            string pngPath = Path.Combine(docsDir, "panel-log-viewer.png");
            string dbPath = Path.Combine(Path.GetTempPath(), $"test_log_{Guid.NewGuid():N}.db");

            var t = new Thread(() =>
            {
                EnsureAppResources();
                var log = new LogService();
                var center = new AlarmCenter(dbPath);

                log.Info("会话状态已恢复 (app_init.json)");
                log.Info("正在连接 PLC (127.0.0.1:1502) ...");
                log.Info("PLC 连接成功，协议 Modbus TCP，通信状态: Connected");
                log.Info("首包数据同步对齐：36 个寄存器/线圈点位契约校验通过");
                log.Warn("[W01] 标签 \"Pressure-1\" 在界面中无绑定控件 (如为预留点可忽略)");
                log.Warn("1# 支管供水压力接近高限警戒值: 0.85 Mpa");
                log.Error("2# 水泵电气回路发生过载保护 (E04)，变频器已停机");
                log.Warn("2# 水泵电气故障: Pump2Fault 已触发 Critical 级报警");
                log.Info("3# 备用泵已手动投入运行");
                log.Info("主进水阀门开启到位，下管廊汇管流量平稳输出");
                log.Info("2# 水泵电气保护已复位，状态恢复正常");

                var win = new LogViewerWindow(log, center);
                if (win.Content is Grid g)
                {
                    if (g.Children[1] is DataGrid dg)
                    {
                        dg.Columns[2].Width = new DataGridLength(760);
                    }
                    win.Content = null;
                    RenderElementToPng(g, pngPath, 960, 520, (Brush)Application.Current.FindResource("BgWindow"));
                }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            Assert.True(t.Join(10_000));
            Assert.True(File.Exists(pngPath), "panel-log-viewer.png 生成失败");

            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
        }

        [Fact]
        public void Generate_ConfirmDialog_Screenshot()
        {
            string docsDir = GetDocsDir();
            string pngPath = Path.Combine(docsDir, "panel-confirm-dialog.png");

            var t = new Thread(() =>
            {
                EnsureAppResources();
                var req = new ConfirmRequest(
                    "即将向现场 PLC 下发水泵启停指令，是否确认执行？",
                    "Pump1_Run",
                    "1# 水泵运行与启停控制",
                    "停止 (OFF)",
                    "启动 (ON)",
                    "FC05 写线圈 0x0000 = 0xFF00");

                var win = new ConfirmDialog(req);
                if (win.Content is FrameworkElement fe)
                {
                    win.Content = null;
                    var bg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#131C2E"));
                    RenderElementToPng(fe, pngPath, 560, 160, bg);
                }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            Assert.True(t.Join(10_000));
            Assert.True(File.Exists(pngPath), "panel-confirm-dialog.png 生成失败");
        }

        [Fact]
        public void Generate_SystemInfo_Screenshot()
        {
            string docsDir = GetDocsDir();
            string pngPath = Path.Combine(docsDir, "panel-system-info.png");

            var t = new Thread(() =>
            {
                EnsureAppResources();
                var win = new SystemInfoWindow();
                if (win.Content is FrameworkElement fe)
                {
                    win.Content = null;
                    var bg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0B1220"));
                    RenderElementToPng(fe, pngPath, 760, 580, bg);
                }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            Assert.True(t.Join(10_000));
            Assert.True(File.Exists(pngPath), "panel-system-info.png 生成失败");
        }
    }
}
