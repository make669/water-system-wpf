namespace Intelligent_water_storage_system.Model
{
    /// <summary>
    /// Modbus TCP 通信配置模型。
    /// 对应配置文件 configFiles/Communication.txt 中的内容，格式为：
    ///   IP:192.168.100.50
    ///   Port:502
    /// 该模型只保存最基本的连接参数（IP 地址和端口号），
    /// 由 Utility.LoadCommunicationConfig() 从文件中读取后，
    /// 传递给 ModbusClient 构造函数建立 TCP 连接。
    /// </summary>
    public class CommunicationSet
    {
        /// <summary>PLC 的 IP 地址，例如 "192.168.100.50"</summary>
        public string IP { get; set; } = string.Empty;

        /// <summary>
        /// Modbus TCP 端口号，标准端口为 502。
        /// 如果配置文件中未指定或解析失败，FrmMain 中会使用默认值 502。
        /// </summary>
        public int Port { get; set; }
    }
}
