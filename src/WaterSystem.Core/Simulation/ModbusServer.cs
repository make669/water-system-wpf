using System.Net;
using System.Net.Sockets;

namespace Intelligent_water_storage_system.Simulation
{
    /// <summary>
    /// Modbus TCP 从站服务：TcpListener + 每连接一个处理任务。
    ///
    /// 【帧处理】与上位机 ModbusClient 互补的正确实现：
    ///   先读 7 字节 MBAP 头 → 按其中的 Length 字段精确读余量 → 处理 → 组帧回发。
    ///   （这正是模块 C 要给上位机修的读法，模拟器端先按标准实现）
    ///
    /// 【通信劣化】（SimulatorOptions，用于复现/验证上位机的通信健壮性）
    ///   mute  —— 不回响应（连接保持），复现上位机异步读无超时的卡死
    ///   drop  —— 每 N 个响应丢 1 个
    ///   delay —— 延迟后回响应
    ///   frag  —— 先发"完整帧-4"字节，停 150ms 再发剩余 4 字节，
    ///            精确复现旧客户端"按总长盲读"的截断与后续错帧
    /// </summary>
    public class ModbusServer
    {
        private readonly DataStore _store;
        private readonly SimulatorOptions _options;
        private readonly ScenarioEngine _engine;

        public ModbusServer(DataStore store, SimulatorOptions options, ScenarioEngine engine)
        {
            _store = store;
            _options = options;
            _engine = engine;
        }

        /// <summary>启动监听循环。打印 READY 行供自动化脚本同步。</summary>
        public async Task RunAsync(int port, CancellationToken ct)
        {
            var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            Console.WriteLine($"READY port={port}");
            try
            {
                while (true)
                {
                    TcpClient client = await listener.AcceptTcpClientAsync(ct);
                    _ = Task.Run(() => HandleClientAsync(client, ct), ct);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                listener.Stop();
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
        {
            using (client)
            {
                NetworkStream stream = client.GetStream();
                try
                {
                    while (!ct.IsCancellationRequested && client.Connected)
                    {
                        // 读 7 字节 MBAP 头
                        byte[] header = await ReadExactAsync(stream, 7, ct);
                        ushort tid = (ushort)((header[0] << 8) | header[1]);
                        ushort length = (ushort)((header[4] << 8) | header[5]);
                        byte unitId = header[6];
                        if (length < 2) continue;

                        // 按 Length 精确读余量（PDU = Length - UnitId）
                        byte[] pdu = await ReadExactAsync(stream, length - 1, ct);

                        byte[] respPdu = ProtocolProcessor.Process(pdu, _store, _engine);
                        byte[] frame = BuildFrame(tid, unitId, respPdu);

                        // ── 通信劣化开关 ──
                        if (_options.Mute)
                        {
                            Console.WriteLine($"[mute] 丢弃响应 TID={tid}（连接保持）");
                            continue;
                        }
                        if (_options.ShouldDrop())
                        {
                            Console.WriteLine($"[drop] 丢弃响应 TID={tid}");
                            continue;
                        }
                        if (_options.ResponseDelayMs > 0)
                            await Task.Delay(_options.ResponseDelayMs, ct);

                        if (_options.Frag && frame.Length > 8)
                        {
                            // 先发"完整帧-4"字节 → 停 150ms → 再发尾部 4 字节
                            int cut = frame.Length - 4;
                            await stream.WriteAsync(frame.AsMemory(0, cut), ct);
                            Console.WriteLine($"[frag] TID={tid} 先发 {cut} 字节，150ms 后发剩余 {frame.Length - cut} 字节");
                            await Task.Delay(150, ct);
                            await stream.WriteAsync(frame.AsMemory(cut, frame.Length - cut), ct);
                        }
                        else
                        {
                            await stream.WriteAsync(frame, ct);
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (IOException) { /* 客户端断开，正常 */ }
                catch (ObjectDisposedException) { }
                catch (SocketException) { }
            }
        }

        private static byte[] BuildFrame(ushort tid, byte unitId, byte[] pdu)
        {
            var frame = new byte[7 + pdu.Length];
            frame[0] = (byte)(tid >> 8);
            frame[1] = (byte)(tid & 0xFF);
            // Protocol ID (frame[2..3]) = 0
            frame[4] = (byte)((pdu.Length + 1) >> 8);   // Length = UnitId + PDU
            frame[5] = (byte)((pdu.Length + 1) & 0xFF);
            frame[6] = unitId;
            Array.Copy(pdu, 0, frame, 7, pdu.Length);
            return frame;
        }

        /// <summary>精确读满 count 字节（TCP 流式，可能分多次到达）</summary>
        private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int count, CancellationToken ct)
        {
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
    }
}
