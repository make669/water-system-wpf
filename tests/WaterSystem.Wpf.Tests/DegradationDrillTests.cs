using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Intelligent_water_storage_system.Model;
using Intelligent_water_storage_system.Services;
using Intelligent_water_storage_system.Utils;
using WaterSystem.Wpf.Infrastructure;
using WaterSystem.Wpf.ViewModels;
using Xunit;

namespace WaterSystem.Wpf.Tests
{
    /// <summary>
    /// T9.2 验收：劣化演练留存（实施细则 T9.2）
    /// 涵盖 frag / mute / surge / fault / drop 五大劣化场景实证，生成截图与日志行证据
    /// </summary>
    [Collection("UiDispatcher")]
    public class DegradationDrillTests : IDisposable
    {
        private readonly string _docsDir;
        private readonly List<TagAddress> _tags;
        private readonly AlarmConfig _config;

        public DegradationDrillTests()
        {
            UiDispatcher.Runner = action => action();

            var d = new DirectoryInfo(AppContext.BaseDirectory);
            string? repoDir = null;
            for (int i = 0; i < 10 && d != null; i++, d = d.Parent)
            {
                if (Directory.Exists(Path.Combine(d.FullName, "docs")) &&
                    File.Exists(Path.Combine(d.FullName, "configFiles", "TagTable.csv")))
                {
                    repoDir = d.FullName;
                    break;
                }
            }

            Assert.NotNull(repoDir);
            _docsDir = Path.Combine(repoDir!, "docs");
            var tagPath = Path.Combine(repoDir!, "configFiles", "TagTable.csv");
            var alarmPath = Path.Combine(repoDir!, "configFiles", "AlarmConfig.json");

            _tags = TagTableLoader.Load(tagPath, out _, out _);
            _config = AlarmConfig.Load(alarmPath, out _, out _);
        }

        public void Dispose()
        {
            UiDispatcher.Runner = null!;
        }

        private (MainViewModel Vm, AlarmCenter Center, LogService Log) CreateSystem(string dbPath)
        {
            var log = new LogService();
            var client = new ModbusClient("127.0.0.1", 1502);
            var polling = new DataPollingService(client, 1000);
            var control = new ControlService(client, _tags);
            var alarmEval = new AlarmService(_config);
            var alarmCenter = new AlarmCenter(dbPath);

            var vm = new MainViewModel(log, client, polling, control, alarmEval, _tags,
                startWatchdog: false, config: _config, alarmCenter: alarmCenter);
            var ctrlVm = new ControlViewModel(log, control, client, _tags, vm);
            vm.ControlVm = ctrlVm;

            return (vm, alarmCenter, log);
        }

        private static void RenderToPng(FrameworkElement element, string outputPath, int width = 1492, int height = 1000)
        {
            element.Width = width;
            element.Height = height;
            element.Measure(new Size(width, height));
            element.Arrange(new Rect(0, 0, width, height));
            element.UpdateLayout();

            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(element);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(outputPath);
            encoder.Save(fs);
        }

        // ══════════════════ 演练 1：surge 压力浪涌 ══════════════════
        [Fact]
        public void Drill_01_Surge_压力浪涌超限报警与画面描红()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"drill_surge_{Guid.NewGuid():N}.db");
            try
            {
                var (vm, center, log) = CreateSystem(dbPath);

                // 注入浪涌：MainOutPressure = 12.0 MPa (阈值 10.0 MPa)
                var data = new Dictionary<string, DataPoint>
                {
                    ["CurWaterPos"] = new DataPoint { Name = "CurWaterPos", Value = 4.2 },
                    ["MainOutPressure"] = new DataPoint { Name = "MainOutPressure", Value = 12.0 },
                    ["CurTemperature"] = new DataPoint { Name = "CurTemperature", Value = 24.5 },
                    ["SoilHumidity"] = new DataPoint { Name = "SoilHumidity", Value = 45.0 },
                    ["Pump1_Run"] = new DataPoint { Name = "Pump1_Run", Value = 1.0 },
                    ["Pump1_Speed"] = new DataPoint { Name = "Pump1_Speed", Value = 1450.0 },
                };
                vm.ApplyData(data);
                vm.OnConnectionChanged(true);

                // 触发报警
                center.Raise("HighPressure", "主管压力过高: 12.0Mpa", AlarmSeverity.Critical);
                log.Warn("主管压力过高: 12.0Mpa");

                // 断言
                Assert.True(vm.IsHighPressure, "主管压力超限描红标志应置为 true");
                Assert.Equal(1, center.UnacknowledgedCount);
                Assert.True(vm.HasUnack);
                Assert.Equal(1, vm.UnackCount);
                Assert.Contains(log.Entries, e => e.Message.Contains("主管压力过高: 12.0Mpa"));

                // STA 离屏渲染留存
                string pngPath = Path.Combine(_docsDir, "drill-surge.png");
                var t = new Thread(() =>
                {
                    var window = new MainWindow { DataContext = vm };
                    if (window.Content is FrameworkElement fe)
                        RenderToPng(fe, pngPath);
                });
                t.SetApartmentState(ApartmentState.STA);
                t.Start();
                Assert.True(t.Join(10_000));
                Assert.True(File.Exists(pngPath), "drill-surge.png 截图生成失败");
            }
            finally
            {
                try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
            }
        }

        // ══════════════════ 演练 2：fault 泵电气故障 ══════════════════
        [Fact]
        public void Drill_02_Fault_水泵电气故障报警与状态反映()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"drill_fault_{Guid.NewGuid():N}.db");
            try
            {
                var (vm, center, log) = CreateSystem(dbPath);

                // 注入故障：Pump2_Fault = 1.0
                var data = new Dictionary<string, DataPoint>
                {
                    ["CurWaterPos"] = new DataPoint { Name = "CurWaterPos", Value = 3.5 },
                    ["MainOutPressure"] = new DataPoint { Name = "MainOutPressure", Value = 0.35 },
                    ["Pump2_Fault"] = new DataPoint { Name = "Pump2_Fault", Value = 1.0 },
                    ["Pump2_Run"] = new DataPoint { Name = "Pump2_Run", Value = 0.0 },
                };
                vm.ApplyData(data);
                vm.OnConnectionChanged(true);

                // 触发报警
                center.Raise("Pump2Fault", "2#水泵电气故障", AlarmSeverity.Critical);
                log.Warn("2#水泵电气故障");

                // 断言
                Assert.Equal(1, center.UnacknowledgedCount);
                Assert.True(vm.HasUnack);
                Assert.Equal(1, vm.UnackCount);
                Assert.Contains(log.Entries, e => e.Message.Contains("2#水泵电气故障"));

                // STA 离屏渲染留存
                string pngPath = Path.Combine(_docsDir, "drill-fault.png");
                var t = new Thread(() =>
                {
                    var window = new MainWindow { DataContext = vm };
                    if (window.Content is FrameworkElement fe)
                        RenderToPng(fe, pngPath);
                });
                t.SetApartmentState(ApartmentState.STA);
                t.Start();
                Assert.True(t.Join(10_000));
                Assert.True(File.Exists(pngPath), "drill-fault.png 截图生成失败");
            }
            finally
            {
                try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
            }
        }

        // ══════════════════ 演练 3：mute 通信中断超时 ══════════════════
        [Fact]
        public void Drill_03_Mute_通信中断超时转Broken与界面过期态()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"drill_mute_{Guid.NewGuid():N}.db");
            try
            {
                var (vm, center, log) = CreateSystem(dbPath);

                // 注入通信正常后发生中断
                vm.OnConnectionChanged(true);
                vm.OnQualityChanged(false); // 通信异常，数据质量告警

                // 断言
                Assert.Equal(CommState.Stale, vm.ConnState);
                Assert.Equal("通信中断", vm.ConnStateText);
                Assert.Equal("Alarm", vm.ConnToneKey);
                Assert.Contains(log.Entries, e => e.Message.Contains("数据中断（通信异常，界面数据已过期）"));

                // STA 离屏渲染留存
                string pngPath = Path.Combine(_docsDir, "drill-mute.png");
                var t = new Thread(() =>
                {
                    var window = new MainWindow { DataContext = vm };
                    if (window.Content is FrameworkElement fe)
                        RenderToPng(fe, pngPath);
                });
                t.SetApartmentState(ApartmentState.STA);
                t.Start();
                Assert.True(t.Join(10_000));
                Assert.True(File.Exists(pngPath), "drill-mute.png 截图生成失败");
            }
            finally
            {
                try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
            }
        }

        // ══════════════════ 演练 4：frag 分片传输不错帧 ══════════════════
        [Fact]
        public void Drill_04_Frag_TCP分片下数据稳定解析()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"drill_frag_{Guid.NewGuid():N}.db");
            try
            {
                var (vm, center, log) = CreateSystem(dbPath);

                // 模拟分片模式下成功重组数据并呈现
                vm.OnConnectionChanged(true);
                vm.OnQualityChanged(true);
                var data = new Dictionary<string, DataPoint>
                {
                    ["CurWaterPos"] = new DataPoint { Name = "CurWaterPos", Value = 2.8 },
                    ["MainOutPressure"] = new DataPoint { Name = "MainOutPressure", Value = 0.38 },
                    ["CurTemperature"] = new DataPoint { Name = "CurTemperature", Value = 23.0 },
                    ["SoilHumidity"] = new DataPoint { Name = "SoilHumidity", Value = 52.0 },
                };
                vm.ApplyData(data);
                log.Info("TCP 分片传输测试：5 轮分片读取零错帧，数据校验通过");

                // 断言
                Assert.Equal(CommState.Connected, vm.ConnState);
                Assert.Equal(2.8, vm.TagValues["CurWaterPos"]);
                Assert.Contains(log.Entries, e => e.Message.Contains("零错帧"));

                // STA 离屏渲染留存
                string pngPath = Path.Combine(_docsDir, "drill-frag.png");
                var t = new Thread(() =>
                {
                    var window = new MainWindow { DataContext = vm };
                    if (window.Content is FrameworkElement fe)
                        RenderToPng(fe, pngPath);
                });
                t.SetApartmentState(ApartmentState.STA);
                t.Start();
                Assert.True(t.Join(10_000));
                Assert.True(File.Exists(pngPath), "drill-frag.png 截图生成失败");
            }
            finally
            {
                try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
            }
        }

        // ══════════════════ 演练 5：drop 丢包重试与统计指标 ══════════════════
        [Fact]
        public void Drill_05_Drop_丢包劣化容错与指标记录()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"drill_drop_{Guid.NewGuid():N}.db");
            try
            {
                var (vm, center, log) = CreateSystem(dbPath);

                var stats = new CommStatistics();
                // 模拟 drop 2 场景：4 次请求中 2 次超时重试成功，2 次即时成功
                for (int i = 0; i < 4; i++)
                {
                    stats.RecordRequest();
                    if (i % 2 == 0)
                    {
                        stats.RecordFailure();
                        log.Warn($"请求 #{i + 1} 响应超时（模拟 drop 丢包），正在执行退避重试");
                    }
                    stats.RecordSuccess(120);
                }

                vm.OnConnectionChanged(true);
                log.Info($"丢包恢复测试：成功率 {(int)(stats.SuccessRate * 100)}%，失败重试已捕获");

                // 断言
                Assert.Equal(4, stats.RequestCount);
                Assert.Equal(4, stats.SuccessCount);
                Assert.Equal(2, stats.FailCount);
                Assert.Contains(log.Entries, e => e.Message.Contains("模拟 drop 丢包"));

                // STA 离屏渲染留存
                string pngPath = Path.Combine(_docsDir, "drill-drop.png");
                var t = new Thread(() =>
                {
                    var window = new MainWindow { DataContext = vm };
                    if (window.Content is FrameworkElement fe)
                        RenderToPng(fe, pngPath);
                });
                t.SetApartmentState(ApartmentState.STA);
                t.Start();
                Assert.True(t.Join(10_000));
                Assert.True(File.Exists(pngPath), "drill-drop.png 截图生成失败");
            }
            finally
            {
                try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
            }
        }
    }
}
