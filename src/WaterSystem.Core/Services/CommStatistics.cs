namespace Intelligent_water_storage_system.Services
{
    /// <summary>
    /// 通信统计（线程安全，全部 Interlocked 原子操作）。
    ///
    /// 【埋点位置】ModbusClient.SendReceiveAsync：
    ///   请求发出前 RecordRequest → 帧写出后 RecordSent → 响应收齐后 RecordReceived
    ///   → 成功 RecordSuccess(耗时)；任何失败路径 RecordFailure。
    ///   DataPollingService 在重连成功后 RecordReconnect。
    ///
    /// 【消费方】UI 的通信状态面板（模块 D）：RX/TX 字节、请求成功率、平均响应耗时、
    ///   重连次数。把"我懂通信"从嘴上说变成屏幕上看得见。
    /// </summary>
    public class CommStatistics
    {
        private long _txBytes;
        private long _rxBytes;
        private long _requests;
        private long _successes;
        private long _failures;
        private long _reconnects;
        private long _totalRttMs;
        private long _lastRttMs;

        /// <summary>累计发送字节数</summary>
        public long TxBytes => Interlocked.Read(ref _txBytes);

        /// <summary>累计接收字节数</summary>
        public long RxBytes => Interlocked.Read(ref _rxBytes);

        /// <summary>累计请求数</summary>
        public long RequestCount => Interlocked.Read(ref _requests);

        /// <summary>成功请求数</summary>
        public long SuccessCount => Interlocked.Read(ref _successes);

        /// <summary>失败请求数（超时/断开/协议错/PLC 异常响应）</summary>
        public long FailCount => Interlocked.Read(ref _failures);

        /// <summary>重连成功次数</summary>
        public long ReconnectCount => Interlocked.Read(ref _reconnects);

        /// <summary>最近一次请求的往返耗时（毫秒）</summary>
        public long LastRttMs => Interlocked.Read(ref _lastRttMs);

        /// <summary>平均往返耗时（毫秒，成功请求的算术平均）</summary>
        public double AvgRttMs => SuccessCount > 0
            ? (double)Interlocked.Read(ref _totalRttMs) / SuccessCount
            : 0;

        /// <summary>请求成功率（0~1）</summary>
        public double SuccessRate => RequestCount > 0 ? (double)SuccessCount / RequestCount : 0;

        public void RecordRequest() => Interlocked.Increment(ref _requests);

        public void RecordSent(int bytes)
        {
            if (bytes > 0) Interlocked.Add(ref _txBytes, bytes);
        }

        public void RecordReceived(int bytes)
        {
            if (bytes > 0) Interlocked.Add(ref _rxBytes, bytes);
        }

        public void RecordSuccess(long rttMs)
        {
            Interlocked.Increment(ref _successes);
            Interlocked.Add(ref _totalRttMs, rttMs);
            Interlocked.Exchange(ref _lastRttMs, rttMs);
        }

        public void RecordFailure() => Interlocked.Increment(ref _failures);

        public void RecordReconnect() => Interlocked.Increment(ref _reconnects);

        /// <summary>清零（通信面板"复位"按钮预留）</summary>
        public void Reset()
        {
            Interlocked.Exchange(ref _txBytes, 0);
            Interlocked.Exchange(ref _rxBytes, 0);
            Interlocked.Exchange(ref _requests, 0);
            Interlocked.Exchange(ref _successes, 0);
            Interlocked.Exchange(ref _failures, 0);
            Interlocked.Exchange(ref _reconnects, 0);
            Interlocked.Exchange(ref _totalRttMs, 0);
            Interlocked.Exchange(ref _lastRttMs, 0);
        }
    }
}
