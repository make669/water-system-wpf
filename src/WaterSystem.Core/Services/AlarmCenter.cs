using Microsoft.Data.Sqlite;

namespace Intelligent_water_storage_system.Services
{
    /// <summary>报警级别。Critical=安全相关（压力/水位过低/泵故障），Warning=一般越限，Info=通知。</summary>
    public enum AlarmSeverity
    {
        Info = 0,
        Warning = 1,
        Critical = 2,
    }

    /// <summary>
    /// 单条报警记录（ISA-18.2 简化生命周期）。
    ///
    /// 状态机：
    ///   触发 → [报警中·未确认] → 确认 → [报警中·已确认]
   ///                │恢复                     │恢复
    ///                ↓                        ↓
    ///        [已恢复·未确认] ──确认──→ [已关闭]
    ///
    /// "已恢复·未确认"的存在迫使操作员回头看一眼刚才发生了什么。
    /// </summary>
    public class AlarmRecord
    {
        public long Id { get; set; }

        /// <summary>报警名（同 AlarmService 命名，如 Pump2Fault / HighPressure）</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>中文描述（含触发时的实时数值）</summary>
        public string Message { get; set; } = string.Empty;

        public AlarmSeverity Severity { get; set; }

        public DateTime TriggeredAt { get; set; } = DateTime.Now;

        /// <summary>恢复时间（null = 条件仍成立，报警活跃）</summary>
        public DateTime? ClearedAt { get; set; }

        /// <summary>确认时间（null = 操作员未确认）</summary>
        public DateTime? AckedAt { get; set; }

        public string? AckedBy { get; set; }

        /// <summary>报警条件是否仍成立</summary>
        public bool IsActive => ClearedAt == null;

        /// <summary>是否未确认（活跃或已恢复都算，恢复后仍需确认）</summary>
        public bool NeedsAck => AckedAt == null;

        /// <summary>持续时长（未恢复按当前时刻活计时）</summary>
        public TimeSpan Duration => (ClearedAt ?? DateTime.Now) - TriggeredAt;

        /// <summary>状态文案（实时报警面板显示）</summary>
        public string StateText => (IsActive, NeedsAck) switch
        {
            (true, true) => "报警中",
            (true, false) => "报警中·已确认",
            (false, true) => "已恢复·未确认",
            (false, false) => "已关闭",
        };
    }

    /// <summary>
    /// 报警中心：报警生命周期管理 + SQLite 持久化。
    ///
    /// 【职责】
    ///   - 接收 AlarmService 的增量触发/恢复（Raise / Clear），维护未关闭记录表
    ///   - 确认操作（Acknowledge / AcknowledgeAll）写 AckedAt
    ///   - 全部落库 SQLite（data/history.db），重启后历史可查
    ///   - 写库失败降级为日志（内存状态不受影响），报警显示不因存储故障而失效
    ///
    /// 【线程模型】
    ///   触发/恢复来自轮询回调（已封送 UI 线程），确认来自按钮，查询来自筛选控件，
    ///   统一 _gate 串行化。AlarmService 的 HashSet 去重保证 Raise/Clear 成对；
    ///   这里再做一层同名活跃去重作为第二道防线。
    /// </summary>
    public class AlarmCenter
    {
        private readonly object _gate = new();
        private readonly List<AlarmRecord> _open = new();
        private readonly string _dbPath;

        /// <summary>任意变化（触发/恢复/确认），UI 全量刷新实时面板与标题栏</summary>
        public event Action<AlarmRecord>? OnAlarmChanged;

        /// <summary>新触发（提示音/闪烁用）</summary>
        public event Action<AlarmRecord>? OnAlarmRaised;

        /// <summary>存储层故障（降级为日志，不中断报警功能）</summary>
        public event Action<string>? OnError;

        public AlarmCenter(string dbPath)
        {
            _dbPath = dbPath;
            EnsureDatabase();
        }

        /// <summary>未关闭记录数（报警中 + 已恢复未确认）</summary>
        public int OpenCount
        {
            get { lock (_gate) return _open.Count; }
        }

        /// <summary>未确认数（标题栏徽标、提示音的依据）</summary>
        public int UnacknowledgedCount
        {
            get { lock (_gate) return _open.Count(r => r.NeedsAck); }
        }

        /// <summary>未确认且 Critical 的记录数（标题栏闪烁的依据）</summary>
        public int UnacknowledgedCriticalCount
        {
            get { lock (_gate) return _open.Count(r => r.NeedsAck && r.Severity == AlarmSeverity.Critical); }
        }

        /// <summary>未关闭记录快照（实时面板数据源）</summary>
        public List<AlarmRecord> OpenSnapshot()
        {
            lock (_gate) return _open.OrderByDescending(r => r.TriggeredAt).ToList();
        }

        /// <summary>报警名 → 级别映射</summary>
        public static AlarmSeverity MapSeverity(string alarmName)
        {
            if (alarmName.StartsWith("Pump")) return AlarmSeverity.Critical;
            return alarmName switch
            {
                "HighPressure" => AlarmSeverity.Critical,
                "LowWaterLevel" => AlarmSeverity.Critical,
                _ => AlarmSeverity.Warning,
            };
        }

        /// <summary>
        /// 报警触发。同名报警仍在活跃（未恢复）时去重忽略。
        /// </summary>
        public void Raise(string name, string message, AlarmSeverity severity)
        {
            AlarmRecord record;
            lock (_gate)
            {
                if (_open.Any(r => r.Name == name && r.IsActive))
                    return;
                record = new AlarmRecord
                {
                    Name = name,
                    Message = message,
                    Severity = severity,
                    TriggeredAt = DateTime.Now,
                };
                record.Id = Insert(record);
                _open.Add(record);
            }
            OnAlarmRaised?.Invoke(record);
            OnAlarmChanged?.Invoke(record);
        }

        /// <summary>
        /// 报警恢复。没有对应的活跃触发时静默忽略（恢复消息比触发消息多属正常）。
        /// 已确认（NeedsAck=false）且已恢复的记录随恢复关闭并移出未关闭列表——
        /// 源实现缺失此移除（已关闭记录滞留 OpenCount，WPF M6 发现并修复，行为与 Acknowledge 对齐）。
        /// </summary>
        public void Clear(string name, string recoveryMessage)
        {
            AlarmRecord? changed = null;
            lock (_gate)
            {
                var record = _open.FirstOrDefault(r => r.Name == name && r.IsActive);
                if (record == null) return;
                record.ClearedAt = DateTime.Now;
                Update(record);
                if (!record.NeedsAck)
                    _open.Remove(record);   // 已确认 + 已恢复 = 生命周期终点（与 Acknowledge 对齐；WPF M6 修复源滞留缺陷）
                changed = record;
            }
            if (changed != null) OnAlarmChanged?.Invoke(changed);
        }

        /// <summary>确认一条记录（写 AckedAt/AckedBy 并落库）。已恢复的记录确认后即关闭，移出未关闭列表。</summary>
        public void Acknowledge(long id, string by = "操作员")
        {
            AlarmRecord? changed = null;
            lock (_gate)
            {
                var record = _open.FirstOrDefault(r => r.Id == id);
                if (record == null || record.AckedAt != null) return;
                record.AckedAt = DateTime.Now;
                record.AckedBy = by;
                Update(record);
                if (!record.IsActive)
                    _open.Remove(record); // 已恢复 + 已确认 = 生命周期终点
                changed = record;
            }
            if (changed != null) OnAlarmChanged?.Invoke(changed);
        }

        /// <summary>确认全部未确认记录（已恢复的随即关闭移出）</summary>
        public void AcknowledgeAll(string by = "操作员")
        {
            List<AlarmRecord> changed = new();
            lock (_gate)
            {
                foreach (var record in _open.Where(r => r.NeedsAck).ToList())
                {
                    record.AckedAt = DateTime.Now;
                    record.AckedBy = by;
                    Update(record);
                    if (!record.IsActive)
                        _open.Remove(record);
                    changed.Add(record);
                }
            }
            if (changed.Count > 0) OnAlarmChanged?.Invoke(changed[0]);
        }

        /// <summary>
        /// 历史查询（从 SQLite，含已关闭记录）。
        /// </summary>
        /// <param name="from">起始时间（null=不限）</param>
        /// <param name="to">结束时间（null=不限）</param>
        /// <param name="severity">级别筛选（null=全部）</param>
        /// <param name="keyword">关键字（匹配名称/描述，null=全部）</param>
        /// <param name="limit">最大返回条数</param>
        public List<AlarmRecord> QueryHistory(DateTime? from, DateTime? to, AlarmSeverity? severity, string? keyword, int limit = 500)
        {
            var result = new List<AlarmRecord>();
            try
            {
                using var conn = OpenConnection();
                var sql = "SELECT id, name, message, severity, triggered_at, cleared_at, acked_at, acked_by FROM alarms WHERE 1=1";
                if (from != null) sql += " AND triggered_at >= @from";
                if (to != null) sql += " AND triggered_at <= @to";
                if (severity != null) sql += " AND severity = @sev";
                if (!string.IsNullOrEmpty(keyword)) sql += " AND (name LIKE @kw OR message LIKE @kw)";
                sql += " ORDER BY triggered_at DESC LIMIT @lim";

                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                if (from != null) cmd.Parameters.AddWithValue("@from", FormatTime(from.Value));
                if (to != null) cmd.Parameters.AddWithValue("@to", FormatTime(to.Value));
                if (severity != null) cmd.Parameters.AddWithValue("@sev", (int)severity);
                if (!string.IsNullOrEmpty(keyword)) cmd.Parameters.AddWithValue("@kw", $"%{keyword}%");
                cmd.Parameters.AddWithValue("@lim", limit);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    result.Add(new AlarmRecord
                    {
                        Id = reader.GetInt64(0),
                        Name = reader.GetString(1),
                        Message = reader.GetString(2),
                        Severity = (AlarmSeverity)reader.GetInt32(3),
                        TriggeredAt = ParseTime(reader.GetString(4)),
                        ClearedAt = reader.IsDBNull(5) ? null : ParseTime(reader.GetString(5)),
                        AckedAt = reader.IsDBNull(6) ? null : ParseTime(reader.GetString(6)),
                        AckedBy = reader.IsDBNull(7) ? null : reader.GetString(7),
                    });
                }
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"报警历史查询失败: {ex.Message}");
            }
            return result;
        }

        // ═══════════════════════════════════════════════
        // SQLite 存储层（失败降级：返回 -1 / 静默，不影响内存状态）
        // ═══════════════════════════════════════════════

        private SqliteConnection OpenConnection()
        {
            var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            return conn;
        }

        private void EnsureDatabase()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
                using var conn = OpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS alarms (
  id           INTEGER PRIMARY KEY AUTOINCREMENT,
  name         TEXT NOT NULL,
  message      TEXT NOT NULL,
  severity     INTEGER NOT NULL,
  triggered_at TEXT NOT NULL,
  cleared_at   TEXT,
  acked_at     TEXT,
  acked_by     TEXT
);
CREATE INDEX IF NOT EXISTS idx_alarms_time ON alarms(triggered_at);";
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"报警数据库初始化失败: {ex.Message}");
            }
        }

        private long Insert(AlarmRecord r)
        {
            try
            {
                using var conn = OpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
INSERT INTO alarms (name, message, severity, triggered_at, cleared_at, acked_at, acked_by)
VALUES (@name, @msg, @sev, @trg, @clr, @ack, @by);
SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("@name", r.Name);
                cmd.Parameters.AddWithValue("@msg", r.Message);
                cmd.Parameters.AddWithValue("@sev", (int)r.Severity);
                cmd.Parameters.AddWithValue("@trg", FormatTime(r.TriggeredAt));
                cmd.Parameters.AddWithValue("@clr", r.ClearedAt == null ? DBNull.Value : FormatTime(r.ClearedAt.Value));
                cmd.Parameters.AddWithValue("@ack", r.AckedAt == null ? DBNull.Value : FormatTime(r.AckedAt.Value));
                cmd.Parameters.AddWithValue("@by", (object?)r.AckedBy ?? DBNull.Value);
                return (long)(cmd.ExecuteScalar() ?? 0L);
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"报警落库失败: {ex.Message}");
                return 0;
            }
        }

        private void Update(AlarmRecord r)
        {
            try
            {
                using var conn = OpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
UPDATE alarms SET message=@msg, cleared_at=@clr, acked_at=@ack, acked_by=@by WHERE id=@id";
                cmd.Parameters.AddWithValue("@msg", r.Message);
                cmd.Parameters.AddWithValue("@clr", r.ClearedAt == null ? DBNull.Value : FormatTime(r.ClearedAt.Value));
                cmd.Parameters.AddWithValue("@ack", r.AckedAt == null ? DBNull.Value : FormatTime(r.AckedAt.Value));
                cmd.Parameters.AddWithValue("@by", (object?)r.AckedBy ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@id", r.Id);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"报警更新失败: {ex.Message}");
            }
        }

        private static string FormatTime(DateTime t) => t.ToString("yyyy-MM-dd HH:mm:ss.fff");

        private static DateTime ParseTime(string s) => DateTime.Parse(s);
    }
}
