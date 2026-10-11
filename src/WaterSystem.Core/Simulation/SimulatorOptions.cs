namespace Intelligent_water_storage_system.Simulation
{
    /// <summary>
    /// 模拟器运行选项（通信劣化开关）。
    ///
    /// 【线程约定】命令线程写、IO 线程读；bool/int 的单字段读写无撕裂，
    /// 标记 volatile 保证跨线程可见性（演示工具，无需更重的同步）。
    ///
    /// 【用途】对应 docs/验收标准.md 的 T1.7（S6 通信劣化剧本）：
    ///   delay —— 响应延迟，验证客户端的等待行为
    ///   frag  —— 分片发送（响应先发"完整长度-4"字节，停 150ms 再发剩余 4 字节），
    ///            用于复现旧客户端"按总长盲读"的截断与错帧问题
    ///   mute  —— 收到请求不回应，用于复现旧客户端异步读无超时导致的轮询卡死
    ///   drop  —— 每 N 个响应丢 1 个，用于复现超时路径
    /// </summary>
    public class SimulatorOptions
    {
        /// <summary>响应延迟毫秒数，0=不延迟</summary>
        public volatile int ResponseDelayMs;

        /// <summary>分片发送开关</summary>
        public volatile bool Frag;

        /// <summary>静默开关（收到请求不回应）</summary>
        public volatile bool Mute;

        /// <summary>每 N 个响应丢弃 1 个，0=不丢弃</summary>
        public volatile int DropEveryN;

        private int _responseCounter;

        /// <summary>判断当前响应是否应当被丢弃（按全局计数取模）</summary>
        public bool ShouldDrop() => DropEveryN > 0 && Interlocked.Increment(ref _responseCounter) % DropEveryN == 0;
    }
}
