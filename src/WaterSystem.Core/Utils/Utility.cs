using System.IO;
using Intelligent_water_storage_system.Model;

namespace Intelligent_water_storage_system.Utils
{
    /// <summary>
    /// 通用工具类。包含从配置文件读取通信参数的方法。
    ///
    /// 【在本项目中的用途】
    ///   只有一个方法 LoadCommunicationConfig()，用于读取 PLC 通信配置文件。
    ///   FrmMain.LoadCommunicationConfig() 调用此方法获取 IP 和端口，
    ///   然后创建 ModbusClient 实例。
    /// </summary>
    public static class Utility
    {
        /// <summary>
        /// 从 Communication.txt 文件加载 Modbus TCP 通信配置。
        ///
        /// 【配置文件格式】configFiles/Communication.txt
        ///   每行为 Key:Value 格式，例如：
        ///     IP:127.0.0.1
        ///     Port:502
        ///
        /// 【解析逻辑】
        ///   1. 逐行读取文件
        ///   2. 跳过空行
        ///   3. 用 LastIndexOf(':') 分割 Key 和 Value
        ///      （用 LastIndexOf 而不是 Split(':') 是因为 IP 地址中可能包含冒号，
        ///        如 IPv6 地址 "::1"，虽然当前只支持 IPv4）
        ///   4. 根据 Key 填充 CommunicationSet 的对应字段
        ///
        /// 【容错处理】
        ///   - 文件不存在：返回默认的 CommunicationSet（IP=""，Port=0）
        ///   - 行格式不正确（没有冒号分隔符）：跳过该行
        ///   - Port 解析失败：保持默认值 0，FrmMain 中会使用 502 作为兜底
        /// </summary>
        /// <param name="filePath">配置文件的完整路径，如 "...\configFiles\Communication.txt"</param>
        /// <returns>解析后的通信配置对象</returns>
        public static CommunicationSet LoadCommunicationConfig(string filePath)
        {
            var config = new CommunicationSet();

            if (!File.Exists(filePath))
                return config; // 文件不存在，返回默认配置

            string[] lines = File.ReadAllLines(filePath);
            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                // 用 LastIndexOf(':') 分割，避免 IP 地址中冒号的干扰
                int sepIndex = line.LastIndexOf(':');
                if (sepIndex <= 0) continue; // 没有分隔符或分隔符在行首，跳过

                string key = line.Substring(0, sepIndex).Trim();
                string value = line.Substring(sepIndex + 1).Trim();

                switch (key)
                {
                    case "IP":
                        config.IP = value;
                        break;
                    case "Port":
                        if (int.TryParse(value, out int port))
                            config.Port = port;
                        break;
                }
            }

            return config;
        }
    }
}
