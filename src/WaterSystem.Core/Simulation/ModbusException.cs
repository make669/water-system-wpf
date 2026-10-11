namespace Intelligent_water_storage_system.Simulation
{
    /// <summary>
    /// Modbus 异常响应（协议层）。
    /// 被 ProtocolProcessor 捕获后生成 功能码|0x80 + 异常码 的异常响应帧。
    /// </summary>
    public class ModbusException(byte code) : Exception($"Modbus 异常码 0x{code:X2}")
    {
        /// <summary>异常码：0x01 非法功能 0x02 非法地址 0x03 非法值</summary>
        public byte Code { get; } = code;
    }
}
