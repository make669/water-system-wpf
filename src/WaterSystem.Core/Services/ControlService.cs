using Intelligent_water_storage_system.Model;

namespace Intelligent_water_storage_system.Services
{
    /// <summary>
    /// 水泵和阀门控制服务。
    ///
    /// 【核心职责】
    /// 将上层 UI 的控制意图（"启动水泵 1"、"关闭阀门 2"）转换为
    /// Modbus 写线圈指令，发送给 PLC 执行。
    ///
    /// 【调用链路】
    ///   FrmMain.HandleControlClick()
    ///     → ControlService.WriteCoilAsync(tagName, value)
    ///       → ModbusClient.WriteSingleCoilAsync(unitId, address, value)
    ///         → Modbus TCP 0x05 功能码请求 → PLC 执行
    ///
    /// 【安全机制】
    ///   FrmMain 中在调用 WriteCoilAsync 之前，会先弹出 Confirm 操作确认对话框，
    ///   用户点击"是"才真正执行写入操作，防止误触导致设备误动作。
    /// </summary>
    public class ControlService
    {
        /// <summary>Modbus TCP 客户端，用于发送写指令</summary>
        private readonly ModbusClient _client;

        /// <summary>
        /// 标签名称 → TagAddress 的映射字典。
        /// 从 List&lt;TagAddress&gt; 通过 ToDictionary 转换而来，
        /// 用于根据标签名称快速查找对应的 Modbus 地址和从站 ID。
        /// 例如：_tagMap["Pump1_Run"] → TagAddress { Address=200, UnitId=1, IsCoil=true }
        /// </summary>
        private readonly Dictionary<string, TagAddress> _tagMap;

        /// <summary>
        /// 错误事件。当控制操作失败时触发，将错误信息通知 FrmMain。
        /// 失败场景：
        ///   - PLC 未连接
        ///   - 找不到指定的标签名称
        ///   - Modbus 写入过程中发生异常
        /// </summary>
        public event Action<string>? OnError;

        /// <summary>
        /// 构造函数。接收 Modbus 客户端和标签列表。
        /// </summary>
        /// <param name="client">Modbus TCP 客户端实例</param>
        /// <param name="tags">所有数据点的地址配置列表（从 Excel 加载）</param>
        public ControlService(ModbusClient client, List<TagAddress> tags)
        {
            _client = client;
            // 将列表转换为字典，Key=TagName, Value=TagAddress，方便 O(1) 查找。
            // 大小写不敏感：与 TagTableLoader / UI 绑定核对的比较口径保持一致
            _tagMap = tags.ToDictionary(t => t.TagName, t => t, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 异步写入线圈值，用于控制水泵启停或阀门开关。
        ///
        /// 【执行流程】
        ///   1. 检查 PLC 连接状态，未连接则报错返回
        ///   2. 根据 tagName 查找对应的 TagAddress，找不到则报错返回
        ///   3. 调用 ModbusClient.WriteSingleCoilAsync() 发送 Modbus 0x05 写线圈指令
        ///   4. 返回写入结果（true=成功，false=失败）
        ///
        /// 【调用场景】
        ///   - FrmMain.HandleControlClick() 中用户点击水泵开关/阀门开关时调用
        ///   - FrmMain.UiPollTimer_Tick() 中检测到自定义控件状态变化时调用
        /// </summary>
        /// <param name="tagName">标签名称，如 "Pump1_Run"、"MainValve"</param>
        /// <param name="value">true=置ON（启动/打开），false=置OFF（停止/关闭）</param>
        /// <returns>true=写入成功，false=写入失败</returns>
        public async Task<bool> WriteCoilAsync(string tagName, bool value)
        {
            // 安全检查 1：PLC 必须已连接
            if (!_client.IsConnected)
            {
                OnError?.Invoke("PLC 未连接，无法执行控制指令");
                return false;
            }

            // 安全检查 2：标签名称必须在地址配置中存在
            if (!_tagMap.TryGetValue(tagName, out var tag))
            {
                OnError?.Invoke($"找不到标签: {tagName}");
                return false;
            }

            // 安全检查 3：标签必须可写。R=只读状态反馈（如泵故障），写入属于越权操作
            if (tag.Access != TagAccess.RW)
            {
                OnError?.Invoke($"标签 {tagName} 为只读(R)，禁止写入");
                return false;
            }

            try
            {
                // 发送 Modbus 写单个线圈指令
                // tag.UnitId = 从站标识，tag.Address = 线圈地址
                await _client.WriteSingleCoilAsync(tag.UnitId, (ushort)tag.Address, value);
                return true;
            }
            catch (Exception ex)
            {
                // 写入过程中发生异常（超时、连接断开、PLC 返回异常码等）
                OnError?.Invoke($"写入失败 [{tagName}]: {ex.Message}");
                return false;
            }
        }
    }
}
