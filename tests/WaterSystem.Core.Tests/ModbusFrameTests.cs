using Intelligent_water_storage_system.Services;
using Xunit;

namespace IntelligentWaterStorage.Tests
{
    /// <summary>
    /// ModbusFrame 帧协议纯函数（模块 F 组 1，8 条；设计依据 docs/模块F-设计.md 7-组1）。
    /// 原 9.3 的"TID 回绕"用例由 TID 边界值取代：回绕逻辑在 ModbusClient 私有方法（Interlocked），
    /// 帧层无可测主体（缺陷 D2），边界值 0x0000/0xFFFF 覆盖组帧-解析往返。
    /// </summary>
    public class ModbusFrameTests
    {
        [Fact]
        public void BuildFrame_PacksBigEndianHeader_LengthEqualsPduPlusUnitId()
        {
            byte[] pdu = { 0x03, 0x00, 0x00, 0x00, 0x01 };

            byte[] frame = ModbusFrame.BuildFrame(0x0001, 0x01, pdu);

            Assert.Equal(7 + pdu.Length, frame.Length);
            Assert.Equal(0x00, frame[0]);                 // TID 高字节
            Assert.Equal(0x01, frame[1]);                 // TID 低字节
            Assert.Equal(0x00, frame[2]);                 // Protocol ID 固定 0
            Assert.Equal(0x00, frame[3]);
            Assert.Equal(0x00, frame[4]);                 // Length = PDU + UnitId = 6
            Assert.Equal(0x06, frame[5]);
            Assert.Equal(0x01, frame[6]);                 // UnitId
            Assert.Equal(pdu, frame[7..]);                // PDU 原样追加
        }

        [Fact]
        public void BuildFrame_CopiesPduCompletely()
        {
            byte[] pdu = Enumerable.Range(0, 253).Select(i => (byte)(i % 256)).ToArray();

            byte[] frame = ModbusFrame.BuildFrame(0x1234, 0x02, pdu);

            Assert.Equal(7 + 253, frame.Length);
            Assert.Equal(pdu, frame[7..]);
            Assert.Equal(0x12, frame[0]);
            Assert.Equal(0x34, frame[1]);
        }

        [Fact]
        public void ParseTransactionIdAndLength_RoundTrip()
        {
            byte[] pdu = { 0x06, 0x00, 0x01, 0x00, 0x02 };

            byte[] frame = ModbusFrame.BuildFrame(0xABCD, 0x11, pdu);

            Assert.Equal(0xABCD, ModbusFrame.ParseTransactionId(frame));
            Assert.Equal(pdu.Length + 1, ModbusFrame.ParseLength(frame));
        }

        [Fact]
        public void ParseLength_AcceptsBoundaryValues()
        {
            // 0（协议非法但帧层只负责解析，非法判定在 ModbusClient）与 MaxPduLength+1=254
            byte[] zero = { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01 };
            byte[] max = { 0x00, 0x00, 0x00, 0x00, 0x00, 0xFE, 0x01 };

            Assert.Equal(0, ModbusFrame.ParseLength(zero));
            Assert.Equal(254, ModbusFrame.ParseLength(max));
        }

        [Fact]
        public void BuildFrame_TidBoundaryValues_RoundTrip()
        {
            byte[] pdu = { 0x01, 0x00, 0x00, 0x00, 0x0D };

            foreach (ushort tid in new ushort[] { 0x0000, 0xFFFF, 0x7FFF })
            {
                byte[] frame = ModbusFrame.BuildFrame(tid, 0x01, pdu);
                Assert.Equal(tid, ModbusFrame.ParseTransactionId(frame));
            }
        }

        [Theory]
        [InlineData((byte)0x83, true)]   // 0x03 | 0x80
        [InlineData((byte)0x85, true)]   // 0x05 | 0x80
        [InlineData((byte)0x03, false)]
        [InlineData((byte)0x00, false)]
        [InlineData((byte)0xFF, true)]   // 0x7F | 0x80
        public void IsException_DetectsHighBit(byte functionCode, bool expected)
        {
            Assert.Equal(expected, ModbusFrame.IsException(functionCode));
        }

        [Theory]
        [InlineData((byte)0x01, "非法功能(0x01)")]
        [InlineData((byte)0x02, "非法地址(0x02)")]
        [InlineData((byte)0x03, "非法值(0x03)")]
        [InlineData((byte)0x04, "从站故障(0x04)")]
        [InlineData((byte)0x05, "确认(0x05)")]
        [InlineData((byte)0x06, "从站忙(0x06)")]
        [InlineData((byte)0x0A, "网关路径不可用(0x0A)")]
        [InlineData((byte)0x0B, "网关目标无响应(0x0B)")]
        public void DescribeException_MapsAllKnownCodes(byte code, string expected)
        {
            Assert.Equal(expected, ModbusFrame.DescribeException(code));
        }

        [Fact]
        public void DescribeException_UnknownCode_FallsBackToHexFormat()
        {
            Assert.Equal("异常码 0x77", ModbusFrame.DescribeException(0x77));
        }
    }
}
