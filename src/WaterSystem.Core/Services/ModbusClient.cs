using System.Diagnostics;
using System.Net.Sockets;
using Intelligent_water_storage_system.Utils;

namespace Intelligent_water_storage_system.Services
{
    /// <summary>Modbus 连接三态。</summary>
    public enum ModbusConnectionState
    {
        /// <summary>未连接（初始或主动断开后）</summary>
        Disconnected,

        /// <summary>已连接，可正常收发</summary>
        Connected,

        /// <summary>
        /// 已损坏：IO 超时 / 对端关闭 / 帧错位后进入。
        /// 与 Disconnected 的区别：表示"这次 TCP 会话已不可信"，
        /// DataPollingService 据此走指数退避重连。
        /// </summary>
        Broken,
    }

    /// <summary>
    /// 轻量级 Modbus TCP 客户端。
    ///
    /// 【Modbus TCP 协议简介】
    /// Modbus TCP 是工业领域最常用的通信协议之一，基于 TCP/IP 网络。
    /// 通信模型是"请求-响应"：上位机（本程序）发送请求帧，PLC 返回响应帧。
    ///
    /// 本类实现了 Modbus TCP 的核心功能：
    ///   - 读取保持寄存器（功能码 0x03）：读取模拟量，如水位、流量、温度等
    ///   - 读取线圈（功能码 0x01）：读取开关量，如水泵运行状态、阀门开关等
    ///   - 写单个寄存器（功能码 0x06）：写入模拟量设定值
    ///   - 写单个线圈（功能码 0x05）：写入开关量控制指令（启停泵、开关阀等）
    ///
    /// 【收发协议（模块 C 修复的核心）】
    ///   1. 两阶段精确读：先读 7 字节 MBAP 头 → 按其中的 Length 字段读余量。
    ///      旧实现按"期望总长"盲读，存在两个缺陷：
      ///      a) 读响应按 5+n 截断，TCP 分片时解析未到达的数据（错值/错位）；
    ///      b) 写指令的异常响应是 9 字节而非 12 字节回显，旧实现永远等不齐 → 永久挂起。
    ///   2. 每次收发使用 CancellationTokenSource 真超时（NetworkStream.ReadTimeout
    ///      对异步 Read 无效），超时转 Broken 并抛 TimeoutException。
    ///   3. 事务 ID 校验：响应 TID 必须等于请求 TID，不等说明帧已错位，转 Broken。
    ///   4. 异常响应（功能码|0x80）：解析异常码并中文化，连接本身不判死。
    ///
    /// 【线程安全设计】
    /// SemaphoreSlim(1,1) 串行化请求-响应；Disconnect() 无条件释放流，
    /// 并发中的读写在流释放后立即失败并转 Broken（旧实现 Wait(0) 拿不到锁会漏释放）。
    /// </summary>
    public class ModbusClient : IDisposable
    {
        /// <summary>TCP 客户端，负责与 PLC 建立网络连接</summary>
        private TcpClient? _tcpClient;

        /// <summary>网络数据流，通过 TcpClient.GetStream() 获取，用于收发数据</summary>
        private NetworkStream? _stream;

        /// <summary>信号量互斥锁：Modbus TCP 要求请求-响应严格配对，同一时刻只允许一个请求</summary>
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        /// <summary>事务 ID 自增计数器（1~0xFFFF 回绕），Interlocked 原子自增</summary>
        private int _transactionId;

        /// <summary>连接状态（volatile 保证跨线程可见性）</summary>
        private volatile ModbusConnectionState _state = ModbusConnectionState.Disconnected;

        /// <summary>连接三态（Disconnected / Connected / Broken）</summary>
        public ModbusConnectionState State => _state;

        /// <summary>是否已连接（Broken 视为未连接，触发上层重连）</summary>
        public bool IsConnected => _state == ModbusConnectionState.Connected;

        /// <summary>PLC 的 IP 地址</summary>
        public string IpAddress { get; private set; } = string.Empty;

        /// <summary>Modbus TCP 端口号（标准为 502）</summary>
        public int Port { get; private set; }

        /// <summary>最近一次连接失败的详细原因</summary>
        public string LastError { get; private set; } = string.Empty;

        /// <summary>通信统计（RX/TX 字节、成功率、响应耗时、重连次数）</summary>
        public CommStatistics Statistics { get; } = new();

        /// <summary>
        /// 构造函数，指定 PLC 的 IP 地址和端口。只保存参数，不发起连接。
        /// </summary>
        public ModbusClient(string ip, int port)
        {
            IpAddress = ip;
            Port = port;
        }

        /// <summary>
        /// 更新连接参数（IP 和端口）。只更新参数值，不自动断开或重连；
        /// 需要先 Disconnect() 再 ConnectAsync() 才会用新参数建连。
        /// </summary>
        public void UpdateConnection(string ip, int port)
        {
            IpAddress = ip;
            Port = port;
        }

        /// <summary>
        /// 异步连接到 PLC。
        /// 先断开旧连接，再以 timeoutMs 为上限发起 TCP 连接；
        /// 成功转 Connected，失败清理资源并返回 false。
        /// </summary>
        public async Task<bool> ConnectAsync(int timeoutMs = AppConstants.ConnectTimeoutMs)
        {
            Disconnect();
            _tcpClient = new TcpClient();
            try
            {
                using var cts = new CancellationTokenSource(timeoutMs);
                await _tcpClient.ConnectAsync(IpAddress, Port, cts.Token);
                _stream = _tcpClient.GetStream();
                _state = ModbusConnectionState.Connected;
                return true;
            }
            catch (Exception ex)
            {
                LastError = $"{IpAddress}:{Port} - {ex.GetType().Name}: {ex.Message}";
                Disconnect();
                return false;
            }
        }

        /// <summary>
        /// 断开与 PLC 的连接，无条件释放网络资源。
        ///
        /// 【并发语义】
        /// 若此刻有收发正在进行，流被释放后该次 ReadAsync/WriteAsync 立即失败
        /// （ObjectDisposed/IOException），SendReceiveAsync 捕获后转 Broken 并抛出
        /// "连接已断开"——断开语义即时生效，不存在旧实现"拿不到锁就漏释放"的竞态。
        /// </summary>
        public void Disconnect()
        {
            _state = ModbusConnectionState.Disconnected;
            _stream?.Dispose();
            _stream = null;
            _tcpClient?.Close();
            _tcpClient = null;
        }

        /// <summary>
        /// 读取保持寄存器（Modbus 功能码 0x03）。
        /// 适用场景：读取模拟量传感器数据，如水位、流量、温度、压力等。
        /// 返回值按大端序解析：每寄存器 2 字节，高字节在前。
        /// </summary>
        /// <param name="unitId">Modbus 从站单元标识（通常为 1）</param>
        /// <param name="startAddress">起始寄存器地址（十进制，如 32）</param>
        /// <param name="quantity">要读取的寄存器数量（1~125）</param>
        /// <returns>ushort 数组，每个元素对应一个寄存器的原始值</returns>
        public async Task<ushort[]> ReadHoldingRegistersAsync(byte unitId, ushort startAddress, ushort quantity)
        {
            if (quantity < 1 || quantity > AppConstants.MaxRegistersPerRead)
                throw new ArgumentOutOfRangeException(nameof(quantity), $"读取数量必须在 1~{AppConstants.MaxRegistersPerRead} 之间");

            byte[] respPdu = await SendReceiveAsync(unitId, BuildPdu(0x03, startAddress, quantity));
            ValidateResponse(respPdu, 0x03);
            if (respPdu[1] != quantity * 2)
                throw new InvalidOperationException($"Modbus 读取字节数不匹配: 期望 {quantity * 2}，实际 {respPdu[1]}");

            var values = new ushort[quantity];
            for (int i = 0; i < quantity; i++)
                values[i] = (ushort)((respPdu[2 + i * 2] << 8) | respPdu[3 + i * 2]);
            return values;
        }

        /// <summary>
        /// 读取线圈状态（Modbus 功能码 0x01）。
        /// 适用场景：读取开关量状态，如水泵运行/停止、阀门开/关等。
        /// 响应数据按位打包（每字节 8 个线圈，低位在前），本方法解包为 bool 数组。
        /// </summary>
        public async Task<bool[]> ReadCoilsAsync(byte unitId, ushort startAddress, ushort quantity)
        {
            if (quantity < 1 || quantity > AppConstants.MaxCoilsPerRead)
                throw new ArgumentOutOfRangeException(nameof(quantity), $"读取数量必须在 1~{AppConstants.MaxCoilsPerRead} 之间");

            byte[] respPdu = await SendReceiveAsync(unitId, BuildPdu(0x01, startAddress, quantity));
            ValidateResponse(respPdu, 0x01);
            int byteCount = (quantity + 7) / 8;
            if (respPdu[1] != byteCount)
                throw new InvalidOperationException($"Modbus 读取字节数不匹配: 期望 {byteCount}，实际 {respPdu[1]}");

            var values = new bool[quantity];
            for (int i = 0; i < quantity; i++)
                values[i] = ((respPdu[2 + i / 8] >> (i % 8)) & 1) == 1;
            return values;
        }

        /// <summary>
        /// 写单个寄存器（Modbus 功能码 0x06）。
        /// 适用场景：向 PLC 写入模拟量设定值。PLC 异常时抛 InvalidOperationException（含中文异常码）。
        /// </summary>
        public async Task WriteSingleRegisterAsync(byte unitId, ushort address, ushort value)
        {
            byte[] respPdu = await SendReceiveAsync(unitId, BuildPdu(0x06, address, value));
            ValidateResponse(respPdu, 0x06);
        }

        /// <summary>
        /// 写单个线圈（Modbus 功能码 0x05）。
        /// 适用场景：控制开关量（启停泵、开关阀），ControlService 的底层方法。
        /// 协议规定：线圈 ON = 0xFF00，OFF = 0x0000。
        /// </summary>
        public async Task WriteSingleCoilAsync(byte unitId, ushort address, bool value)
        {
            ushort coilValue = value ? (ushort)0xFF00 : (ushort)0x0000;
            byte[] respPdu = await SendReceiveAsync(unitId, BuildPdu(0x05, address, coilValue));
            ValidateResponse(respPdu, 0x05);
        }

        // ═══════════════════════════════════════════════
        // 核心收发：串行化 + 真超时 + 两阶段精确读 + 状态机 + 统计
        // ═══════════════════════════════════════════════

        /// <summary>
        /// 发送一个请求 PDU，接收并返回响应 PDU。
        ///
        /// 【执行流程】
        ///   1. 信号量串行化 → 取事务 ID → 组帧（ModbusFrame.BuildFrame）
        ///   2. 带超时写出，按响应 MBAP 头的 Length 两阶段精确读取
        ///   3. TID 校验 → 异常帧识别（中文化抛出，不判死连接）→ 成功计入统计
        ///   4. 任何 IO/超时/帧错位 → 转 Broken 并抛出带上下文的异常
        /// </summary>
        /// <param name="unitId">从站单元标识</param>
        /// <param name="pdu">请求 PDU（功能码 + 参数，不含 MBAP 头与 UnitId）</param>
        /// <returns>响应 PDU（功能码 + 数据）</returns>
        private async Task<byte[]> SendReceiveAsync(byte unitId, byte[] pdu)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Modbus 未连接");

            await _semaphore.WaitAsync();
            try
            {
                var stream = _stream
                    ?? throw new InvalidOperationException("Modbus 流已关闭");

                ushort tid = GetTransactionId();
                byte[] frame = ModbusFrame.BuildFrame(tid, unitId, pdu);
                long startTimestamp = Stopwatch.GetTimestamp();

                Statistics.RecordRequest();
                using var cts = new CancellationTokenSource(AppConstants.IoTimeoutMs);
                try
                {
                    await stream.WriteAsync(frame.AsMemory(), cts.Token);
                    Statistics.RecordSent(frame.Length);

                    // ── 两阶段精确读 ──
                    byte[] header = await ReadExactAsync(stream, ModbusFrame.MbapHeaderLength, cts.Token);
                    Statistics.RecordReceived(header.Length);

                    ushort respLength = ModbusFrame.ParseLength(header);
                    if (respLength < 2 || respLength > ModbusFrame.MaxPduLength + 1)
                    {
                        Statistics.RecordFailure();
                        MarkBroken();
                        throw new InvalidOperationException($"Modbus 响应长度非法: {respLength}（帧已错位，需重连）");
                    }

                    byte[] respPdu = await ReadExactAsync(stream, respLength - 1, cts.Token);
                    Statistics.RecordReceived(respPdu.Length);

                    ushort respTid = ModbusFrame.ParseTransactionId(header);
                    if (respTid != tid)
                    {
                        Statistics.RecordFailure();
                        MarkBroken();
                        throw new InvalidOperationException($"Modbus 事务 ID 不匹配: 请求 {tid}，响应 {respTid}（帧已错位，需重连）");
                    }

                    long rttMs = (Stopwatch.GetTimestamp() - startTimestamp) * 1000 / Stopwatch.Frequency;

                    // ── 异常响应：功能码|0x80 + 异常码，连接本身是好的，不判死 ──
                    if (ModbusFrame.IsException(respPdu[0]))
                    {
                        Statistics.RecordFailure();
                        byte code = respPdu.Length > 1 ? respPdu[1] : (byte)0;
                        throw new InvalidOperationException($"Modbus 异常响应: {ModbusFrame.DescribeException(code)}");
                    }

                    Statistics.RecordSuccess(rttMs);
                    return respPdu;
                }
                catch (OperationCanceledException)
                {
                    Statistics.RecordFailure();
                    MarkBroken();
                    throw new TimeoutException($"Modbus IO 超时（{AppConstants.IoTimeoutMs}ms 内未收完整响应）");
                }
                catch (IOException ex)
                {
                    Statistics.RecordFailure();
                    MarkBroken();
                    throw new InvalidOperationException("Modbus 连接已断开（IO 错误）", ex);
                }
                catch (ObjectDisposedException)
                {
                    Statistics.RecordFailure();
                    MarkBroken();
                    throw new InvalidOperationException("Modbus 连接已断开（流已释放）");
                }
                catch (SocketException ex)
                {
                    Statistics.RecordFailure();
                    MarkBroken();
                    throw new InvalidOperationException("Modbus 连接已断开（Socket 错误）", ex);
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// 精确读满 count 字节。TCP 是流式协议，可能分多次到达，必须循环读。
        /// 读到 0 字节表示对端关闭连接（IOException → Broken）。
        /// CancellationToken 真实生效于 Socket 层——这是异步超时的关键。
        /// </summary>
        private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int count, CancellationToken ct)
        {
            if (count == 0) return Array.Empty<byte>();
            var buffer = new byte[count];
            int read = 0;
            while (read < count)
            {
                int n = await stream.ReadAsync(buffer.AsMemory(read, count - read), ct);
                if (n == 0) throw new IOException("对端关闭连接");
                read += n;
            }
            return buffer;
        }

        /// <summary>构建请求 PDU：功能码 + 两个 16 位参数（大端序）</summary>
        private static byte[] BuildPdu(byte functionCode, ushort p1, ushort p2) => new byte[]
        {
            functionCode,
            (byte)(p1 >> 8), (byte)(p1 & 0xFF),
            (byte)(p2 >> 8), (byte)(p2 & 0xFF),
        };

        /// <summary>校验响应功能码与请求一致（异常帧已在 SendReceiveAsync 中过滤）</summary>
        private static void ValidateResponse(byte[] respPdu, byte expectedFc)
        {
            if (respPdu[0] != expectedFc)
                throw new InvalidOperationException(
                    $"Modbus 响应功能码不匹配: 期望 0x{expectedFc:X2}，实际 0x{respPdu[0]:X2}");
        }

        /// <summary>进入 Broken 状态（只在 Connected 时转换，幂等）</summary>
        private void MarkBroken()
        {
            if (_state == ModbusConnectionState.Connected)
                _state = ModbusConnectionState.Broken;
        }

        /// <summary>
        /// 获取下一个事务 ID。Interlocked 原子自增，范围 1~0xFFFF，超过后回绕到 1。
        /// </summary>
        private ushort GetTransactionId()
        {
            int id = Interlocked.Increment(ref _transactionId);
            if (id > 0xFFFF)
            {
                Interlocked.CompareExchange(ref _transactionId, 1, id);
                id = 1;
            }
            return (ushort)id;
        }

        /// <summary>释放所有资源。断开连接并释放信号量。</summary>
        public void Dispose()
        {
            Disconnect();
            _semaphore.Dispose();
        }
    }
}
