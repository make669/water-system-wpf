using Intelligent_water_storage_system.Model;

namespace Intelligent_water_storage_system.Services
{
    /// <summary>
    /// 报警评估服务。
    ///
    /// 【核心职责】
    /// 每次轮询得到新数据后，将所有传感器数据与预设阈值进行比较，
    /// 判断是否存在报警条件，并跟踪报警的触发和恢复。
    ///
    /// 【报警评估逻辑】
    ///   1. 从 DataPoint 字典中提取各项数据，与 AlarmConfig 中的阈值比较
    ///   2. 维护 _activeAlarms HashSet 记录当前所有活跃报警
    ///   3. 每次评估时，用 currentActive HashSet 记录本轮应该活跃的报警
    ///   4. 对比 _activeAlarms 和 currentActive：
    ///      - 新增：currentActive 有但 _activeAlarms 没有 → 触发报警（IsActive=true）
    ///      - 恢复：_activeAlarms 有但 currentActive 没有 → 报警恢复（IsActive=false）
    ///   5. 通过 OnStatusChanged 事件将状态变化通知 FrmMain
    ///
    /// 【报警类型】
    ///   - 模拟量越限：水位过低/过高、流量过高、压力过高
    ///   - 设备故障：水泵 1~N 故障
    /// </summary>
    public class AlarmService
    {
        /// <summary>
        /// 报警阈值配置。包含水位上下限、流量上限、压力上限、水泵数量、阀门名称等。
        /// 如果构造时未传入 config，则使用 AlarmConfig 的默认值。
        /// </summary>
        private readonly AlarmConfig _config;

        /// <summary>
        /// 当前活跃报警集合。存储所有当前处于触发状态的报警名称。
        /// 例如：{"LowWaterLevel", "Pump2Fault"} 表示水位过低和 2#泵故障同时存在。
        /// 使用 HashSet 实现 O(1) 的添加/查找/删除。
        /// </summary>
        private readonly HashSet<string> _activeAlarms = new();

        /// <summary>
        /// 系统状态变化事件。
        /// 每次 Evaluate() 执行完毕后触发，传递包含状态变化报警列表的 SystemStatus。
        /// FrmMain.OnAlarmStatusChanged() 订阅此事件来更新水泵/阀门的 UI 状态。
        /// </summary>
        public event Action<SystemStatus>? OnStatusChanged;

        /// <summary>
        /// 构造函数。
        /// </summary>
        /// <param name="config">
        /// 报警阈值配置。传 null 则使用 AlarmConfig 的默认值
        /// （水位 1~10m，流量上限 1000m³，压力上限 10Mpa，3 台泵，4 个阀门）
        /// </param>
        public AlarmService(AlarmConfig? config = null)
        {
            _config = config ?? new AlarmConfig();
        }

        /// <summary>
        /// 核心报警评估方法。每次轮询数据到达时由 FrmMain 调用。
        ///
        /// 【执行流程详解】
        ///   第 1 阶段：提取数据并构建 SystemStatus 快照
        ///     - 水位、流量、压力、环境数据
        ///     - 每台水泵的运行/故障/手动/速度/功率/频率状态
        ///     - 每个阀门的开/关状态
        ///
        ///   第 2 阶段：报警条件判断
        ///     - 水位 < 低限 → "LowWaterLevel"
        ///     - 水位 > 高限 → "HighWaterLevel"
        ///     - 流量 > 上限 → "HighFlow"
        ///     - 压力 > 上限 → "HighPressure"
        ///     - 水泵故障 → "PumpNFault"
        ///
        ///   第 3 阶段：增量报警通知
        ///     - 新报警：当前触发但之前未在 _activeAlarms 中的
        ///     - 已恢复：之前在 _activeAlarms 中但当前不再触发的
        ///     - 只有状态变化的报警才会出现在 status.Alarms 列表中
        /// </summary>
        /// <param name="data">本次轮询的完整数据点字典</param>
        /// <returns>包含当前系统状态和增量报警信息的 SystemStatus 对象</returns>
        public SystemStatus Evaluate(Dictionary<string, DataPoint> data)
        {
            var status = new SystemStatus();
            // 本轮新增或恢复的报警列表
            var alarms = new List<AlarmInfo>();
            // 本轮应该活跃的报警名称集合
            var currentActive = new HashSet<string>();

            // ─── 水位报警判断 ───
            if (data.TryGetValue("CurWaterPos", out var wp))
            {
                status.WaterLevel = wp.Value;
                if (wp.Value < _config.WaterLevelLow)
                    currentActive.Add("LowWaterLevel");   // 水位过低报警
                else if (wp.Value > _config.WaterLevelHigh)
                    currentActive.Add("HighWaterLevel");  // 水位过高报警
            }

            // ─── 主管流量报警判断 ───
            if (data.TryGetValue("MainOutFlow", out var mf))
            {
                status.MainOutFlow = mf.Value;
                if (mf.Value > _config.MaxFlow)
                    currentActive.Add("HighFlow");  // 流量过高报警
            }

            // ─── 主管压力报警判断 ───
            if (data.TryGetValue("MainOutPressure", out var mp))
            {
                status.MainOutPressure = mp.Value;
                if (mp.Value > _config.MaxPressure)
                    currentActive.Add("HighPressure");  // 压力过高报警
            }

            // ─── 环境数据提取（无报警判断，仅用于 UI 显示） ───
            if (data.TryGetValue("ActualWaters", out var aw))
                status.ActualWater = aw.Value;
            if (data.TryGetValue("PlanWaters", out var pw))
                status.PlanWater = pw.Value;
            if (data.TryGetValue("CurTemperature", out var ct))
                status.Temperature = ct.Value;
            if (data.TryGetValue("SoilHumidity", out var sh))
                status.Humidity = sh.Value;
            if (data.TryGetValue("Precipitation", out var pr))
                status.Precipitation = pr.Value;

            // ─── 水泵状态提取与故障报警 ───
            for (int i = 1; i <= _config.PumpCount; i++)
            {
                var ps = new PumpStatus();
                // 按约定的命名规则查找各状态位：
                //   PumpN_Run   → 运行状态（0=停止，非0=运行）
                //   PumpN_Fault → 故障状态（0=正常，非0=故障）
                //   PumpN_Mode  → 控制模式（0=自动，非0=手动）
                //   PumpN_S     → 转速
                //   PumpN_P     → 功率
                //   PumpN_F     → 频率
                if (data.TryGetValue($"Pump{i}_Run", out var run)) ps.IsRunning = run.Value != 0;
                if (data.TryGetValue($"Pump{i}_Fault", out var fault)) ps.IsFault = fault.Value != 0;
                if (data.TryGetValue($"Pump{i}_Mode", out var mode)) ps.IsManual = mode.Value != 0;
                if (data.TryGetValue($"Pump{i}_S", out var speed)) ps.Speed = speed.Value;
                if (data.TryGetValue($"Pump{i}_P", out var power)) ps.Power = power.Value;
                if (data.TryGetValue($"Pump{i}_F", out var freq)) ps.Frequency = freq.Value;

                // 水泵故障 → 产生报警
                if (ps.IsFault)
                    currentActive.Add($"Pump{i}Fault");

                status.Pumps[$"Pump{i}"] = ps;
            }

            // ─── 阀门状态提取 ───
            var valveNames = _config.ValveNames;
            foreach (var vn in valveNames)
            {
                var vs = new ValveStatus();
                if (data.TryGetValue(vn, out var v))
                    vs.IsOpen = v.Value != 0;
                status.Valves[vn] = vs;
            }

            // ─── 增量报警处理 ───

            // 新触发的报警：currentActive 中有，但 _activeAlarms 中没有的
            foreach (var name in currentActive)
            {
                // HashSet.Add() 返回 true 表示之前不存在（成功添加）
                if (_activeAlarms.Add(name))
                    alarms.Add(new AlarmInfo { Name = name, Message = GetAlarmMessage(name, data), IsActive = true });
            }

            // 已恢复的报警：_activeAlarms 中有，但 currentActive 中没有的
            // 使用 .ToList() 创建副本，避免在遍历过程中修改集合
            foreach (var name in _activeAlarms.ToList())
            {
                if (!currentActive.Contains(name))
                {
                    _activeAlarms.Remove(name);
                    alarms.Add(new AlarmInfo { Name = name, Message = GetRecoveryMessage(name), IsActive = false });
                }
            }

            status.Alarms = alarms;
            // 通知外部：系统状态已更新
            OnStatusChanged?.Invoke(status);
            return status;
        }

        /// <summary>
        /// 根据报警名称生成对应的中文报警消息（带实时数值）。
        /// 使用 switch 表达式进行模式匹配：
        ///   - "when" 条件：在匹配名称的同时，从 data 中取出当前值
        ///   - "var n when" 通配模式：匹配 PumpNFault 格式的报警
        ///   - "_" 兜底：未识别的报警名称
        /// </summary>
        /// <param name="name">报警名称，如 "LowWaterLevel"</param>
        /// <param name="data">当前数据字典，用于提取实际值</param>
        /// <returns>中文报警消息，如 "水位过低: 0.5m"</returns>
        private static string GetAlarmMessage(string name, Dictionary<string, DataPoint> data)
        {
            return name switch
            {
                // 水位过低报警，附带当前水位值
                "LowWaterLevel" when data.TryGetValue("CurWaterPos", out var v) => $"水位过低: {v.Value:F1}m",
                // 水位过高报警，附带当前水位值
                "HighWaterLevel" when data.TryGetValue("CurWaterPos", out var v) => $"水位过高: {v.Value:F1}m",
                // 流量过高报警，附带当前流量值
                "HighFlow" when data.TryGetValue("MainOutFlow", out var v) => $"主管流量过高: {v.Value:F1}m³",
                // 压力过高报警，附带当前压力值
                "HighPressure" when data.TryGetValue("MainOutPressure", out var v) => $"主管压力过高: {v.Value:F1}Mpa",
                // 水泵故障报警，如 "Pump2Fault" → "2#故障"
                var n when n.StartsWith("Pump") && n.EndsWith("Fault") => $"{n.Replace("Fault", "")}#故障",
                // 未识别的报警名称
                _ => $"{name} 报警"
            };
        }

        /// <summary>
        /// 根据报警名称生成对应的中文恢复消息。
        /// 与 GetAlarmMessage 类似，但不附带实时数值（恢复时不需要显示具体值）。
        /// </summary>
        /// <param name="name">报警名称</param>
        /// <returns>中文恢复消息，如 "水位恢复正常"</returns>
        private static string GetRecoveryMessage(string name)
        {
            return name switch
            {
                "LowWaterLevel" => "水位恢复正常",
                "HighWaterLevel" => "水位恢复正常",
                "HighFlow" => "主管流量恢复正常",
                "HighPressure" => "主管压力恢复正常",
                // 水泵故障恢复，如 "Pump2Fault" → "2#故障已恢复"
                var n when n.StartsWith("Pump") && n.EndsWith("Fault") => $"{n.Replace("Fault", "")}#故障已恢复",
                _ => $"{name} 已恢复"
            };
        }
    }
}
