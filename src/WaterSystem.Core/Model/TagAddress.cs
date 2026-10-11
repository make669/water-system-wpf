namespace Intelligent_water_storage_system.Model
{
    /// <summary>
    /// 数据点类型。决定该点在 Modbus 地址空间中的读取方式与占用数量。
    /// 由 TagTable.csv 的 Type 列解析而来（TagTableLoader.TryParseType）。
    /// </summary>
    public enum TagType
    {
        /// <summary>
        /// 线圈（开关量）。1 个线圈 = 1 位。
        /// 读取：ReadCoilsAsync(0x01)；写入：WriteSingleCoilAsync(0x05)。
        /// 对应原 DataType="bool" 的语义。
        /// </summary>
        Coil,

        /// <summary>16 位无符号整数，占用 1 个保持寄存器。读取 0x03 / 写入 0x06。</summary>
        UShort,

        /// <summary>
        /// 32 位浮点数（IEEE 754），占用 2 个连续保持寄存器。
        /// 字序由 AppConstants.FloatWordOrder 决定（默认 CDAB=低字在前）。
        /// 约束：起始地址必须为偶数（双寄存器对齐，加载器校验 E06）。
        /// </summary>
        Float,

        /// <summary>
        /// 32 位无符号整数，占用 2 个连续保持寄存器。
        /// 约束：起始地址必须为偶数（双寄存器对齐，加载器校验 E06）。
        /// </summary>
        UInt,
    }

    /// <summary>
    /// 数据点访问权限。由 TagTable.csv 的 Access 列解析而来。
    /// </summary>
    public enum TagAccess
    {
        /// <summary>只读：PLC→上位机的状态反馈（如泵故障、运行反馈）。写入会被 ControlService 拒绝。</summary>
        R,

        /// <summary>可读写：控制类标签（泵启停、模式、阀门）。UI 必须存在对应控制控件（校验 E08）。</summary>
        RW,
    }

    /// <summary>
    /// Modbus 寄存器地址映射模型。
    /// 定义了一个数据点如何在 PLC 的 Modbus 地址空间中定位。
    ///
    /// 数据来源：configFiles/TagTable.csv（由 TagTableLoader 加载并做 fail-fast 校验）。
    ///
    /// 示例一行数据：
    ///   CurWaterPos | 32 | float | R | 0.1 | m
    ///   含义：名为"当前储水池水位"的点，位于保持寄存器地址 32（占用 32~33 两个寄存器），
    ///         32 位浮点类型，只读，缩放系数 0.1（原始值×0.1=工程值），单位为米。
    ///
    /// 读取方式由 Type 决定：
    ///   Coil  → ReadCoilsAsync(0x01) / WriteSingleCoilAsync(0x05)
    ///   其他  → ReadHoldingRegistersAsync(0x03) / WriteSingleRegisterAsync(0x06)
    /// </summary>
    public class TagAddress
    {
        /// <summary>
        /// 数据点逻辑名称，全局唯一（加载器校验 E02/E03）。
        /// 命名约定：
        ///   - 水位/流量/压力等模拟量：CurWaterPos, MainOutFlow, MainOutPressure
        ///   - 分管量：Pressure-1, Flow-1（允许连字符，与界面 ParaName 一致）
        ///   - 水泵相关：Pump1_Run, Pump1_Fault, Pump1_Mode, Pump1_S(转速), Pump1_P(功率), Pump1_F(频率)
        ///   - 阀门相关：MainValve, Valve1, Valve2, Valve3
        ///   - 环境数据：CurTemperature, SoilHumidity, Precipitation, ActualWaters, PlanWaters
        /// </summary>
        public string TagName { get; set; } = string.Empty;

        /// <summary>
        /// Modbus 寄存器/线圈地址（十进制，0~65535，加载器校验 E04）。
        /// 对于保持寄存器：是寄存器编号（如 32 表示 40033 寄存器，Modbus 从 0 开始编址）
        /// 对于线圈：是线圈编号（如 0 表示第一个线圈）
        /// </summary>
        public int Address { get; set; }

        /// <summary>数据点类型（coil/ushort/float/uint），加载器校验 E05。</summary>
        public TagType Type { get; set; } = TagType.UShort;

        /// <summary>访问权限（R/RW）。RW 标签在 UI 中必须有对应控制控件（校验 E08）。</summary>
        public TagAccess Access { get; set; } = TagAccess.R;

        /// <summary>
        /// Modbus 从站单元标识符（Unit ID）。
        /// 在 Modbus TCP 中，这个字段标识目标设备，默认 1 对应大多数单 PLC 场景。
        /// 在 DataPollingService.GroupAddresses 中会按 UnitId 分组，
        /// 确保不同从站的寄存器不会混在同一个 Modbus 请求中。
        /// </summary>
        public byte UnitId { get; set; } = 1;

        /// <summary>是否为线圈类型（由 Type 派生，保留此便捷属性供既有调用方使用）。</summary>
        public bool IsCoil => Type == TagType.Coil;

        /// <summary>
        /// 该点占用的 Modbus 单元数量（用于分组批量读取与结果映射）：
        ///   Coil → 1（1 个线圈位）
        ///   UShort → 1（1 个寄存器）
        ///   Float / UInt → 2（2 个连续寄存器）
        /// </summary>
        public int RegisterCount => (Type == TagType.Float || Type == TagType.UInt) ? 2 : 1;

        /// <summary>
        /// 缩放系数，将原始寄存器值转换为工程单位值。
        /// Value = RawValue × Scale（必须大于 0，加载器校验 E07）。
        /// 例如：
        ///   - Scale=0.1：原始值 150 → 工程值 15.0（水位 15 米）
        ///   - Scale=0.01：压力原始值 3500 → 工程值 35.00（需结合设备量程确认）
        /// </summary>
        public double Scale { get; set; } = 1.0;

        /// <summary>
        /// 工程单位字符串，如 "m"、"m³"、"Mpa"、"℃"、"%"、"Hz" 等。
        /// 会复制到 DataPoint.Unit 中，方便 UI 直接拼接显示。
        /// </summary>
        public string Unit { get; set; } = string.Empty;

        /// <summary>逻辑分组（工艺/泵1/泵2/泵3/分管1/分管2/分管3/环境/阀门），供文档与界面索引，不参与通信。</summary>
        public string Group { get; set; } = string.Empty;

        /// <summary>显示量程下限（可选，模块 E2：UMeter/UCircleMeter 指针的工程含义）</summary>
        public double? MinValue { get; set; }

        /// <summary>显示量程上限（可选）</summary>
        public double? MaxValue { get; set; }

        /// <summary>中文描述，来源 TagTable.csv 的 Description 列。</summary>
        public string Description { get; set; } = string.Empty;
    }
}
