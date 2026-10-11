namespace Intelligent_water_storage_system.Model
{
    /// <summary>
    /// PLC 实时数据点模型。
    /// 每次轮询时，DataPollingService 会为每个 TagAddress 生成一个 DataPoint，
    /// 所有 DataPoint 打包成 Dictionary&lt;string, DataPoint&gt; 通过 OnDataReceived 事件传递给 FrmMain。
    ///
    /// 数据流转过程：
    ///   Modbus 寄存器原始值(ushort) → RawValue → 乘以 Scale → Value → 供 UI 显示和报警评估使用
    ///
    /// 例如：水位寄存器原始值为 150，Scale=0.1，则 Value=15.0 米。
    /// </summary>
    public class DataPoint
    {
        /// <summary>
        /// 数据点名称，与 TagAddress.TagName 对应。
        /// 例如 "CurWaterPos"（当前水位）、"MainOutFlow"（主管流量）、"Pump1_Run"（1#泵运行状态）
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 缩放后的工程值，用于 UI 显示和业务逻辑判断。
        /// 计算公式：Value = RawValue × TagAddress.Scale
        /// </summary>
        public double Value { get; set; }

        /// <summary>
        /// 从 Modbus 寄存器直接读取的原始数值，未经缩放。
        /// 对于保持寄存器(Holding Register)：原始 ushort 值
        /// 对于线圈(Coil)：1 表示 true，0 表示 false
        /// </summary>
        public double RawValue { get; set; }

        /// <summary>
        /// 工程单位字符串，如 "m"（米）、"m³"（立方米）、"℃"、"%" 等。
        /// 从 TagAddress.Unit 字段复制过来，方便 UI 直接显示 "15.0m" 这样的文本。
        /// </summary>
        public string Unit { get; set; } = string.Empty;

        /// <summary>
        /// 数据采集时间戳，由 DataPollingService 在读取数据时打上 DateTime.Now。
        /// 可用于判断数据新鲜度、趋势图时间轴等。
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>
        /// 数据质量标识。true 表示数据有效，false 表示通信异常或数据不可信。
        /// 当前代码中默认为 true，如果后续加入通信超时检测，可将此字段设为 false。
        /// </summary>
        public bool Quality { get; set; } = true;
    }
}
