using Intelligent_water_storage_system.Model;
using Intelligent_water_storage_system.Utils;
using System.Timers;
using Timer = System.Timers.Timer;

namespace Intelligent_water_storage_system.Services
{
    /// <summary>
    /// 数据轮询服务。
    ///
    /// 【核心职责】
    /// 以固定时间间隔（默认 1000ms）周期性地从 PLC 读取所有数据点，
    /// 并将结果通过 OnDataReceived 事件广播给订阅者（FrmMain）。
    ///
    /// 【工作原理】
    ///   System.Timers.Timer 每隔 intervalMs 毫秒触发一次 Elapsed 事件，
    ///   在事件回调中启动一个 Task.Run 异步执行 PollAsync()。
    ///   使用 Interlocked.CompareExchange 防止重入——如果上一轮轮询尚未完成，本轮直接跳过。
    ///
    /// 【地址分组优化】
    ///   将连续的 Modbus 寄存器地址合并为一次批量读取请求（GroupAddresses），
    ///   大大减少网络通信次数。例如 10 个连续寄存器只需 1 次 Modbus 请求，而不是 10 次。
    ///   MaxRegisterGap=8 表示地址间隔不超过 8 时仍然合并（宁可多读几个空寄存器，减少请求次数）。
    ///
    /// 【断线重连机制】
    ///   采用指数退避策略：1s → 2s → 4s → 8s → 16s → 30s（上限）。
    ///   每次轮询时检查连接状态，如果断开则尝试重连，避免频繁重连浪费资源。
    /// </summary>
    public class DataPollingService : IDisposable
    {
        /// <summary>Modbus TCP 客户端实例，负责底层网络通信</summary>
        private readonly ModbusClient _client;

        /// <summary>
        /// 所有数据点的地址配置列表。
        /// 由 FrmMain 通过 TagTableLoader 加载 configFiles/TagTable.csv 并完成 fail-fast 校验后，
        /// 通过 SetTags() 注入。volatile 修饰确保 PollAsync 中读取时能获取最新引用。
        /// </summary>
        private volatile List<TagAddress> _tags = new();

        /// <summary>定时器，驱动周期性轮询</summary>
        private readonly Timer _timer;

        /// <summary>轮询间隔（毫秒），由构造函数传入，默认 1000ms</summary>
        private readonly int _intervalMs;

        /// <summary>
        /// 地址分组时允许的最大地址间隔已随分组算法迁入 <see cref="GroupPlanner.MaxRegisterGap"/>（模块 F）。
        /// </summary>

        /// <summary>是否正在轮询中</summary>
        private volatile bool _isPolling;

        /// <summary>上次上报的数据质量（true=正常），用于只在质量变化时触发事件</summary>
        private bool _lastQuality = true;

        /// <summary>
        /// 轮询防重入锁。使用 Interlocked 原子操作：
        ///   0 = 空闲（可以启动新一轮轮询）
        ///   1 = 忙（上一轮轮询还在执行中）
        /// 这比 lock 更轻量，因为只是对一个 int 做原子比较交换。
        /// </summary>
        private int _pollRunning;

        // ─── 指数退避重连参数 ───

        /// <summary>累计重连尝试次数，用于计算退避延迟时间</summary>
        private int _reconnectAttempt;

        /// <summary>上一次重连尝试的时间点，用于判断是否已等待足够长的时间</summary>
        private DateTime _lastReconnectAttempt = DateTime.MinValue;

        // ─── 事件 ───

        /// <summary>
        /// 数据接收事件。每次成功轮询后触发，参数为所有数据点的字典。
        /// 字典 Key = TagName（如 "CurWaterPos"），Value = DataPoint（包含缩放后的值和原始值）。
        /// FrmMain 订阅此事件来更新 UI、评估报警、绘制趋势图。
        /// </summary>
        public event Action<Dictionary<string, DataPoint>>? OnDataReceived;

        /// <summary>
        /// 连接状态变化事件。参数 true=已连接，false=已断开。
        /// 在连接成功、断开连接、轮询过程中检测到断线时触发。
        /// </summary>
        public event Action<bool>? OnConnectionChanged;

        /// <summary>错误事件，将异常信息以字符串形式通知订阅者</summary>
        public event Action<string>? OnError;

        /// <summary>
        /// 数据质量变化事件。true=数据恢复正常；false=数据中断（通信异常）。
        /// 供 UI 实现"数据过期"视觉状态（置灰/闪烁），模块 D 消费。
        /// </summary>
        public event Action<bool>? OnQualityChanged;

        /// <summary>调试日志回调，输出轮询读取的原始数据</summary>
        public Action<string, string>? OnLog { get; set; }

        /// <summary>是否正在轮询</summary>
        public bool IsPolling => _isPolling;

        /// <summary>底层 Modbus 客户端是否已连接</summary>
        public bool IsConnected => _client.IsConnected;

        /// <summary>当前累计重连尝试次数（顶栏"通信中断·第N次重连"的数据源，WPF T3.2 增量）。</summary>
        public int ReconnectAttempts => _reconnectAttempt;

        /// <summary>指数退避延迟（毫秒）：1s→2s→4s→8s→16s→30s 封顶（纯函数提自 PollAsync，可测）。</summary>
        public static int BackoffDelayMs(int attempts)
            => Math.Min(1000 * (1 << Math.Min(attempts, 5)), 30000);

        /// <summary>
        /// 最后一次成功轮询的时间点。
        /// UI 兜底判定：当前时间 - 此值 &gt; 3×轮询周期 即视为数据过期
        /// （即使质量事件丢失，时间戳也能兜住）。
        /// </summary>
        public DateTime LastGoodPollAt { get; private set; } = DateTime.Now;

        /// <summary>通信统计（来自底层 ModbusClient 的请求/成功/失败/字节/耗时）</summary>
        public CommStatistics Statistics => _client.Statistics;

        /// <summary>
        /// 构造函数。
        /// </summary>
        /// <param name="client">已创建的 Modbus 客户端实例</param>
        /// <param name="intervalMs">轮询间隔（毫秒），默认 1000ms = 每秒读取一次</param>
        public DataPollingService(ModbusClient client, int intervalMs = 1000)
        {
            _client = client;
            _intervalMs = intervalMs;
            _timer = new Timer(intervalMs);
            _timer.AutoReset = true; // 每次间隔到后自动重新触发

            // 定时器回调：每次间隔到期时执行
            _timer.Elapsed += (_, _) =>
            {
                // 防重入：如果上一轮轮询还没完成（_pollRunning==1），CompareExchange 返回的不是 0，直接跳过
                if (Interlocked.CompareExchange(ref _pollRunning, 1, 0) != 0)
                    return; // 上一轮轮询尚未完成，跳过本轮

                // 在线程池中异步执行轮询（不阻塞定时器线程）
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await PollAsync();
                    }
                    catch (Exception ex)
                    {
                        OnError?.Invoke($"轮询异常: {ex.Message}");
                    }
                    finally
                    {
                        // 轮询完成（无论成功还是失败），将标志位重置为 0，允许下一轮轮询
                        Interlocked.Exchange(ref _pollRunning, 0);
                    }
                });
            };
        }

        /// <summary>
        /// 注入标签配置（数据契约）。
        /// 由 FrmMain 启动时调用：TagTableLoader.Load() 加载并完成 fail-fast 校验，
        /// 只有零错误时才会走到这里，因此本方法收到的列表视为可信。
        /// </summary>
        /// <param name="tags">经校验的标签地址配置列表</param>
        public void SetTags(List<TagAddress> tags)
        {
            // 以新列表整体替换（volatile 引用交换），避免在轮询中途修改集合
            _tags = tags.ToList();
        }

        /// <summary>
        /// 异步连接到 PLC。
        /// 调用底层 ModbusClient.ConnectAsync()，并通过 OnConnectionChanged 事件通知外部。
        /// </summary>
        /// <returns>true=连接成功，false=连接失败</returns>
        public async Task<bool> ConnectAsync()
        {
            bool connected = await _client.ConnectAsync();
            OnConnectionChanged?.Invoke(connected);
            return connected;
        }

        /// <summary>
        /// 断开连接。
        ///   1. 停止轮询定时器
        ///   2. 断开底层 TCP 连接
        ///   3. 通知外部连接已断开
        /// </summary>
        public void Disconnect()
        {
            Stop();
            _client.Disconnect();
            OnConnectionChanged?.Invoke(false);
        }

        /// <summary>
        /// 启动轮询。只有在已加载 Tag 配置（_tags.Count > 0）时才启动。
        /// 启动后 Timer 开始按 intervalMs 间隔触发 Elapsed 事件。
        /// </summary>
        public void Start()
        {
            if (_tags.Count == 0)
            {
                OnError?.Invoke("警告: 标签配置为空，无法启动轮询 (请检查 DataAddressFile.xls)");
                return;
            }
            _timer.Start();
            _isPolling = true;
        }

        /// <summary>
        /// 停止轮询。Timer 停止触发，但不会断开 TCP 连接。
        /// 如果需要完全断开，应调用 Disconnect()。
        /// </summary>
        public void Stop()
        {
            _timer.Stop();
            _isPolling = false;
        }

        /// <summary>
        /// 核心轮询方法。每次定时器触发时执行一次。
        ///
        /// 【执行流程】
        ///   1. 检查连接状态，如果断开则尝试指数退避重连
        ///   2. 将 _tags 分为两类：保持寄存器（模拟量）和线圈（开关量）
        ///   3. 对每类地址进行连续分组（GroupAddresses），合并相邻地址
        ///   4. 按组发送 Modbus 批量读取请求
        ///   5. 将读取结果解析为 DataPoint 对象，填入 data 字典
        ///   6. 通过 OnDataReceived 事件将数据广播给订阅者
        ///
        /// 【异常处理】
        ///   如果读取过程中发生异常（如网络断开、PLC 无响应），
        ///   会触发 OnError 和 OnConnectionChanged 事件，并断开连接。
        /// </summary>
        private async Task PollAsync()
        {
            // ─── 第 1 步：检查连接状态，如果断开则尝试重连 ───
            if (!_client.IsConnected)
            {
                // 指数退避：1s→2s→4s→8s→16s→30s 封顶（提纯为 BackoffDelayMs 纯函数，T3.2 可测）
                int delayMs = BackoffDelayMs(_reconnectAttempt);

                // 如果距离上次重连尝试还没到退避时间，直接返回，不发起重连
                if ((DateTime.Now - _lastReconnectAttempt).TotalMilliseconds < delayMs)
                    return;

                _lastReconnectAttempt = DateTime.Now;
                try
                {
                    await _client.ConnectAsync();
                    if (_client.IsConnected)
                    {
                        // 重连成功，重置退避计数器
                        _reconnectAttempt = 0;
                        _client.Statistics.RecordReconnect();
                        OnConnectionChanged?.Invoke(true);
                        SetQuality(true);
                    }
                    else
                    {
                        // 重连失败，增加退避计数
                        _reconnectAttempt++;
                        OnError?.Invoke($"重连失败 (第{_reconnectAttempt}次): {_client.LastError}");
                        SetQuality(false);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    // 重连过程中发生异常（如 DNS 解析失败、目标端口拒绝等）
                    _reconnectAttempt++;
                    OnError?.Invoke($"重连异常 (第{_reconnectAttempt}次): {ex.Message}");
                    SetQuality(false);
                    return;
                }
            }

            // ─── 第 2 步：构建数据字典 ───
            var data = new Dictionary<string, DataPoint>();

            try
            {
                // 将所有标签分为两类：保持寄存器（模拟量）和线圈（开关量）
                var registerTags = _tags.Where(t => !t.IsCoil).ToList();
                var coilTags = _tags.Where(t => t.IsCoil).ToList();

                // ─── 第 3 步：读取保持寄存器（模拟量数据） ───
                if (registerTags.Any())
                {
                    // 将连续地址的寄存器合并为一组，减少 Modbus 请求次数
                    var groups = GroupAddresses(registerTags);
                    foreach (var g in groups)
                    {
                        // 该组的起始地址与读取跨度。
                        // 跨度 = 组内末端(最后标签地址+占用) - 起始地址，包含组内地址间隙；
                        // 不能按标签数累加，否则间隙会把后续标签的映射整体前移、漏读尾部寄存器
                        ushort startAddr = (ushort)g.Key;
                        var last = g.Last();
                        int span = last.Address + last.RegisterCount - g.Key;
                        if (span > AppConstants.MaxRegistersPerRead)
                            throw new InvalidOperationException($"分组读取跨度 {span} 超出协议上限 {AppConstants.MaxRegistersPerRead}");

                        // 用第一个标签的 UnitId 作为该组的从站标识（同组内 UnitId 相同）
                        byte unitId = g.First().UnitId;
                        ushort[] values = await _client.ReadHoldingRegistersAsync(unitId, startAddr, (ushort)span);

                        // 调试日志：输出读取的寄存器原始值
                        var rawDetails = string.Join(", ", values.Select((v, i) => $"[{startAddr + i}]={v}"));
                        OnLog?.Invoke("DEBUG", $"读寄存器 UnitId={unitId} Addr={startAddr}~{startAddr + span - 1}: {rawDetails}");

                        // 按绝对地址差映射：每个标签的数据在结果数组中的下标 = 该标签地址 - 组起始地址
                        foreach (var tag in g)
                        {
                            int offset = tag.Address - g.Key;
                            double raw = RegisterCodec.Decode(tag.Type, values, offset);
                            // 应用缩放系数：工程值 = 原始值 × Scale
                            double scaled = raw * tag.Scale;
                            data[tag.TagName] = new DataPoint
                            {
                                Name = tag.TagName,
                                Value = scaled,
                                RawValue = raw,
                                Unit = tag.Unit,
                                Timestamp = DateTime.Now
                            };
                        }
                    }
                }

                // ─── 第 4 步：读取线圈（开关量数据） ───
                if (coilTags.Any())
                {
                    var groups = GroupAddresses(coilTags);
                    foreach (var g in groups)
                    {
                        ushort startAddr = (ushort)g.Key;
                        var last = g.Last();
                        int span = last.Address + last.RegisterCount - g.Key; // 每个线圈点占 1 位
                        if (span > AppConstants.MaxCoilsPerRead)
                            throw new InvalidOperationException($"分组线圈跨度 {span} 超出协议上限 {AppConstants.MaxCoilsPerRead}");

                        byte unitId = g.First().UnitId;
                        bool[] values = await _client.ReadCoilsAsync(unitId, startAddr, (ushort)span);

                        // 调试日志：输出读取的线圈原始值
                        var coilDetails = string.Join(", ", values.Select((v, i) => $"[{startAddr + i}]={(v ? "ON" : "OFF")}"));
                        OnLog?.Invoke("DEBUG", $"读线圈 UnitId={unitId} Addr={startAddr}~{startAddr + span - 1}: {coilDetails}");

                        foreach (var tag in g)
                        {
                            int idx = tag.Address - g.Key;
                            // 线圈值转为 0/1：true→1（开），false→0（关）
                            data[tag.TagName] = new DataPoint
                            {
                                Name = tag.TagName,
                                Value = values[idx] ? 1 : 0,
                                RawValue = values[idx] ? 1 : 0,
                                Unit = tag.Unit,
                                Timestamp = DateTime.Now
                            };
                        }
                    }
                }

                // ─── 第 5 步：将数据广播给所有订阅者 ───
                LastGoodPollAt = DateTime.Now;
                SetQuality(true);
                OnDataReceived?.Invoke(data);
            }
            catch (Exception ex)
            {
                // 读取过程中发生异常（超时、连接断开、PLC 异常响应等）
                OnError?.Invoke($"轮询错误: {ex.Message}");
                SetQuality(false);
                OnConnectionChanged?.Invoke(false);
                // 安全断开连接（catch 中再包 try-catch，防止断开时也出错）
                try { _client.Disconnect(); } catch (Exception dex) { OnError?.Invoke($"断开连接异常: {dex.Message}"); }
            }
        }

        /// <summary>
        /// 上报数据质量变化（只在状态翻转时触发事件，避免每秒重复通知）。
        /// 同时维护 LastGoodPollAt 由调用方在成功路径设置。
        /// </summary>
        private void SetQuality(bool ok)
        {
            if (_lastQuality == ok) return;
            _lastQuality = ok;
            OnQualityChanged?.Invoke(ok);
        }

        /// <summary>
        /// 地址连续分组已提取为 <see cref="GroupPlanner.Group"/> 纯函数（模块 F 可测化，
        /// 行为不变——算法与行为不变量见 GroupPlanner 类注释）。
        /// </summary>
        /// <param name="tags">要分组的标签列表（调用方保证同组类型一致：全是线圈或全是寄存器）</param>
        /// <returns>分组后的列表，每个 AddressGroup 的 Key 为该组的起始地址</returns>
        private List<GroupPlanner.AddressGroup> GroupAddresses(List<TagAddress> tags) => GroupPlanner.Group(tags);

        /// <summary>
        /// 释放资源。
        ///   1. 停止并释放定时器
        ///   2. 等待当前轮询完成（最多等 2 秒），避免 Dispose 后还有异步操作在运行
        ///   3. 释放底层 ModbusClient
        /// </summary>
        public void Dispose()
        {
            _timer.Stop();
            _timer.Dispose();
            // 等待当前轮询完成（最多 2 秒）
            // 每 50ms 检查一次 _pollRunning 标志
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (_pollRunning != 0 && DateTime.UtcNow < deadline)
                Thread.Sleep(50);
            _client.Dispose();
        }
    }
}
