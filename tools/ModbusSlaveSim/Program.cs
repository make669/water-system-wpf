using Intelligent_water_storage_system.Model;
using Intelligent_water_storage_system.Utils;
using Intelligent_water_storage_system.Simulation;

namespace ModbusSlaveSim
{
    /// <summary>
    /// Modbus TCP 从站模拟器（控制台程序）。
    ///
    /// 【用途】
    ///   1. 面试/开发演示不依赖硬件：与上位机走完全相同的 Modbus TCP 协议
    ///   2. 通过通信劣化开关（delay/frag/mute/drop）复现并验证上位机通信层的健壮性
    ///   3. 数据源与上位机同源：加载同一份 configFiles/TagTable.csv
    ///
    /// 【启动】
    ///   ModbusSlaveSim [--port 502]
    ///
    /// 【命令】（stdin，逐行；每条命令输出 OK/ERR，供自动化脚本同步）
    ///   status                 打印全部点位的原始值与工程值
    ///   set &lt;Tag&gt; &lt;工程值&gt;     手动改任意点位
    ///   fault &lt;1-3&gt; on|off     注入/清除泵故障
    ///   surge on|off           压力浪涌（12Mpa，触发报警）
    ///   scenario steady|wave   稳态 / 水位波动
    ///   delay &lt;ms&gt;             响应延迟
    ///   frag on|off            分片发送（复现错帧）
    ///   mute on|off            静默（复现卡死）
    ///   drop &lt;n&gt;               每 n 个响应丢 1 个（0=关闭）
    ///   help / quit
    /// </summary>
    internal static class Program
    {
        static async Task<int> Main(string[] args)
        {
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { /* 非交互环境可能不支持 */ }

            int port = 502;
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "--port" && int.TryParse(args[i + 1], out int p) && p is > 0 and <= 65535)
                    port = p;

            // 与上位机同源的标签表（数据契约）
            string? csvPath = FindRepoFile("configFiles", "TagTable.csv");
            if (csvPath == null)
            {
                Console.WriteLine("ERR 未找到 configFiles/TagTable.csv（请从仓库根目录或已部署目录运行）");
                return 1;
            }
            var tags = TagTableLoader.Load(csvPath, out var errors, out _);
            if (errors.Count > 0)
            {
                Console.WriteLine("ERR 标签表校验失败：");
                foreach (var e in errors) Console.WriteLine("  " + e);
                return 1;
            }
            Console.WriteLine($"标签表加载完成: {tags.Count} 点 ({csvPath})");

            var store = new DataStore(tags);
            var options = new SimulatorOptions();
            var engine = new ScenarioEngine(store);
            var server = new ModbusServer(store, options, engine);

            using var cts = new CancellationTokenSource();
            Task serverTask = server.RunAsync(port, cts.Token);

            PrintHelp();
            while (true)
            {
                string? line = Console.ReadLine();
                if (line == null) break;
                string cmd = line.Trim();
                if (cmd.Length == 0) continue;
                if (HandleCommand(cmd, store, options, engine)) break; // quit
            }

            cts.Cancel();
            try { await serverTask; } catch (OperationCanceledException) { }
            Console.WriteLine("BYE");
            return 0;
        }

        /// <summary>执行一条命令。返回 true 表示退出。</summary>
        static bool HandleCommand(string cmd, DataStore store, SimulatorOptions options, ScenarioEngine engine)
        {
            string[] parts = cmd.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string op = parts[0].ToLowerInvariant();
            try
            {
                switch (op)
                {
                    case "help":
                        PrintHelp();
                        break;

                    case "status":
                        PrintStatus(store);
                        break;

                    case "set":
                        Require(parts.Length == 3, "用法: set <Tag> <工程值>");
                        {
                            var tag = store.FindTag(parts[1]) ?? throw new ArgumentException($"找不到标签 {parts[1]}");
                            double eng = double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
                            store.WriteEngineering(tag, eng);
                            Console.WriteLine($"OK set {tag.TagName} = {eng} (raw={eng / tag.Scale:0.###})");
                        }
                        break;

                    case "fault":
                        Require(parts.Length == 3 && (parts[2] == "on" || parts[2] == "off"), "用法: fault <1-3> on|off");
                        if (!int.TryParse(parts[1], out int pumpNo) || pumpNo is < 1 or > 3)
                        {
                            Console.WriteLine("ERR 泵编号必须是 1~3");
                            break;
                        }
                        {
                            var tag = store.FindTag($"Pump{pumpNo}_Fault") ?? throw new ArgumentException("找不到故障标签");
                            store.WriteEngineering(tag, parts[2] == "on" ? 1 : 0);
                            Console.WriteLine($"OK fault {pumpNo} -> {parts[2]}");
                        }
                        break;

                    case "surge":
                        Require(parts.Length == 2 && (parts[1] == "on" || parts[1] == "off"), "用法: surge on|off");
                        engine.SetSurge(parts[1] == "on");
                        Console.WriteLine($"OK surge {parts[1]}");
                        break;

                    case "scenario":
                        Require(parts.Length == 2 && (parts[1] == "steady" || parts[1] == "wave"),
                            "用法: scenario steady|wave");
                        engine.SetScenario(parts[1]);
                        Console.WriteLine($"OK scenario {parts[1]}");
                        break;

                    case "delay":
                        Require(parts.Length == 2, "用法: delay <ms>");
                        if (!int.TryParse(parts[1], out int delayMs) || delayMs < 0 || delayMs > 60000)
                        {
                            Console.WriteLine("ERR delay 必须是 0~60000 的整数");
                            break;
                        }
                        options.ResponseDelayMs = delayMs;
                        Console.WriteLine($"OK delay {delayMs}ms");
                        break;

                    case "frag":
                        Require(parts.Length == 2 && (parts[1] == "on" || parts[1] == "off"), "用法: frag on|off");
                        options.Frag = parts[1] == "on";
                        Console.WriteLine($"OK frag {parts[1]}");
                        break;

                    case "mute":
                        Require(parts.Length == 2 && (parts[1] == "on" || parts[1] == "off"), "用法: mute on|off");
                        options.Mute = parts[1] == "on";
                        Console.WriteLine($"OK mute {parts[1]}");
                        break;

                    case "drop":
                        Require(parts.Length == 2, "用法: drop <n>（0=关闭）");
                        if (!int.TryParse(parts[1], out int dropN) || dropN < 0)
                        {
                            Console.WriteLine("ERR drop 必须是非负整数");
                            break;
                        }
                        options.DropEveryN = dropN;
                        Console.WriteLine($"OK drop every {dropN}");
                        break;

                    case "quit":
                        Console.WriteLine("OK quit");
                        return true;

                    default:
                        Console.WriteLine($"ERR 未知命令: {cmd}（help 查看用法）");
                        break;
                }
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"ERR {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERR {ex.GetType().Name}: {ex.Message}");
            }
            return false;
        }

        static void Require(bool condition, string usage)
        {
            if (!condition) throw new ArgumentException(usage);
        }

        static void PrintStatus(DataStore store)
        {
            Console.WriteLine("── TagTable 当前值 ──");
            foreach (var tag in store.Tags)
            {
                double eng = store.ReadEngineering(tag);
                string value = tag.IsCoil ? (eng != 0 ? "ON" : "OFF") : $"{eng:0.###} {tag.Unit}";
                Console.WriteLine($"  {tag.TagName,-18} Addr={tag.Address,-3} {tag.Type,-7} {value}");
            }
            Console.WriteLine("OK status");
        }

        static void PrintHelp()
        {
            Console.WriteLine("命令: status | set <Tag> <值> | fault <1-3> on|off | surge on|off");
            Console.WriteLine("      scenario steady|wave | delay <ms> | frag on|off | mute on|off | drop <n> | quit");
        }

        /// <summary>向上查找仓库内的文件（开发环境与部署目录都能工作）</summary>
        static string? FindRepoFile(string dir, string file)
        {
            if (File.Exists(Path.Combine(AppContext.BaseDirectory, dir, file)))
                return Path.Combine(AppContext.BaseDirectory, dir, file);

            var d = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
            {
                string p = Path.Combine(d.FullName, dir, file);
                if (File.Exists(p)) return p;
            }
            return null;
        }
    }
}
