namespace Intelligent_water_storage_system.Services
{
    /// <summary>
    /// Modbus TCP 帧构建与解析（纯函数，无网络/UI 依赖，便于单元测试）。
    ///
    /// 【MBAP 头（7 字节）】
    ///   [0-1] Transaction ID（事务标识，大端序，用于匹配请求与响应）
    ///   [2-3] Protocol ID（固定 0x0000）
    ///   [4-5] Length（其后字节数 = UnitId(1) + PDU）
    ///   [6]   Unit ID（从站地址）
    ///   [7..] PDU：功能码 + 数据
    ///
    /// 【本类的设计意义】
    /// 上位机与从站模拟器（tools/ModbusSlaveSim）共用同一套帧语义；
    /// 帧构建/解析抽成纯函数后，模块 F 的单元测试可以不经过 Socket 直接验证
    /// 字节序、异常帧识别等协议细节。
    /// </summary>
    public static class ModbusFrame
    {
        /// <summary>MBAP 头长度（字节）</summary>
        public const int MbapHeaderLength = 7;

        /// <summary>Modbus 协议规定的 PDU 最大长度（功能码 1 字节 + 数据 ≤ 252）</summary>
        public const int MaxPduLength = 253;

        /// <summary>
        /// 组装完整请求帧：MBAP 头 + PDU。
        /// </summary>
        public static byte[] BuildFrame(ushort transactionId, byte unitId, byte[] pdu)
        {
            var frame = new byte[MbapHeaderLength + pdu.Length];
            frame[0] = (byte)(transactionId >> 8);
            frame[1] = (byte)(transactionId & 0xFF);
            // frame[2..3] = Protocol ID，固定 0
            frame[4] = (byte)((pdu.Length + 1) >> 8);   // Length = UnitId + PDU
            frame[5] = (byte)((pdu.Length + 1) & 0xFF);
            frame[6] = unitId;
            Array.Copy(pdu, 0, frame, MbapHeaderLength, pdu.Length);
            return frame;
        }

        /// <summary>从 MBAP 头解析事务 ID（大端序）</summary>
        public static ushort ParseTransactionId(byte[] header) =>
            (ushort)((header[0] << 8) | header[1]);

        /// <summary>从 MBAP 头解析 Length 字段（= UnitId + PDU 的字节数）</summary>
        public static ushort ParseLength(byte[] header) =>
            (ushort)((header[4] << 8) | header[5]);

        /// <summary>判断功能码是否为异常响应（PLC 把功能码最高位置 1 表示异常）</summary>
        public static bool IsException(byte functionCode) => (functionCode & 0x80) != 0;

        /// <summary>异常码中文化（用于日志与错误提示）</summary>
        public static string DescribeException(byte code) => code switch
        {
            0x01 => "非法功能(0x01)",
            0x02 => "非法地址(0x02)",
            0x03 => "非法值(0x03)",
            0x04 => "从站故障(0x04)",
            0x05 => "确认(0x05)",
            0x06 => "从站忙(0x06)",
            0x0A => "网关路径不可用(0x0A)",
            0x0B => "网关目标无响应(0x0B)",
            _ => $"异常码 0x{code:X2}",
        };
    }
}
