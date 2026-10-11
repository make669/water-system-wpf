using Intelligent_water_storage_system.Model;
using Intelligent_water_storage_system.Utils;
using Xunit;

namespace IntelligentWaterStorage.Tests
{
    /// <summary>
    /// RegisterCodec 编解码（模块 F 组 2，8 条；设计依据 docs/模块F-设计.md 7-组2、4 章）。
    /// 缺陷 D1 修复实证：字序参数化后 CDAB/ABCD 双字序均可测。
    /// </summary>
    public class RegisterCodecTests
    {
        [Theory]
        [InlineData(new ushort[] { 0x0000 }, 0.0)]
        [InlineData(new ushort[] { 0x0001 }, 1.0)]
        [InlineData(new ushort[] { 0xFFFF }, 65535.0)]
        public void Decode_UShort_ReturnsRegisterValue(ushort[] registers, double expected)
        {
            Assert.Equal(expected, RegisterCodec.Decode(TagType.UShort, registers, 0));
        }

        [Theory]
        [InlineData(new ushort[] { 0x0000 }, 0.0)]
        [InlineData(new ushort[] { 0x0001 }, 1.0)]
        [InlineData(new ushort[] { 0xBEEF }, 1.0)]   // 非 0 即 1
        public void Decode_Coil_MapsNonZeroToOne(ushort[] registers, double expected)
        {
            Assert.Equal(expected, RegisterCodec.Decode(TagType.Coil, registers, 0));
        }

        [Fact]
        public void Decode_UInt_Cdab_JoinsLowWordFirst()
        {
            // CDAB（低字在前）：regs[0]=0x3344（低字），regs[1]=0x1122（高字）→ 0x11223344
            ushort[] registers = { 0x3344, 0x1122 };

            Assert.Equal(0x11223344u, RegisterCodec.DecodeUInt32(registers, 0, "CDAB"));
        }

        [Fact]
        public void Decode_UInt_Boundaries()
        {
            ushort[] zero = { 0x0000, 0x0000 };
            ushort[] max = { 0xFFFF, 0xFFFF };

            Assert.Equal(0u, RegisterCodec.DecodeUInt32(zero, 0, "CDAB"));
            Assert.Equal(0xFFFFFFFFu, RegisterCodec.DecodeUInt32(max, 0, "CDAB"));
        }

        [Fact]
        public void Decode_Float_KnownValue_Cdab()
        {
            // 1.0f = 0x3F800000：CDAB → regs[0]=低字 0x0000，regs[1]=高字 0x3F80
            ushort[] registers = { 0x0000, 0x3F80 };

            Assert.Equal(1.0, RegisterCodec.Decode(TagType.Float, registers, 0, "CDAB"), 6);
        }

        [Fact]
        public void Decode_Float_Abcd_HighWordFirst()
        {
            // ABCD（高字在前）：1.0f → regs[0]=0x3F80，regs[1]=0x0000（D1 修复实证：ABCD 可测）
            ushort[] registers = { 0x3F80, 0x0000 };

            Assert.Equal(1.0, RegisterCodec.Decode(TagType.Float, registers, 0, "ABCD"), 6);
        }

        [Fact]
        public void Decode_Float_Cdab_ByteArrangement()
        {
            ushort[] registers = { 0x0000, 0x3F80 };

            Assert.Equal(1.0, RegisterCodec.Decode(TagType.Float, registers, 0, "CDAB"), 6);
            // 同一寄存器按 ABCD 解释是另一个值（证明字序真的生效）
            Assert.NotEqual(1.0, RegisterCodec.Decode(TagType.Float, registers, 0, "ABCD"), 6);
        }

        [Theory]
        [InlineData(0.5)]
        [InlineData(2.5)]
        [InlineData(-12.75)]
        public void EncodeDecode_Float_RoundTrip_BothWordOrders(double value)
        {
            foreach (var order in new[] { "CDAB", "ABCD" })
            {
                var registers = new ushort[2];
                RegisterCodec.Encode(TagType.Float, value, registers, 0, order);
                Assert.Equal(value, RegisterCodec.Decode(TagType.Float, registers, 0, order), 6);
            }
        }

        [Theory]
        [InlineData(0u)]
        [InlineData(1u)]
        [InlineData(0x11223344u)]
        [InlineData(0xFFFFFFFFu)]
        public void EncodeDecode_UInt_RoundTrip_BothWordOrders(uint value)
        {
            foreach (var order in new[] { "CDAB", "ABCD" })
            {
                var registers = new ushort[2];
                RegisterCodec.EncodeUInt32(value, registers, 0, order);
                Assert.Equal(value, RegisterCodec.DecodeUInt32(registers, 0, order));
            }
        }

        [Fact]
        public void Encode_SplitsWordsAccordingToWordOrder()
        {
            var cdab = new ushort[2];
            RegisterCodec.EncodeFloat32(1.0f, cdab, 0, "CDAB");
            Assert.Equal(0x0000, cdab[0]);
            Assert.Equal(0x3F80, cdab[1]);

            var abcd = new ushort[2];
            RegisterCodec.EncodeUInt32(0x11223344, abcd, 0, "ABCD");
            Assert.Equal(0x1122, abcd[0]);
            Assert.Equal(0x3344, abcd[1]);
        }
    }
}
