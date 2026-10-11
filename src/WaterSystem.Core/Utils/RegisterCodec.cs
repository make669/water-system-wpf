using Intelligent_water_storage_system.Model;

namespace Intelligent_water_storage_system.Utils
{
    /// <summary>
    /// 寄存器值解码器（纯函数，无网络/UI 依赖，便于单元测试）。
    ///
    /// 【职责】
    /// 将 Modbus 批量读取返回的原始寄存器/线圈数组，按 TagAddress.Type 解码为工程原始值：
    ///   Coil   → 0/1
    ///   UShort → 寄存器原值
    ///   UInt   → 2 个寄存器拼合为 32 位无符号整数
    ///   Float  → 2 个寄存器拼合为 IEEE 754 单精度浮点
    /// 缩放（Value = RawValue × Scale）由调用方（DataPollingService）负责，本类只管"原始值"。
    ///
    /// 【字序问题】
    /// Modbus 32 位值没有统一字序，各品牌 PLC 不一致：
    ///   ABCD（高字在前）：寄存器[n]=高 16 位，寄存器[n+1]=低 16 位
    ///   CDAB（低字在前，字交换）：寄存器[n]=低 16 位，寄存器[n+1]=高 16 位（西门子系常见）
    /// 字序作为参数传入（模块 F 可测化：默认 = AppConstants.FloatWordOrder，编译期常量，
    /// 生产调用点零改动；单元测试显式传 "CDAB"/"ABCD" 覆盖双字序，不再依赖可变全局）。
    /// 真实设备联调时若 float 读数异常（数量级离谱），将 AppConstants.FloatWordOrder
    /// 改为 ABCD 即可，无需改代码逻辑。
    /// </summary>
    public static class RegisterCodec
    {
        /// <summary>低 16 位字在前（CDAB 字序）判定。仅 "CDAB" 视为低字在前，其余（含 ABCD）一律高字在前——维持原语义。</summary>
        private static bool IsLowWordFirst(string wordOrder) => wordOrder == "CDAB";

        /// <summary>
        /// 从寄存器数组的 offset 位置解码一个数据点的原始值。
        /// </summary>
        /// <param name="type">数据点类型</param>
        /// <param name="registers">批量读取返回的寄存器数组（线圈读取时为 0/1 数组）</param>
        /// <param name="offset">该点数据在数组中的起始下标</param>
        /// <param name="wordOrder">32 位值字序："CDAB"（默认，低字在前）或 "ABCD"（高字在前）</param>
        /// <returns>未缩放的原始值。float 返回浮点值，coil 返回 0/1，其余返回整数</returns>
        public static double Decode(TagType type, ushort[] registers, int offset, string wordOrder = AppConstants.FloatWordOrder)
        {
            return type switch
            {
                TagType.Coil => registers[offset] != 0 ? 1 : 0,
                TagType.UShort => registers[offset],
                TagType.UInt => DecodeUInt32(registers, offset, wordOrder),
                TagType.Float => BitConverter.Int32BitsToSingle((int)DecodeUInt32(registers, offset, wordOrder)),
                _ => 0,
            };
        }

        /// <summary>
        /// 拼合 2 个寄存器为 32 位无符号整数（按 wordOrder 字序）。
        /// </summary>
        public static uint DecodeUInt32(ushort[] registers, int offset, string wordOrder = AppConstants.FloatWordOrder)
        {
            bool lowWordFirst = IsLowWordFirst(wordOrder);
            ushort lo = lowWordFirst ? registers[offset] : registers[offset + 1];
            ushort hi = lowWordFirst ? registers[offset + 1] : registers[offset];
            return (uint)((hi << 16) | lo);
        }

        // ═══════════════════════════════════════════════
        // 编码方向（ ModbusSlaveSim 写入 / 单元测试往返验证用）
        // ═══════════════════════════════════════════════

        /// <summary>
        /// 将原始值按类型编码写入寄存器数组（coil 类型不在此处理，走 bool 通道）。
        /// 与 Decode 互为逆运算，字序一致。
        /// </summary>
        /// <param name="type">数据点类型（UShort/Float/UInt）</param>
        /// <param name="rawValue">未缩放的原始值</param>
        /// <param name="registers">目标寄存器数组</param>
        /// <param name="offset">写入起始下标</param>
        /// <param name="wordOrder">32 位值字序（默认 = AppConstants.FloatWordOrder）</param>
        public static void Encode(TagType type, double rawValue, ushort[] registers, int offset, string wordOrder = AppConstants.FloatWordOrder)
        {
            switch (type)
            {
                case TagType.UShort:
                    registers[offset] = (ushort)Math.Clamp(Math.Round(rawValue), 0, ushort.MaxValue);
                    break;
                case TagType.UInt:
                    EncodeUInt32((uint)Math.Clamp(Math.Round(rawValue), 0, uint.MaxValue), registers, offset, wordOrder);
                    break;
                case TagType.Float:
                    EncodeFloat32((float)rawValue, registers, offset, wordOrder);
                    break;
            }
        }

        /// <summary>按字序把 32 位值拆成 2 个寄存器（与 DecodeUInt32 互逆）。</summary>
        public static void EncodeUInt32(uint value, ushort[] registers, int offset, string wordOrder = AppConstants.FloatWordOrder)
        {
            bool lowWordFirst = IsLowWordFirst(wordOrder);
            ushort lo = (ushort)(value & 0xFFFF);
            ushort hi = (ushort)(value >> 16);
            if (lowWordFirst)
            {
                registers[offset] = lo;
                registers[offset + 1] = hi;
            }
            else
            {
                registers[offset] = hi;
                registers[offset + 1] = lo;
            }
        }

        /// <summary>把 IEEE 754 浮点拆成 2 个寄存器（与 Decode 的 Float 分支互逆）。</summary>
        public static void EncodeFloat32(float value, ushort[] registers, int offset, string wordOrder = AppConstants.FloatWordOrder)
            => EncodeUInt32(BitConverter.SingleToUInt32Bits(value), registers, offset, wordOrder);
    }
}
