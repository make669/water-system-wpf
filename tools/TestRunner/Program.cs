using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Intelligent_water_storage_system.Model;
using Intelligent_water_storage_system.Services;
using Intelligent_water_storage_system.Utils;

class Program
{
    /// <summary>失败用例计数，非 0 时进程以退出码 1 结束（供验收脚本判定）</summary>
    static int _failures;

    static async Task<int> Main()
    {
        Console.WriteLine("=== TestRunner 验收 ===\n");

        RunTagTableTests();
        await SmokeTests();
        await RunCommTests();
        RunAlarmCenterTests();

        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? "=== 结果：全部通过 ==="
            : $"=== 结果：{_failures} 项失败 ===");
        return _failures == 0 ? 0 : 1;
    }

    // ═══════════════════════════════════════════════
    // 模块 A 验收：TagTable.csv + TagTableLoader + UI 绑定核对
    // 对应 docs/验收标准.md 的 T1.1 / T1.3 / T1.4
    // 【模块 F 后】本段为集成回归快照；规范单测已迁 tests/IntelligentWaterStorage.Tests
    // （自建临时 CSV，不依赖仓库文件），新增用例只进 xUnit，此处不再扩充
    // ═══════════════════════════════════════════════

    static void RunTagTableTests()
    {
        Console.WriteLine("── 模块 A 验收：TagTableLoader ──");

        string? csvPath = FindRepoFile("configFiles", "TagTable.csv");
        Check("定位 TagTable.csv", csvPath != null, "未能在仓库中找到 configFiles/TagTable.csv");
        if (csvPath == null) return;

        // ① 基线：真实标签表必须零错误加载
        var tags = TagTableLoader.Load(csvPath, out var errors, out var warnings);
        Check("基线加载零错误", errors.Count == 0, string.Join(" | ", errors));
        Check("数据点总数 = 36", tags.Count == 36, $"实际 {tags.Count}");
        Check("线圈 13 点", tags.Count(t => t.Type == TagType.Coil) == 13, $"实际 {tags.Count(t => t.Type == TagType.Coil)}");
        Check("float 12 点", tags.Count(t => t.Type == TagType.Float) == 12, $"实际 {tags.Count(t => t.Type == TagType.Float)}");
        Check("ushort 11 点", tags.Count(t => t.Type == TagType.UShort) == 11, $"实际 {tags.Count(t => t.Type == TagType.UShort)}");
        Check("无地址重叠警告", warnings.Count == 0, string.Join(" | ", warnings));
        Check("控制类标签均为 RW",
            tags.Where(t => t.TagName is "Pump1_Run" or "Pump2_Run" or "Pump3_Run" or "MainValve"
                            or "Valve1" or "Valve2" or "Valve3"
                            or "Pump1_Mode" or "Pump2_Mode" or "Pump3_Mode")
                .All(t => t.Access == TagAccess.RW));
        Check("故障标签均为只读",
            tags.Where(t => t.TagName is "Pump1_Fault" or "Pump2_Fault" or "Pump3_Fault")
                .All(t => t.Access == TagAccess.R));

        // ② 逐类错误注入：每种错误都必须被拦截且报出对应错误码
        ExpectReject("E01 表头缺列", csvPath, code: "E01",
            mutate: lines => ReplaceHeader(lines, "TagName,Address,Type"));
        ExpectReject("E01 文件不存在", code: "E01", missingFile: true);
        ExpectReject("E02 TagName 重复", csvPath, code: "E02",
            append: "Pressure-1,40,float,R,0.01,Mpa,1,分管1,重复名称");
        ExpectReject("E03 TagName 命名非法", csvPath, code: "E03",
            append: "9bad,40,ushort,R,1.0,,1,泵1,数字开头非法名");
        ExpectReject("E03 TagName 为空", csvPath, code: "E03",
            append: ",40,ushort,R,1.0,,1,泵1,空名称");
        ExpectReject("E04 地址越界", csvPath, code: "E04",
            append: "BadAddr,70000,ushort,R,1.0,,1,泵1,地址超出65535");
        ExpectReject("E04 地址非数字", csvPath, code: "E04",
            append: "BadAddr2,abc,ushort,R,1.0,,1,泵1,地址不是数字");
        ExpectReject("E05 Type 非法", csvPath, code: "E05",
            append: "BadType,40,abc,R,1.0,,1,泵1,类型不存在");
        ExpectReject("E05 Access 非法", csvPath, code: "E05",
            append: "BadAccess,40,ushort,X,1.0,,1,泵1,权限不存在");
        ExpectReject("E06 float 地址未偶对齐", csvPath, code: "E06",
            append: "BadAlign,51,float,R,0.1,,1,泵1,奇地址浮点");
        ExpectReject("E07 Scale 为零", csvPath, code: "E07",
            append: "BadScale,52,ushort,R,0,,1,泵1,零缩放");
        ExpectReject("E07 Scale 非数字", csvPath, code: "E07",
            append: "BadScale2,53,ushort,R,abc,,1,泵1,缩放不是数字");
        ExpectReject("E04 UnitId 非法", csvPath, code: "E04",
            append: "BadUnit,54,ushort,R,1.0,,300,泵1,UnitId超出255");

        // ③ UI 绑定双向核对（E08/E09/W01）
        var uiErrors = new List<string>();
        var uiWarnings = new List<string>();
        // 控制绑定故意缺大部分 RW 标签 → 触发 E08；绑定一个不存在的标签 → 触发 E09
        TagTableLoader.CheckUiBindings(
            tags,
            controlBindings: new[] { "Pump1_Run" },
            displayBindings: new[] { "Pump1_Run", "CurWaterPos", "NotExistTag" },
            uiErrors, uiWarnings);
        Check("E08 RW 标签无控制控件", uiErrors.Any(e => e.Contains("E08")));
        Check("E09 绑定的标签不存在", uiErrors.Any(e => e.Contains("E09") && e.Contains("NotExistTag")));
        Check("W01 未绑定标签告警", uiWarnings.Any(w => w.Contains("W01") && w.Contains("Pump1_Fault")));

        // ④ UI 绑定核对：控制绑定覆盖全部 RW 标签时应零错误
        var uiErrors2 = new List<string>();
        var uiWarnings2 = new List<string>();
        var rwNames = tags.Where(t => t.Access == TagAccess.RW).Select(t => t.TagName).ToList();
        var allNames = tags.Select(t => t.TagName).ToList();
        TagTableLoader.CheckUiBindings(tags, rwNames, allNames, uiErrors2, uiWarnings2);
        Check("核对通过场景零错误", uiErrors2.Count == 0, string.Join(" | ", uiErrors2));
    }

    /// <summary>构造注入错误的临时 CSV 并断言加载被拒绝且报出指定错误码</summary>
    static void ExpectReject(string name, string? basePath = null, string? code = null,
        string? append = null, Func<List<string>, List<string>>? mutate = null, bool missingFile = false)
    {
        string path;
        if (missingFile)
        {
            path = Path.Combine(Path.GetTempPath(), $"no_such_{Guid.NewGuid():N}.csv");
        }
        else
        {
            var lines = File.ReadAllLines(basePath!).ToList();
            if (append != null) lines.Add(append);
            if (mutate != null) lines = mutate(lines);
            path = Path.Combine(Path.GetTempPath(), $"tagtbl_{Guid.NewGuid():N}.csv");
            File.WriteAllLines(path, lines, new UTF8Encoding(true)); // 与真实表一致：UTF-8 BOM
        }

        try
        {
            var t = TagTableLoader.Load(path, out var errs, out _);
            bool rejected = errs.Any(e => code == null || e.Contains($"[{code}]"));
            Check(name, rejected, rejected ? "" : $"未报出 [{code}]，errors=[{string.Join(" | ", errs)}]");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    static List<string> ReplaceHeader(List<string> lines, string newHeader)
    {
        int idx = lines.FindIndex(l => !l.Trim().StartsWith("#") && l.Trim().Length > 0);
        if (idx >= 0) lines[idx] = newHeader;
        return lines;
    }

    /// <summary>向上查找仓库内的文件（dotnet run 的 CWD 各处不一）</summary>
    static string? FindRepoFile(string dir, string file)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, dir, file),
            Path.Combine(Directory.GetCurrentDirectory(), dir, file),
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return c;

        var d = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
        {
            var p = Path.Combine(d.FullName, dir, file);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    // ═══════════════════════════════════════════════
    // 模块 B 验收：ModbusSlaveSim 通信（T1.5 / T1.6 / T1.7-frag 基线）
    // 自动拉起模拟器进程（端口 1502），逐项验证后收回
    // ═══════════════════════════════════════════════

    static async Task RunCommTests()
    {
        Console.WriteLine("\n── 模块 B 验收：ModbusSlaveSim 通信 ──");

        string? simExe = FindSimExe();
        if (simExe == null)
        {
            Console.WriteLine("[SKIP] 未找到 ModbusSlaveSim.exe，请先 dotnet build 解决方案（本节跳过）");
            return;
        }

        Process? sim = null;
        ModbusClient? client = null;
        try
        {
            var outLines = new ConcurrentQueue<string>();
            sim = StartSim(simExe, outLines);

            // 等待模拟器就绪
            await WaitForLineAsync(outLines, l => l.StartsWith("READY"), 20000);

            client = new ModbusClient("127.0.0.1", 1502);
            Check("连接模拟器", await client.ConnectAsync(3000), client.LastError);

            var tags = TagTableLoader.Load(FindRepoFile("configFiles", "TagTable.csv")!, out _, out _);
            var tag = (string name) => tags.First(t => t.TagName.Equals(name, StringComparison.OrdinalIgnoreCase));

            // ── T1.5-① 全量读取：36 点全部可解码，抽查关键值 ──
            ushort[] regs = await client.ReadHoldingRegistersAsync(1, 0, 38);
            bool[] coils = await client.ReadCoilsAsync(1, 0, 13);
            bool allDecoded = tags.All(t =>
            {
                double eng = t.IsCoil
                    ? (coils[t.Address] ? 1 : 0)
                    : RegisterCodec.Decode(t.Type, regs, t.Address) * t.Scale;
                return !double.IsNaN(eng) && !double.IsInfinity(eng) && eng >= 0;
            });
            Check("36 点全部可解码且非负", allDecoded);
            Check("抽查 CurWaterPos ≈ 2.5m", Math.Abs(await ReadTagAsync(client, tag("CurWaterPos")) - 2.5) <= 0.011);
            Check("抽查 MainOutPressure ≈ 0.35Mpa", Math.Abs(await ReadTagAsync(client, tag("MainOutPressure")) - 0.35) <= 0.005);
            Check("抽查 CurTemperature ≈ 26.5℃", Math.Abs(await ReadTagAsync(client, tag("CurTemperature")) - 26.5) <= 0.011);
            Check("抽查 SoilHumidity = 45%", Math.Abs(await ReadTagAsync(client, tag("SoilHumidity")) - 45) <= 0.001);
            Check("抽查 Pump1_S 停机 = 0", (await ReadTagAsync(client, tag("Pump1_S"))) == 0);
            Check("抽查 Pump1_Run 线圈 OFF", (await ReadTagAsync(client, tag("Pump1_Run"))) == 0);
            Check("抽查 Valve3 线圈 OFF", (await ReadTagAsync(client, tag("Valve3"))) == 0);

            // ── T1.5-② 写线圈（RW）→ 读回确认 ──
            await client.WriteSingleCoilAsync(1, (ushort)tag("Pump1_Run").Address, true);
            Check("写 Pump1_Run=ON 读回 ON", (await ReadTagAsync(client, tag("Pump1_Run"))) == 1);
            await client.WriteSingleCoilAsync(1, (ushort)tag("Pump1_Run").Address, false);
            Check("写 Pump1_Run=OFF 读回 OFF", (await ReadTagAsync(client, tag("Pump1_Run"))) == 0);

            // ── T1.5-③ set 命令 ≤1s 反映到读取 ──
            string setResp = await SendCommandAsync(sim!, outLines, "set CurWaterPos 0.8");
            Check("set 命令返回 OK", setResp.StartsWith("OK"), setResp);
            double afterSet = await ReadTagAsync(client, tag("CurWaterPos"));
            Check("set 0.8m 读取≈0.8", Math.Abs(afterSet - 0.8) <= 0.011, $"实际 {afterSet}");
            await SendCommandAsync(sim!, outLines, "set CurWaterPos 2.5");

            // ── T1.5-④ 只读标签拒绝写入 + 异常响应（原始 Socket 直发，
            //    验证的是模拟器行为；旧 ModbusClient 对 9 字节异常响应会永久等待
            //    12 字节回显而挂起 —— 该缺陷为基线记录，模块 C 修复）──
            try
            {
                var (fc, code, _) = await RawRequestAsync(0x06, (ushort)tag("Pump1_F").Address, 100);
                Check("FC06 写只读寄存器回异常码 0x02", fc == 0x86 && code == 0x02, $"fc=0x{fc:X2} code=0x{code:X2}");
                (fc, code, _) = await RawRequestAsync(0x05, (ushort)tag("Pump1_Fault").Address, 0xFF00);
                Check("FC05 写只读线圈回异常码 0x02", fc == 0x85 && code == 0x02, $"fc=0x{fc:X2} code=0x{code:X2}");
            }
            catch (Exception ex)
            {
                Check("异常响应原始请求", false, ex.Message);
            }

            // ── T1.5-⑤ 非法定义地址读取 / 非法功能码 → 异常响应 ──
            try
            {
                var (fc, code, _) = await RawRequestAsync(0x03, 100, 2);
                Check("读未定义地址报非法地址(0x02)", fc == 0x83 && code == 0x02, $"fc=0x{fc:X2} code=0x{code:X2}");
                (fc, code, _) = await RawRequestAsync(0x07, 0, 0);
                Check("非法功能码报异常(0x01)", fc == 0x87 && code == 0x01, $"fc=0x{fc:X2} code=0x{code:X2}");
            }
            catch (Exception ex)
            {
                Check("异常地址/功能码原始请求", false, ex.Message);
            }

            // ── T1.6-S4 故障注入 ──
            Check("fault 2 on 命令 OK", (await SendCommandAsync(sim!, outLines, "fault 2 on")).StartsWith("OK"));
            Check("Pump2_Fault 读回 ON", (await ReadTagAsync(client, tag("Pump2_Fault"))) == 1);
            await SendCommandAsync(sim!, outLines, "fault 2 off");
            Check("Pump2_Fault 读回 OFF", (await ReadTagAsync(client, tag("Pump2_Fault"))) == 0);

            // ── T1.6-S5 压力浪涌 ──
            await SendCommandAsync(sim!, outLines, "surge on");
            Check("浪涌后压力 >10Mpa", await WaitForTagAsync(client, tag("MainOutPressure"), v => v > 10, 4000));
            await SendCommandAsync(sim!, outLines, "surge off");
            Check("浪涌解除压力恢复", await WaitForTagAsync(client, tag("MainOutPressure"), v => v < 1, 4000));

            // ── T1.6-S2 水位波动 ──
            Check("scenario wave 命令 OK", (await SendCommandAsync(sim!, outLines, "scenario wave")).StartsWith("OK"));
            double w1 = await ReadTagAsync(client, tag("CurWaterPos"));
            await Task.Delay(2500);
            double w2 = await ReadTagAsync(client, tag("CurWaterPos"));
            Check("wave：水位随时间变化", Math.Abs(w2 - w1) > 0.02, $"|{w1:F3} → {w2:F3}|");
            Check("wave：水位在 1.0~4.0m 区间", w1 is > 0.9 and < 4.1 && w2 is > 0.9 and < 4.1);
            await SendCommandAsync(sim!, outLines, "scenario steady");
            Check("steady：水位恢复 2.5m", Math.Abs(await ReadTagAsync(client, tag("CurWaterPos")) - 2.5) <= 0.011);

            // ── T1.6-S3 泵联动（启动 3s 延迟 + 3s 缓升到额定 1450 r/m）──
            await client.WriteSingleCoilAsync(1, (ushort)tag("Pump1_Run").Address, true);
            Check("泵1 启动后转速缓升至额定",
                await WaitForTagAsync(client, tag("Pump1_S"), v => v >= 1400, 20000));
            await client.WriteSingleCoilAsync(1, (ushort)tag("Pump1_Run").Address, false);
            Check("泵1 停止后转速回落",
                await WaitForTagAsync(client, tag("Pump1_S"), v => v <= 72, 20000));

            // ═══════════════════════════════════════════════
            // 模块 C 验收：通信层修复（T1.8 / T1.9 / T1.10）
            // 基线（模块 B 验收时）：frag 下 2/3 次错帧、写异常响应永久挂起
            // ═══════════════════════════════════════════════

            // ── T1.8 frag 分片：两阶段精确读，分片下多次读取必须全部正确 ──
            await SendCommandAsync(sim!, outLines, "frag on");
            int fragFailures = 0;
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    ushort[] r = await client.ReadHoldingRegistersAsync(1, 0, 38);
                    double actual = RegisterCodec.Decode(TagType.Float, r, 36) * 0.1;
                    if (Math.Abs(actual - 85) > 0.11) fragFailures++;
                }
                catch { fragFailures++; }
            }
            Check("frag 分片下 5 次读取全部正确（修复：基线 2/3 错帧）", fragFailures == 0, $"{fragFailures} 次错帧/错值");
            await SendCommandAsync(sim!, outLines, "frag off");
            client.Disconnect();
            Check("frag 关闭并重连后恢复正确",
                await client.ConnectAsync(3000)
                && Math.Abs(await ReadTagAsync(client, tag("ActualWaters")) - 85) <= 0.11);

            // ── T1.8 写异常响应：正确解析 9 字节异常帧并抛中文错误（修复：基线为永久挂起）──
            bool writeRejected = await ThrowsAsync(async () =>
                await client.WriteSingleRegisterAsync(1, (ushort)tag("Pump1_F").Address, 100),
                ex => ex.Message.Contains("非法地址"));
            Check("写只读寄存器抛'非法地址'且不挂起", writeRejected);
            double afterReject = await ReadTagAsync(client, tag("CurWaterPos"));
            Check("异常响应后连接仍可用", Math.Abs(afterReject - 2.5) <= 0.011, $"实际 {afterReject}");

            // ── T1.9 mute 静默：IO 真超时 → Broken → 重连恢复（修复：基线为永久卡死）──
            await SendCommandAsync(sim!, outLines, "mute on");
            var muteSw = Stopwatch.StartNew();
            Exception? muteEx = null;
            try { await client.ReadHoldingRegistersAsync(1, 0, 5); }
            catch (Exception ex) { muteEx = ex; }
            long muteElapsed = muteSw.ElapsedMilliseconds;
            Check("mute 下读操作抛超时异常（不永久挂起）", muteEx is TimeoutException, $"实际 {muteEx?.GetType().Name}: {muteEx?.Message}");
            Check("超时时长在 2~4.5s 内", muteElapsed is >= 1800 and <= 4500, $"实际 {muteElapsed}ms");
            Check("客户端状态转 Broken", client.State == ModbusConnectionState.Broken, $"实际 {client.State}");
            await SendCommandAsync(sim!, outLines, "mute off");
            Check("mute 解除后重连成功", await client.ConnectAsync(3000));
            Check("重连后读取正常", Math.Abs(await ReadTagAsync(client, tag("CurWaterPos")) - 2.5) <= 0.011);

            // ── T1.9 drop 丢包：部分请求超时但整体不死锁，恢复后正常 ──
            await SendCommandAsync(sim!, outLines, "drop 2");
            int dropTimeouts = 0, dropOk = 0;
            for (int i = 0; i < 6; i++)
            {
                try
                {
                    await client.ReadHoldingRegistersAsync(1, 0, 5);
                    dropOk++;
                }
                catch
                {
                    dropTimeouts++;
                    client.Disconnect();
                    await client.ConnectAsync(2000);
                }
            }
            Check("drop 2 下出现超时且多数请求成功", dropTimeouts >= 1 && dropOk >= 2, $"超时 {dropTimeouts} 成功 {dropOk}");
            await SendCommandAsync(sim!, outLines, "drop 0");
            Check("drop 关闭后读取正常", Math.Abs(await ReadTagAsync(client, tag("CurWaterPos")) - 2.5) <= 0.011);

            // ── T1.9 delay：响应延迟在超时窗口内应正常完成 ──
            await SendCommandAsync(sim!, outLines, "delay 300");
            ushort[] delayed = await client.ReadHoldingRegistersAsync(1, 0, 5);
            Check("delay 300ms 下读取正常（在 2s 超时窗口内）", delayed.Length == 5);
            await SendCommandAsync(sim!, outLines, "delay 0");

            // ── T1.10 统计埋点：请求/成功/字节/耗时已累积 ──
            var stat = client.Statistics;
            Check("统计：请求数已累积", stat.RequestCount >= 20, $"实际 {stat.RequestCount}");
            Check("统计：成功率 > 60%", stat.SuccessRate > 0.6, $"实际 {stat.SuccessRate:P1}");
            Check("统计：RX/TX 字节已累积", stat.RxBytes > 0 && stat.TxBytes > 0);
            Check("统计：平均响应耗时已记录", stat.AvgRttMs > 0, $"实际 {stat.AvgRttMs:F1}ms");

            // ── 收尾：退出模拟器 ──
            try
            {
                await SendCommandAsync(sim, outLines, "quit");
                Check("模拟器正常退出", sim.WaitForExit(5000));
            }
            catch (Exception ex)
            {
                Check("模拟器正常退出", false, ex.Message);
            }
        }
        catch (Exception ex)
        {
            _failures++;
            Console.WriteLine($"[FAIL] 通信验收流程中断: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try { client?.Dispose(); } catch { }
            try { if (sim is { HasExited: false }) sim.Kill(true); } catch { }
        }
    }
    // 【模块 F 后】本段为集成回归快照；规范单测已迁 tests/IntelligentWaterStorage.Tests，
    // 新增用例只进 xUnit，此处不再扩充
    // ═══════════════════════════════════════════════

    static void RunAlarmCenterTests()
    {
        Console.WriteLine("\n── 模块 D 验收：AlarmCenter ──");

        string dbPath = Path.Combine(Path.GetTempPath(), $"alarms_{Guid.NewGuid():N}.db");
        try
        {
            var center = new AlarmCenter(dbPath);

            // ① 触发与去重
            center.Raise("HighPressure", "主管压力过高: 12.0Mpa", AlarmCenter.MapSeverity("HighPressure"));
            center.Raise("HighPressure", "主管压力过高: 12.5Mpa", AlarmSeverity.Critical); // 同名活跃 → 去重
            Check("触发后未确认数 = 1", center.UnacknowledgedCount == 1, $"实际 {center.UnacknowledgedCount}");
            Check("同名活跃报警去重", center.OpenCount == 1, $"实际 {center.OpenCount}");

            // ② 恢复（未确认）：仍占未确认数，状态为"已恢复·未确认"
            center.Clear("HighPressure", "主管压力恢复正常");
            var open = center.OpenSnapshot();
            Check("恢复后仍在未关闭列表", center.OpenCount == 1);
            Check("已恢复·未确认状态", open.Count == 1 && !open[0].IsActive && open[0].NeedsAck && open[0].StateText == "已恢复·未确认", open.Count == 1 ? open[0].StateText : "");

            // ③ 确认后关闭
            center.Acknowledge(open[0].Id, "测试员");
            Check("确认后未确认数 = 0", center.UnacknowledgedCount == 0);
            Check("确认后未关闭数 = 0", center.OpenCount == 0);

            // ④ 多条 + 全部确认 + 持续时间活计时
            center.Raise("Pump2Fault", "2#故障", AlarmSeverity.Critical);
            System.Threading.Thread.Sleep(1100); // 让持续时长 > 1s
            center.Raise("HighFlow", "主管流量过高", AlarmSeverity.Warning);
            Check("两条报警未确认数 = 2", center.UnacknowledgedCount == 2);
            Check("未确认 Critical 计数 = 1", center.UnacknowledgedCriticalCount == 1);
            var p2 = center.OpenSnapshot().First(r => r.Name == "Pump2Fault");
            Check("持续时长活计时 > 1s", p2.Duration.TotalSeconds > 1.0, $"实际 {p2.Duration.TotalSeconds:F1}s");
            center.AcknowledgeAll();
            Check("全部确认后未确认数 = 0", center.UnacknowledgedCount == 0);

            // ⑤ 严重级别映射
            Check("映射：泵故障 → Critical", AlarmCenter.MapSeverity("Pump2Fault") == AlarmSeverity.Critical);
            Check("映射：压力过高 → Critical", AlarmCenter.MapSeverity("HighPressure") == AlarmSeverity.Critical);
            Check("映射：流量过高 → Warning", AlarmCenter.MapSeverity("HighFlow") == AlarmSeverity.Warning);

            // ⑥ SQLite 持久化：新实例（模拟重启）后历史可查
            center.Clear("Pump2Fault", "2#故障已恢复");
            center.Clear("HighFlow", "主管流量恢复正常");
            var reopened = new AlarmCenter(dbPath);
            var history = reopened.QueryHistory(DateTime.Now.AddHours(-1), DateTime.Now, null, null);
            Check("重启后历史可查（3 条：去重后仅一次触发）", history.Count == 3, $"实际 {history.Count}");
            Check("含已关闭的 Pressure 报警", history.Any(r => r.Name == "HighPressure" && !r.IsActive && !r.NeedsAck));
            var byKeyword = reopened.QueryHistory(null, null, null, "故障");
            Check("关键字筛选命中泵故障", byKeyword.Any(r => r.Name.Contains("Pump") && r.Name.Contains("Fault")));
            var bySeverity = reopened.QueryHistory(null, null, AlarmSeverity.Warning, null);
            Check("级别筛选只含 Warning", bySeverity.All(r => r.Severity == AlarmSeverity.Warning) && bySeverity.Count >= 1);
        }
        catch (Exception ex)
        {
            _failures++;
            Console.WriteLine($"[FAIL] AlarmCenter 验收中断: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
        }
    }

    static Process StartSim(string exePath, ConcurrentQueue<string> outLines)
    {
        var psi = new ProcessStartInfo(exePath, "--port 1502")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        var proc = Process.Start(psi) ?? throw new InvalidOperationException("模拟器进程启动失败");
        _ = Task.Run(async () =>
        {
            try { string? l; while ((l = await proc.StandardOutput.ReadLineAsync()) != null) outLines.Enqueue(l); }
            catch { }
        });
        _ = Task.Run(async () =>
        {
            try { string? l; while ((l = await proc.StandardError.ReadLineAsync()) != null) outLines.Enqueue("[stderr] " + l); }
            catch { }
        });
        return proc;
    }

    static async Task<string> WaitForLineAsync(ConcurrentQueue<string> queue, Func<string, bool> match, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (queue.TryDequeue(out string? line))
            {
                if (match(line)) return line;
                continue;
            }
            await Task.Delay(25);
        }
        throw new TimeoutException("等待模拟器输出超时");
    }

    static async Task<string> SendCommandAsync(Process sim, ConcurrentQueue<string> queue, string command, int timeoutMs = 8000)
    {
        await sim.StandardInput.WriteLineAsync(command);
        return await WaitForLineAsync(queue, l => l.StartsWith("OK") || l.StartsWith("ERR"), timeoutMs);
    }

    static async Task<double> ReadTagAsync(ModbusClient client, TagAddress tag)
    {
        if (tag.IsCoil)
        {
            bool[] v = await client.ReadCoilsAsync(tag.UnitId, (ushort)tag.Address, 1);
            return v[0] ? 1 : 0;
        }
        ushort[] regs = await client.ReadHoldingRegistersAsync(tag.UnitId, (ushort)tag.Address, (ushort)tag.RegisterCount);
        return RegisterCodec.Decode(tag.Type, regs, 0) * tag.Scale;
    }

    static async Task<bool> WaitForTagAsync(ModbusClient client, TagAddress tag, Func<double, bool> condition, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            double v;
            try { v = await ReadTagAsync(client, tag); }
            catch { await Task.Delay(300); continue; }
            if (condition(v)) return true;
            await Task.Delay(300);
        }
        return false;
    }

    /// <summary>
    /// 用原始 Socket 发一个 Modbus TCP 请求并按 MBAP 长度精确收完整响应。
    /// 用于验证模拟器的异常响应行为（绕过待修复的旧客户端读写逻辑）。
    /// </summary>
    static async Task<(byte Fc, byte Code, byte[] Pdu)> RawRequestAsync(byte fc, ushort address, ushort valueOrQty)
    {
        using var socket = new System.Net.Sockets.TcpClient();
        await socket.ConnectAsync("127.0.0.1", 1502);
        var stream = socket.GetStream();
        byte[] request =
        {
            0x00, 0x01, 0x00, 0x00, 0x00, 0x06, 0x01,
            fc,
            (byte)(address >> 8), (byte)(address & 0xFF),
            (byte)(valueOrQty >> 8), (byte)(valueOrQty & 0xFF),
        };
        await stream.WriteAsync(request);

        byte[] header = await ReadExactAsync(stream, 7);
        ushort length = (ushort)((header[4] << 8) | header[5]);
        byte[] pdu = await ReadExactAsync(stream, length - 1);
        return (pdu[0], pdu.Length > 1 ? pdu[1] : (byte)0, pdu);
    }

    static async Task<byte[]> ReadExactAsync(System.Net.Sockets.NetworkStream stream, int count)
    {
        var buffer = new byte[count];
        int read = 0;
        while (read < count)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(read, count - read));
            if (n == 0) throw new IOException("对端关闭连接");
            read += n;
        }
        return buffer;
    }

    static async Task<bool> ThrowsAsync(Func<Task> action, Func<Exception, bool>? filter = null)
    {
        try
        {
            await action();
            return false;
        }
        catch (Exception ex) when (filter == null || filter(ex))
        {
            return true;
        }
    }

    static string? FindSimExe()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "tools", "ModbusSlaveSim", "bin", "Debug", "net8.0", "ModbusSlaveSim.exe");
            if (File.Exists(p)) return p;
        }
        return null;
    }

    static void Check(string name, bool ok, string detail = "")
    {
        if (ok)
            Console.WriteLine($"[PASS] {name}");
        else
        {
            _failures++;
            Console.WriteLine($"[FAIL] {name}  {detail}");
        }
    }

    // ═══════════════════════════════════════════════
    // 保留的历史冒烟测试（无断言，仅观察输出）
    // ═══════════════════════════════════════════════

    static async Task SmokeTests()
    {
        Console.WriteLine("\n── 历史冒烟测试（观察用） ──");

        try
        {
            var client = new ModbusClient("127.0.0.1", 1);
            Console.WriteLine("ModbusClient created (127.0.0.1:1)");
            bool connected = await client.ConnectAsync(1000);
            Console.WriteLine($"ConnectAsync returned: {connected}");
            client.Disconnect();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ModbusClient test threw: {ex.Message}");
        }

        try
        {
            var wb = ExcelHelper.CreateWorkBook(".xlsx");
            Console.WriteLine($"CreateWorkBook(.xlsx) != null: {wb != null}");
            var sheet = ExcelHelper.CreateSheet(wb!, "sheet1");
            Console.WriteLine($"CreateSheet result != null: {sheet != null}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ExcelHelper test threw: {ex.Message}");
        }

        try
        {
            var cfg = Utility.LoadCommunicationConfig("nonexistent_comm_config.txt");
            Console.WriteLine($"LoadCommunicationConfig returned IP='{cfg.IP}', Port={cfg.Port}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Utility test threw: {ex.Message}");
        }

        try
        {
            var mc = new ModbusClient("127.0.0.1", 1);
            var polling = new DataPollingService(mc, 500);
            Console.WriteLine("DataPollingService created");
            polling.Start();
            Console.WriteLine("DataPollingService started (no tags) -- should be no-op");
            polling.Stop();
            Console.WriteLine("DataPollingService stopped");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DataPollingService test threw: {ex.Message}");
        }
    }
}
