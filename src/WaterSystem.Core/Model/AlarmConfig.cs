using System.Text.Json;

namespace Intelligent_water_storage_system.Model
{
    /// <summary>
    /// 报警阈值配置模型。由 AlarmService 在构造时使用。
    ///
    /// 工作方式：AlarmService.Evaluate() 每次轮询时，将当前 DataPoint 值与这里的阈值比较，
    /// 超出阈值则生成报警（AlarmInfo），恢复正常则生成恢复通知。
    ///
    /// 阈值来源（产品级：现场可调，不改代码）：configFiles/AlarmConfig.json（可选）。
    ///   - 文件不存在 → 内置默认值（记一条 W 级日志，不阻断启动）；
    ///   - 文件存在但非法（JSON 坏 / 值越界）→ fail-fast 全量报出、拒绝启动（与标签表同一哲学）；
    ///   - 支持部分覆盖：只写需要覆盖的键，未写的键保持默认；容忍 // 注释与尾逗号。
    /// </summary>
    public class AlarmConfig
    {
        /// <summary>
        /// 水位下限报警阈值（单位：米）。
        /// 当 CurWaterPos < WaterLevelLow 时，触发 "LowWaterLevel" 报警。
        /// 低水位意味着蓄水池水量不足，可能影响供水能力。
        /// 默认值：1.0m
        /// </summary>
        public double WaterLevelLow { get; set; } = 1.0;

        /// <summary>
        /// 水位上限报警阈值（单位：米）。
        /// 当 CurWaterPos > WaterLevelHigh 时，触发 "HighWaterLevel" 报警。
        /// 高水位意味着蓄水池接近满池。
        /// 默认值：6.0m（设计稿量程 0~6m，超过即"水位过高"；旧值 300 永远无法触发，A4 收尾项修正）
        /// </summary>
        public double WaterLevelHigh { get; set; } = 6.0;

        /// <summary>
        /// 主管流量上限报警阈值（单位：m³）。
        /// 当 MainOutFlow > MaxFlow 时，触发 "HighFlow" 报警。
        /// 流量过大可能是管道破裂或阀门异常全开。
        /// 默认值：1000.0m³
        /// </summary>
        public double MaxFlow { get; set; } = 1000.0;

        /// <summary>
        /// 主管压力上限报警阈值（单位：Mpa）。
        /// 当 MainOutPressure > MaxPressure 时，触发 "HighPressure" 报警。
        /// 压力过高可能导致管道爆裂，属于安全关键报警。
        /// 默认值：10.0Mpa
        /// </summary>
        public double MaxPressure { get; set; } = 10.0;

        /// <summary>
        /// 水泵数量。AlarmService 会循环 1~PumpCount 检查每台泵的故障状态。
        /// 当前默认 3 台泵，对应 Tag 名称为 Pump1_Run, Pump1_Fault, Pump1_Mode 等。
        /// </summary>
        public int PumpCount { get; set; } = 3;

        /// <summary>
        /// 阀门名称列表。AlarmService 用这些名称从 DataPoint 字典中查找阀门状态。
        /// 默认包含：MainValve（主管阀门）、Valve1、Valve2、Valve3
        /// 这些名称必须与 DataAddressFile.xls 中的 TagName 一致。
        /// </summary>
        public string[] ValveNames { get; set; } = ["MainValve", "Valve1", "Valve2", "Valve3"];

        /// <summary>
        /// 加载报警阈值。错误码 [A01]~[A09]，警告 [AW1]；与 TagTableLoader 相同的 fail-fast 语义。
        /// </summary>
        public static AlarmConfig Load(string path, out List<string> errors, out List<string> warnings)
        {
            errors = new List<string>();
            warnings = new List<string>();

            if (!File.Exists(path))
            {
                warnings.Add("[AW1] 未找到报警配置 AlarmConfig.json，使用内置默认阈值（现场调参请复制 docs 示例到 configFiles/）");
                return new AlarmConfig();
            }

            AlarmConfig? config;
            try
            {
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                {
                    errors.Add("[A02] AlarmConfig.json 内容为空");
                    return new AlarmConfig();
                }
                config = JsonSerializer.Deserialize<AlarmConfig>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                });
            }
            catch (JsonException ex)
            {
                errors.Add($"[A01] AlarmConfig.json 不是合法 JSON：{ex.Message}");
                return new AlarmConfig();
            }

            if (config == null)
            {
                errors.Add("[A02] AlarmConfig.json 内容为空");
                return new AlarmConfig();
            }

            if (config.WaterLevelLow <= 0)
                errors.Add($"[A03] WaterLevelLow({config.WaterLevelLow}) 必须 > 0");
            if (config.WaterLevelHigh <= config.WaterLevelLow)
                errors.Add($"[A04] WaterLevelHigh({config.WaterLevelHigh}) 必须 > WaterLevelLow({config.WaterLevelLow})");
            if (config.MaxFlow <= 0)
                errors.Add($"[A05] MaxFlow({config.MaxFlow}) 必须 > 0");
            if (config.MaxPressure <= 0)
                errors.Add($"[A06] MaxPressure({config.MaxPressure}) 必须 > 0");
            if (config.PumpCount is < 1 or > 10)
                errors.Add($"[A07] PumpCount({config.PumpCount}) 必须在 1~10");
            if (config.ValveNames == null || config.ValveNames.Length == 0 || config.ValveNames.Any(string.IsNullOrWhiteSpace))
                errors.Add("[A08] ValveNames 不能为空");
            else if (config.ValveNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() != config.ValveNames.Length)
                errors.Add("[A09] ValveNames 存在重复项");

            return config;
        }
    }
}
