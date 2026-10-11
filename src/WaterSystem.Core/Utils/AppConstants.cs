namespace Intelligent_water_storage_system.Utils
{
    /// <summary>
    /// 应用级常量，集中管理所有散落的魔法数字。
    /// </summary>
    public static class AppConstants
    {
        /// <summary>PLC 轮询间隔（毫秒）</summary>
        public const int PollingIntervalMs = 1000;

        /// <summary>UI 控件状态轮询间隔（毫秒）</summary>
        public const int UiPollIntervalMs = 200;

        /// <summary>趋势图最大数据点数量（300点 × 1秒 = 5分钟）</summary>
        public const int MaxChartPoints = 300;

        /// <summary>Modbus TCP 连接超时（毫秒）</summary>
        public const int ConnectTimeoutMs = 5000;

        /// <summary>
        /// Modbus 单次收发超时（毫秒）。
        /// 必须短于连接超时；轮询间隔 1s 时 2s 已足够宽裕。
        /// （NetworkStream.ReadTimeout 对异步 Read 无效，超时靠 CancellationToken 实现）
        /// </summary>
        public const int IoTimeoutMs = 2000;

        /// <summary>错误信息显示时长（毫秒）</summary>
        public const int ErrorDisplayDurationMs = 3000;

        /// <summary>错误信息显示最大字符数</summary>
        public const int ErrorDisplayMaxLength = 40;

        /// <summary>
        /// 32 位值（float/uint）的字序。
        /// CDAB = 低字在前（默认，西门子系常见）；ABCD = 高字在前。
        /// 真实设备联调时 float 读数异常（数量级离谱）就切换此值，无需改代码。
        /// </summary>
        public const string FloatWordOrder = "CDAB";

        /// <summary>Modbus 单次批量读取的最大寄存器数（协议上限 125）</summary>
        public const int MaxRegistersPerRead = 125;

        /// <summary>Modbus 单次批量读取的最大线圈数（协议上限 2000）</summary>
        public const int MaxCoilsPerRead = 2000;
    }
}
