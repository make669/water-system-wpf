using System.IO;
using System.Text.Json;

namespace WaterSystem.Wpf.State
{
    /// <summary>会话状态（对应源 Initializer/app_init.json 的 WPF 版，语义对齐）：
    /// 只存开关语义状态与通信参数——**不存显示值**（恢复后立即空态，FrmMain.cs:179-181"历史快照不得当作事实显示"）。</summary>
    public sealed record AppState(string Ip, int Port, IReadOnlyDictionary<string, bool> Switches);

    internal sealed class Dto
    {
        public int Version { get; set; }
        public CommDto? Comm { get; set; }
        public Dictionary<string, bool>? Switches { get; set; }
        public sealed class CommDto { public string? Ip { get; set; } public int Port { get; set; } }
    }

    /// <summary>fail-soft：坏文件/缺失 → null（调用方记 WARN 不阻断启动，对应源 Initializer.cs:107-111）。</summary>
    public static class AppStateStore
    {
        private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

        public static AppState? Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(path));
                if (dto?.Comm == null || dto.Switches == null) return null;
                return new AppState(dto.Comm.Ip ?? "127.0.0.1", dto.Comm.Port, dto.Switches);
            }
            catch
            {
                return null;
            }
        }

        public static void Save(string path, string ip, int port, IEnumerable<KeyValuePair<string, bool>> switches)
        {
            var dto = new Dto
            {
                Version = 2,
                Comm = new Dto.CommDto { Ip = ip, Port = port },
                Switches = switches.ToDictionary(kv => kv.Key, kv => kv.Value),
            };
            File.WriteAllText(path, JsonSerializer.Serialize(dto, Options));
        }
    }
}
