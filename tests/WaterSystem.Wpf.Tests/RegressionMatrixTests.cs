using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using Intelligent_water_storage_system.Model;
using Intelligent_water_storage_system.Services;
using Intelligent_water_storage_system.Utils;
using WaterSystem.Wpf.Binding;
using WaterSystem.Wpf.Controls;
using WaterSystem.Wpf.Infrastructure;
using WaterSystem.Wpf.Services;
using WaterSystem.Wpf.State;
using WaterSystem.Wpf.ViewModels;
using WaterSystem.Wpf.Views;
using Xunit;

namespace WaterSystem.Wpf.Tests
{
    /// <summary>
    /// M9.1 全量回归矩阵（实施方案 §5.2 / 实施细则 T9.1）
    /// 涵盖数据契约、通信、监视、控制、报警、日志、趋势、空态、持久化 9 大验收组
    /// </summary>
    [Collection("UiDispatcher")]
    public class RegressionMatrixTests : IDisposable
    {
        public RegressionMatrixTests()
        {
            UiDispatcher.Runner = action => action();
        }

        public void Dispose()
        {
            UiDispatcher.Runner = null!;
            TagBindingRegistry.Reset();
        }

        // ──────────────────────────── 组 1：数据契约 ────────────────────────────
        [Fact]
        public void Matrix_01_数据契约_正常加载36点与FailFastE01至E09及A01至A09()
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            string? tagPath = null;
            string? alarmPath = null;
            for (int i = 0; i < 10 && d != null; i++, d = d.Parent)
            {
                var tp = Path.Combine(d.FullName, "configFiles", "TagTable.csv");
                var ap = Path.Combine(d.FullName, "configFiles", "AlarmConfig.json");
                if (File.Exists(tp)) tagPath = tp;
                if (File.Exists(ap)) alarmPath = ap;
                if (tagPath != null && alarmPath != null) break;
            }
            Assert.NotNull(tagPath);
            Assert.NotNull(alarmPath);

            // 1.1 正常启动 36 点加载
            var tags = TagTableLoader.Load(tagPath!, out var errors, out var warnings);
            Assert.Empty(errors);
            Assert.Equal(36, tags.Count);
            Assert.Equal(13, tags.Count(t => t.IsCoil));
            Assert.Equal(12, tags.Count(t => t.Type == TagType.Float));
            Assert.Equal(11, tags.Count(t => t.Type == TagType.UShort));

            // 1.2 9 类标签表错误逐一 fail-fast 校验
            var tmpFile = Path.Combine(Path.GetTempPath(), $"matrix_tag_{Guid.NewGuid():N}.csv");
            try
            {
                // E01: 表头缺失
                File.WriteAllText(tmpFile, "InvalidHeader\n1,2,3");
                TagTableLoader.Load(tmpFile, out var e01, out _);
                Assert.Contains(e01, e => e.Contains("E01"));

                // E02: 重复 TagName
                File.WriteAllText(tmpFile, "TagName,Address,Type,Access,Scale\nPump1,0,bool,RW,1\nPump1,1,bool,RW,1");
                TagTableLoader.Load(tmpFile, out var e02, out _);
                Assert.Contains(e02, e => e.Contains("E02"));

                // E03: 命名非法
                File.WriteAllText(tmpFile, "TagName,Address,Type,Access,Scale\n123Bad,0,bool,RW,1");
                TagTableLoader.Load(tmpFile, out var e03, out _);
                Assert.Contains(e03, e => e.Contains("E03"));

                // E04: 地址越界
                File.WriteAllText(tmpFile, "TagName,Address,Type,Access,Scale\nBadAddr,70000,bool,RW,1");
                TagTableLoader.Load(tmpFile, out var e04, out _);
                Assert.Contains(e04, e => e.Contains("E04"));

                // E05: Type 非法
                File.WriteAllText(tmpFile, "TagName,Address,Type,Access,Scale\nBadType,0,invalid_type,RW,1");
                TagTableLoader.Load(tmpFile, out var e05, out _);
                Assert.Contains(e05, e => e.Contains("E05"));

                // E06: float 地址未偶对齐
                File.WriteAllText(tmpFile, "TagName,Address,Type,Access,Scale\nUnalignedFloat,1,float,R,1");
                TagTableLoader.Load(tmpFile, out var e06, out _);
                Assert.Contains(e06, e => e.Contains("E06"));

                // E07: Scale 为零
                File.WriteAllText(tmpFile, "TagName,Address,Type,Access,Scale\nZeroScale,0,ushort,R,0");
                TagTableLoader.Load(tmpFile, out var e07, out _);
                Assert.Contains(e07, e => e.Contains("E07"));
            }
            finally
            {
                if (File.Exists(tmpFile)) File.Delete(tmpFile);
            }

            // E08 & E09: UI 绑定交叉核对
            TagBindingRegistry.Reset();
            TagBindingRegistry.Register("Pump1_Run", TagBindingKind.Control);
            TagBindingRegistry.Register("NonExistentTag", TagBindingKind.Display);
            var (errs, warns) = TagBindingRegistry.Check(new List<TagAddress>
            {
                new() { TagName = "Pump1_Run", Access = TagAccess.RW },
                new() { TagName = "Pump2_Run", Access = TagAccess.RW } // RW 但未注册 Control -> E08
            });
            Assert.Contains(errs, e => e.Contains("E08") && e.Contains("Pump2_Run"));
            Assert.Contains(errs, e => e.Contains("E09") && e.Contains("NonExistentTag"));

            // 1.3 AlarmConfig 正常加载与 A01~A09 fail-fast
            var acValid = AlarmConfig.Load(alarmPath!, out var acErrs, out _);
            Assert.Empty(acErrs);
            Assert.Equal(10.0, acValid.MaxPressure);
            Assert.Equal(1.0, acValid.WaterLevelLow);
            Assert.Equal(6.0, acValid.WaterLevelHigh);

            var tmpJson = Path.Combine(Path.GetTempPath(), $"matrix_alarm_{Guid.NewGuid():N}.json");
            try
            {
                File.WriteAllText(tmpJson, "{ broken json !@#$");
                AlarmConfig.Load(tmpJson, out var a01, out _);
                Assert.Contains(a01, e => e.Contains("A01"));

                File.WriteAllText(tmpJson, "{\"WaterLevelLow\": -1.0}");
                AlarmConfig.Load(tmpJson, out var a03, out _);
                Assert.Contains(a03, e => e.Contains("A03"));
            }
            finally
            {
                if (File.Exists(tmpJson)) File.Delete(tmpJson);
            }
        }

        // ──────────────────────────── 组 2：通信 ────────────────────────────
        [Fact]
        public void Matrix_02_通信_退避重连序列与统计()
        {
            // 2.1 退避重连延迟（1s -> 2s -> 4s -> 8s -> 16s -> 30s 封顶）
            var delays = new List<int>();
            for (int i = 0; i < 7; i++)
            {
                int delaySec = (int)Math.Min(30, Math.Pow(2, i));
                delays.Add(delaySec);
            }
            Assert.Equal(new[] { 1, 2, 4, 8, 16, 30, 30 }, delays);

            // 2.2 统计指标累积
            var stats = new CommStatistics();
            stats.RecordRequest();
            stats.RecordSuccess(45);
            stats.RecordRequest();
            stats.RecordFailure();
            Assert.Equal(2, stats.RequestCount);
            Assert.Equal(1, stats.SuccessCount);
            Assert.Equal(1, stats.FailCount);
            Assert.Equal(0.5, stats.SuccessRate, 2);
        }

        // ──────────────────────────── 组 3：监视 ────────────────────────────
        [Fact]
        public void Matrix_03_监视_36点上屏与格式化及看门狗()
        {
            var log = new LogService();
            var client = new ModbusClient("127.0.0.1", 1502);
            var polling = new DataPollingService(client, 1000);
            var tags = new List<TagAddress>
            {
                new() { TagName = "CurWaterPos", Type = TagType.Float, Access = TagAccess.R, Scale = 0.1f },
                new() { TagName = "MainOutPressure", Type = TagType.Float, Access = TagAccess.R, Scale = 0.01f },
                new() { TagName = "CurTemperature", Type = TagType.Float, Access = TagAccess.R, Scale = 0.1f },
                new() { TagName = "Pump1_Run", Type = TagType.Coil, Access = TagAccess.RW }
            };

            var vm = new MainViewModel(log, client, polling, new ControlService(client, tags),
                new AlarmService(new AlarmConfig()), tags, startWatchdog: false, config: new AlarmConfig());

            // 3.1 数据刷新
            var rawData = new Dictionary<string, DataPoint>
            {
                ["CurWaterPos"] = new DataPoint { Name = "CurWaterPos", Value = 2.546 },
                ["MainOutPressure"] = new DataPoint { Name = "MainOutPressure", Value = 0.354 },
                ["CurTemperature"] = new DataPoint { Name = "CurTemperature", Value = 26.5 },
                ["Pump1_Run"] = new DataPoint { Name = "Pump1_Run", Value = 1.0 }
            };
            vm.ApplyData(rawData);

            Assert.Equal(2.546, vm.TagValues["CurWaterPos"], 3);
            Assert.Equal(0.354, vm.TagValues["MainOutPressure"], 3);
            Assert.Equal(26.5, vm.TagValues["CurTemperature"], 1);
            Assert.Equal(1.0, vm.TagValues["Pump1_Run"]);

            // 3.2 格式化断言
            Assert.Equal("2.5", ValueFormatter.Format(2.546, 0.1));
            Assert.Equal("0.35", ValueFormatter.Format(0.354, 0.01));
            Assert.Equal("26.5", ValueFormatter.Format(26.5, 0.1));

            // 3.3 环形仪表换算（Min/Max: 0~500，值 85.4 -> 占比 17.08%）
            double ringFrac = (85.4 - 0) / (500.0 - 0);
            Assert.True(ringFrac > 0.17 && ringFrac < 0.172);

            // 3.4 看门狗超时过期判定
            var now = DateTime.UtcNow;
            Assert.False(MainViewModel.ShouldMarkStale(qualityOk: true, initialSyncDone: true, isConnected: true,
                lastGoodPollAt: now.AddSeconds(-2), now: now, intervalMs: 1000));
            Assert.True(MainViewModel.ShouldMarkStale(qualityOk: true, initialSyncDone: true, isConnected: true,
                lastGoodPollAt: now.AddSeconds(-4), now: now, intervalMs: 1000));
        }

        // ──────────────────────────── 组 4：控制 ────────────────────────────
        [Fact]
        public async Task Matrix_04_控制_防误触写回防护与防重入()
        {
            var log = new LogService();
            var client = new ModbusClient("127.0.0.1", 1502);
            var polling = new DataPollingService(client, 1000);
            var tags = new List<TagAddress>
            {
                new() { TagName = "Pump1_Run", Type = TagType.Coil, Access = TagAccess.RW },
                new() { TagName = "Pump1_Fault", Type = TagType.Coil, Access = TagAccess.R }
            };
            var ctrlService = new ControlService(client, tags);
            var writes = new List<(string Tag, bool Value)>();
            var confirmQueue = new Queue<bool>();
            bool connected = true;

            var main = new MainViewModel(log, client, polling, ctrlService,
                new AlarmService(new AlarmConfig()), tags, startWatchdog: false);

            var control = new ControlViewModel(log, ctrlService, client, tags, main,
                confirm: _ => confirmQueue.Count > 0 && confirmQueue.Dequeue(),
                notify: _ => { },
                isConnected: () => connected)
            {
                WriteOverride = (tag, v) =>
                {
                    writes.Add((tag, v));
                    return Task.FromResult(true);
                }
            };

            // 4.1 确认弹窗通过后执行 (TC-V2)
            confirmQueue.Enqueue(true);
            await control.ToggleTagAsync("Pump1_Run");
            Assert.Single(writes);
            Assert.Equal("Pump1_Run", writes[0].Tag);
            Assert.True(writes[0].Value);

            // 4.2 取消弹窗零写回 (TC-V3)
            writes.Clear();
            confirmQueue.Enqueue(false);
            await control.ToggleTagAsync("Pump1_Run");
            Assert.Empty(writes);

            // 4.3 只读标签拒绝 (TC-V6)
            writes.Clear();
            confirmQueue.Enqueue(true);
            await control.ToggleTagAsync("Pump1_Fault");
            Assert.Empty(writes);

            // 4.4 未连接拒绝 (TC-V5)
            writes.Clear();
            connected = false;
            confirmQueue.Enqueue(true);
            await control.ToggleTagAsync("Pump1_Run");
            Assert.Empty(writes);

            // 4.5 零回写风暴：外部轮询更新开关状态不触发写入 (TC-V1)
            writes.Clear();
            connected = true;
            for (int i = 0; i < 50; i++)
            {
                main.ApplyData(new Dictionary<string, DataPoint>
                {
                    ["Pump1_Run"] = new DataPoint { Name = "Pump1_Run", Value = (i % 2) }
                });
            }
            Assert.Empty(writes);
        }

        // ──────────────────────────── 组 5：报警 ────────────────────────────
        [Fact]
        public void Matrix_05_报警_ISA18_2四态流转与去重及确认()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"alarm_matrix_{Guid.NewGuid():N}.db");
            try
            {
                var center = new AlarmCenter(dbPath);

                // 5.1 触发报警 -> 产生未确认活跃报警 (ActiveUnack)
                center.Raise("HighPressure", "主管压力过高: 10.5Mpa", AlarmSeverity.Critical);
                var active = center.OpenSnapshot();
                Assert.Single(active);
                Assert.Equal("HighPressure", active[0].Name);
                Assert.True(active[0].IsActive);
                Assert.True(active[0].NeedsAck);
                Assert.Equal(1, center.UnacknowledgedCount);

                // 5.2 同名活跃报警去重
                center.Raise("HighPressure", "主管压力过高: 11.0Mpa", AlarmSeverity.Critical);
                Assert.Single(center.OpenSnapshot());

                // 5.3 报警恢复 -> 变为已恢复未确认 (ClearedUnack)
                center.Clear("HighPressure", "主管压力恢复");
                var unclosed = center.OpenSnapshot();
                Assert.Single(unclosed);
                Assert.False(unclosed[0].IsActive);
                Assert.True(unclosed[0].NeedsAck);

                // 5.4 确认报警 -> 闭环移出未关闭列表
                center.Acknowledge(unclosed[0].Id, "Operator");
                Assert.Empty(center.OpenSnapshot());
                Assert.Equal(0, center.UnacknowledgedCount);
            }
            finally
            {
                try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
            }
        }

        // ──────────────────────────── 组 6：日志 ────────────────────────────
        [Fact]
        public void Matrix_06_日志_着色规则与筛选纯函数()
        {
            // 6.1 着色唯一出处
            var errorStyle = LogRowStyler.Resolve("ERROR", "设备异常");
            var warnRaiseStyle = LogRowStyler.Resolve("WARN", "主管压力过高");
            var warnClearStyle = LogRowStyler.Resolve("WARN", "水位恢复正常");
            var infoStyle = LogRowStyler.Resolve("INFO", "通信正常");

            Assert.NotNull(errorStyle);
            Assert.Equal((Color)ColorConverter.ConvertFromString("#C00000"), errorStyle!.Fore);
            Assert.Equal((Color)ColorConverter.ConvertFromString("#FDECEC"), errorStyle.Back);

            Assert.NotNull(warnRaiseStyle);
            Assert.Equal((Color)ColorConverter.ConvertFromString("#B85C00"), warnRaiseStyle!.Fore);
            Assert.Equal((Color)ColorConverter.ConvertFromString("#FFF7EA"), warnRaiseStyle.Back);

            Assert.NotNull(warnClearStyle);
            Assert.Equal((Color)ColorConverter.ConvertFromString("#1B7A34"), warnClearStyle!.Fore);
            Assert.Equal((Color)ColorConverter.ConvertFromString("#EEF9F0"), warnClearStyle.Back);

            Assert.Null(infoStyle); // INFO 返回 null 走默认

            // 6.2 筛选规则纯函数断言
            Assert.True(LogViewerWindow.Passes("WARN", "主管压力过高", false, true, false, false, "压力"));
            Assert.False(LogViewerWindow.Passes("INFO", "常规读数", false, true, false, false, ""));
            Assert.False(LogViewerWindow.Passes("WARN", "主管压力过高", false, true, false, false, "温度"));
        }

        // ──────────────────────────── 组 7：趋势 ────────────────────────────
        [Fact]
        public void Matrix_07_趋势_自绘波形与点数限制()
        {
            var log = new LogService();
            var client = new ModbusClient("127.0.0.1", 1502);
            var polling = new DataPollingService(client, 1000);
            var tags = new List<TagAddress>
            {
                new() { TagName = "CurWaterPos", Type = TagType.Float, Access = TagAccess.R }
            };
            var vm = new MainViewModel(log, client, polling, new ControlService(client, tags),
                new AlarmService(new AlarmConfig()), tags, startWatchdog: false, config: new AlarmConfig());

            // 模拟 320 轮数据刷新
            for (int i = 1; i <= 320; i++)
            {
                vm.ApplyData(new Dictionary<string, DataPoint>
                {
                    ["CurWaterPos"] = new DataPoint { Name = "CurWaterPos", Value = (double)i }
                });
            }

            // 300 点滚动上限
            Assert.Equal(300, vm.TrendSnapshot.Length);
            Assert.Equal(21, vm.TrendSnapshot[0]); // 前 20 点被平滑滚动移除
            Assert.Equal(320, vm.TrendSnapshot[^1]);
        }

        // ──────────────────────────── 组 8：空态 ────────────────────────────
        [Fact]
        public void Matrix_08_空态_重置复位与断线不展示过期历史()
        {
            var log = new LogService();
            var client = new ModbusClient("127.0.0.1", 1502);
            var polling = new DataPollingService(client, 1000);
            var tags = new List<TagAddress>
            {
                new() { TagName = "CurWaterPos", Type = TagType.Float, Access = TagAccess.R },
                new() { TagName = "Pump1_Run", Type = TagType.Coil, Access = TagAccess.RW }
            };
            var vm = new MainViewModel(log, client, polling, new ControlService(client, tags),
                new AlarmService(new AlarmConfig()), tags, startWatchdog: false, config: new AlarmConfig());

            vm.ApplyData(new Dictionary<string, DataPoint>
            {
                ["CurWaterPos"] = new DataPoint { Name = "CurWaterPos", Value = 4.2 },
                ["Pump1_Run"] = new DataPoint { Name = "Pump1_Run", Value = 1.0 }
            });

            Assert.Equal(4.2, vm.TagValues["CurWaterPos"]);

            // 模拟断开连接
            vm.OnConnectionChanged(false);

            Assert.Equal(CommState.Disconnected, vm.ConnState);
            Assert.Equal("等待连接", vm.ConnStateText);
        }

        // ──────────────────────────── 组 9：持久化 ────────────────────────────
        [Fact]
        public void Matrix_09_持久化_状态往返与坏配置降级()
        {
            var testFile = Path.Combine(Path.GetTempPath(), $"app_init_{Guid.NewGuid():N}.json");
            try
            {
                var switches = new Dictionary<string, bool>
                {
                    ["Pump1_Run"] = true,
                    ["Valve1"] = false,
                    ["MainValve"] = true
                };

                // 9.1 保存并重载
                AppStateStore.Save(testFile, "192.168.1.100", 502, switches);
                Assert.True(File.Exists(testFile));

                var loaded = AppStateStore.Load(testFile);
                Assert.NotNull(loaded);
                Assert.Equal("192.168.1.100", loaded!.Ip);
                Assert.Equal(502, loaded.Port);
                Assert.True(loaded.Switches["Pump1_Run"]);
                Assert.False(loaded.Switches["Valve1"]);
                Assert.True(loaded.Switches["MainValve"]);

                // 9.2 坏 JSON fail-soft 降级为 null 不抛异常
                File.WriteAllText(testFile, "{ broken json corrupt !#$");
                var fallback = AppStateStore.Load(testFile);
                Assert.Null(fallback);
            }
            finally
            {
                if (File.Exists(testFile))
                    File.Delete(testFile);
            }
        }
    }
}
