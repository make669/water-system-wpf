namespace Intelligent_water_storage_system.Model
{
    /// <summary>
    /// 单条报警信息模型。
    /// 由 AlarmService.Evaluate() 生成，表示一个报警事件的触发或恢复。
    ///
    /// 生命周期：
    ///   1. 报警触发 → IsActive=true，生成 AlarmInfo，通过 OnStatusChanged 事件通知 UI
    ///   2. 报警恢复 → IsActive=false，生成 AlarmInfo，表示该报警已消除
    ///   3. AlarmService 内部用 _activeAlarms HashSet 维护当前活跃报警集合
    /// </summary>
    public class AlarmInfo
    {
        /// <summary>
        /// 报警名称标识，全局唯一。
        /// 预定义的报警名称：
        ///   "LowWaterLevel" — 水位过低
        ///   "HighWaterLevel" — 水位过高
        ///   "HighFlow" — 主管流量过高
        ///   "HighPressure" — 主管压力过高
        ///   "Pump1Fault" / "Pump2Fault" / "Pump3Fault" — 水泵故障
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 报警的中文描述信息，直接用于 UI 显示。
        /// 例如："水位过低: 0.5m"、"主管压力过高: 12.3Mpa"、"1#故障"
        /// 由 AlarmService.GetAlarmMessage() / GetRecoveryMessage() 生成。
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 报警状态标识。
        /// true = 报警刚刚触发（新产生的报警）
        /// false = 报警刚刚恢复（之前的报警已消除）
        /// 注意：一个 AlarmInfo 实例只代表一次状态变化，不是持续状态。
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// 报警事件发生的时间戳。
        /// 触发时记录触发时间，恢复时记录恢复时间。
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 单台水泵的状态模型。
    /// 由 AlarmService.Evaluate() 根据 DataPoint 数据填充。
    ///
    /// 对应的 Tag 命名规则（以 Pump1 为例）：
    ///   Pump1_Run   → IsRunning（运行状态，0=停止，非0=运行）
    ///   Pump1_Fault → IsFault（故障状态，0=正常，非0=故障）
    ///   Pump1_Mode  → IsManual（控制模式，0=自动，非0=手动）
    ///   Pump1_S     → Speed（转速）
    ///   Pump1_P     → Power（功率）
    ///   Pump1_F     → Frequency（频率/变频器输出）
    /// </summary>
    public class PumpStatus
    {
        /// <summary>水泵是否正在运行</summary>
        public bool IsRunning { get; set; }

        /// <summary>水泵是否处于故障状态。故障时 AlarmService 会触发 "PumpNFault" 报警</summary>
        public bool IsFault { get; set; }

        /// <summary>水泵是否处于手动控制模式。true=手动, false=自动</summary>
        public bool IsManual { get; set; }

        /// <summary>水泵当前转速</summary>
        public double Speed { get; set; }

        /// <summary>水泵当前功率</summary>
        public double Power { get; set; }

        /// <summary>变频器输出频率</summary>
        public double Frequency { get; set; }
    }

    /// <summary>
    /// 单个阀门的状态模型。
    /// 由 AlarmService.Evaluate() 根据 DataPoint 数据填充。
    /// 阀门名称对应 AlarmConfig.ValveNames 数组中的值。
    /// </summary>
    public class ValveStatus
    {
        /// <summary>阀门是否打开。true=开启(运行), false=关闭</summary>
        public bool IsOpen { get; set; }
    }

    /// <summary>
    /// 系统整体状态快照模型。
    /// 由 AlarmService.Evaluate() 每次轮询时生成一个完整实例，
    /// 包含所有传感器数据、设备状态和报警信息的聚合视图。
    ///
    /// 数据来源：
    ///   - 水位/流量/压力/环境数据：从 DataPoint 字典中按 TagName 提取
    ///   - 水泵状态：遍历 Pump1~PumpN，每台泵提取 6 个数据点
    ///   - 阀门状态：遍历 ValveNames，每个阀门提取 1 个数据点
    ///   - 报警列表：本次评估中状态发生变化（新增触发或已恢复）的报警
    ///
    /// 消费者：
    ///   - FrmMain.OnAlarmStatusChanged() 接收后调用 UIUpdateService 更新界面
    ///   - UIUpdateService.UpdatePumpStatus() — 更新水泵状态标签和开关
    ///   - UIUpdateService.UpdateValveStatus() — 更新阀门状态标签
    /// </summary>
    public class SystemStatus
    {
        /// <summary>PLC 连接状态（当前代码中未被填充，预留字段）</summary>
        public bool IsConnected { get; set; }

        /// <summary>当前水位值（对应 Tag: CurWaterPos）</summary>
        public double WaterLevel { get; set; }

        /// <summary>主管出水流量（对应 Tag: MainOutFlow）</summary>
        public double MainOutFlow { get; set; }

        /// <summary>主管出水压力（对应 Tag: MainOutPressure）</summary>
        public double MainOutPressure { get; set; }

        /// <summary>实际用水量（对应 Tag: ActualWaters）</summary>
        public double ActualWater { get; set; }

        /// <summary>计划用水量（对应 Tag: PlanWaters）</summary>
        public double PlanWater { get; set; }

        /// <summary>环境温度（对应 Tag: CurTemperature）</summary>
        public double Temperature { get; set; }

        /// <summary>土壤湿度（对应 Tag: SoilHumidity）</summary>
        public double Humidity { get; set; }

        /// <summary>降水量（对应 Tag: Precipitation）</summary>
        public double Precipitation { get; set; }

        /// <summary>
        /// 水泵状态字典。Key 为 "Pump1"、"Pump2"、"Pump3" 等。
        /// </summary>
        public Dictionary<string, PumpStatus> Pumps { get; set; } = new();

        /// <summary>
        /// 阀门状态字典。Key 为 "MainValve"、"Valve1"、"Valve2"、"Valve3" 等。
        /// </summary>
        public Dictionary<string, ValveStatus> Valves { get; set; } = new();

        /// <summary>
        /// 本次评估中状态发生变化的报警列表。
        /// 注意：不是所有活跃报警，而是本次新增触发或已恢复的报警。
        /// 用于增量通知 UI，避免重复处理。
        /// </summary>
        public List<AlarmInfo> Alarms { get; set; } = new();
    }
}
