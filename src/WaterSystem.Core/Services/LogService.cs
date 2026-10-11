using System.ComponentModel;

namespace Intelligent_water_storage_system.Services
{
    /// <summary>
    /// 日志条目模型，用于 DataGridView 显示。
    /// </summary>
    public class LogEntry
    {
        public DateTime Time { get; set; }
        public string Level { get; set; } = "";
        public string Message { get; set; } = "";
    }

    /// <summary>
    /// 日志服务。将日志写入文件，同时提供数据源给 DataGridView 实时显示。
    ///
    /// 【UI v4 数据源分层】（docs/UI重设计-v4.md 第 4 章）
    ///   日志文件            → 全量（含 DEBUG）
    ///   Entries（主界面网格）→ INFO/WARN/ERROR（DEBUG 不进，主界面不刷屏）
    ///   OnEntry 事件        → 全量（含 DEBUG）→ 独立日志窗口 FrmLogViewer（F12）
    /// </summary>
    public class LogService
    {
        private readonly BindingList<LogEntry> _entries = new();
        private readonly string _logFilePath;
        private readonly object _fileLock = new();

        /// <summary>供 DataGridView 绑定的数据源（不含 DEBUG）</summary>
        public BindingList<LogEntry> Entries => _entries;

        /// <summary>每条日志（含 DEBUG）的实时事件——独立日志窗口的数据源。UI 线程触发（Add 经 FrmMain.Log 封送）。</summary>
        public event Action<LogEntry>? OnEntry;

        /// <summary>最大保留条数，超过后移除最老的（仅主界面网格；文件与日志窗口各自独立）</summary>
        public int MaxEntries { get; set; } = 500;

        public LogService()
        {
            string logDir = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(logDir);
            _logFilePath = Path.Combine(logDir, $"{DateTime.Now:yyyy-MM-dd}.log");
        }

        /// <summary>
        /// 添加一条日志。线程安全，可从任意线程调用。
        /// DEBUG 级别只落文件与 OnEntry 事件，不进主界面网格（UI v4 降噪）。
        /// </summary>
        public void Add(string level, string message)
        {
            var entry = new LogEntry
            {
                Time = DateTime.Now,
                Level = level,
                Message = message
            };

            // 写文件（后台线程安全）
            lock (_fileLock)
            {
                try
                {
                    File.AppendAllText(_logFilePath,
                        $"[{entry.Time:HH:mm:ss}] [{level}] {message}{Environment.NewLine}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[LogService] 文件写入失败: {ex.Message}");
                }
            }

            // 添加到内存列表（必须在 UI 线程调用）；DEBUG 不进主界面
            if (level != "DEBUG")
            {
                _entries.Add(entry);

                // 超过上限移除最老的
                while (_entries.Count > MaxEntries)
                    _entries.RemoveAt(0);
            }

            // 全量事件（含 DEBUG）→ 独立日志窗口
            OnEntry?.Invoke(entry);
        }

        public void Info(string message) => Add("INFO", message);
        public void Warn(string message) => Add("WARN", message);
        public void Error(string message) => Add("ERROR", message);
    }
}
