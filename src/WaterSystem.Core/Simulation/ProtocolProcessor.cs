namespace Intelligent_water_storage_system.Simulation
{
    /// <summary>
    /// Modbus 功能码处理器：解析请求 PDU → 操作 DataStore → 生成响应 PDU。
    ///
    /// 【支持的功能码】
    ///   0x01 读线圈、0x03 读保持寄存器、0x05 写单线圈、0x06 写单寄存器
    ///   （与上位机 ModbusClient 的功能码集合一致）
    ///
    /// 【异常响应】
    ///   非法功能 → 0x01；地址越界/对只读标签写入 → 0x02；
    ///   数量非法/线圈值非法 → 0x03。
    ///   异常响应 = 功能码|0x80 + 异常码，与真实 PLC 行为一致。
    /// </summary>
    public static class ProtocolProcessor
    {
        /// <summary>写单线圈（0x05）请求计数（T5.6 回写风暴专项取证面：无人操作时必须为 0）。</summary>
        public static long WriteCoilRequests;

        /// <summary>清零计数（测试/取证前调用）。</summary>
        public static void ResetCounters() => WriteCoilRequests = 0;

        /// <summary>处理一个请求 PDU，返回响应 PDU。</summary>
        public static byte[] Process(byte[] pdu, DataStore store, ScenarioEngine engine)
        {
            byte fc = pdu.Length > 0 ? pdu[0] : (byte)0;
            try
            {
                return fc switch
                {
                    0x01 => ReadCoils(pdu, store),
                    0x03 => ReadRegisters(pdu, store),
                    0x05 => WriteSingleCoil(pdu, store, engine),
                    0x06 => WriteSingleRegister(pdu, store),
                    _ => ExceptionPdu(fc, 0x01),
                };
            }
            catch (ModbusException ex)
            {
                return ExceptionPdu(fc, ex.Code);
            }
        }

        // ── 0x03 读保持寄存器：请求 = FC + 起始(2) + 数量(2)；响应 = FC + 字节数 + 数据 ──
        private static byte[] ReadRegisters(byte[] pdu, DataStore store)
        {
            if (pdu.Length < 5) throw new ModbusException(0x01);
            ushort start = Be16(pdu, 1);
            ushort qty = Be16(pdu, 3);
            if (qty < 1 || qty > 125) throw new ModbusException(0x03);
            if (start + (uint)qty > (uint)store.RegisterLimit) throw new ModbusException(0x02);

            ushort[] regs = store.ReadRegisters(start, qty);
            var resp = new byte[2 + qty * 2];
            resp[0] = 0x03;
            resp[1] = (byte)(qty * 2);
            for (int i = 0; i < qty; i++)
            {
                resp[2 + i * 2] = (byte)(regs[i] >> 8);
                resp[3 + i * 2] = (byte)(regs[i] & 0xFF);
            }
            return resp;
        }

        // ── 0x01 读线圈：响应数据按位打包，每字节 8 个线圈 ──
        private static byte[] ReadCoils(byte[] pdu, DataStore store)
        {
            if (pdu.Length < 5) throw new ModbusException(0x01);
            ushort start = Be16(pdu, 1);
            ushort qty = Be16(pdu, 3);
            if (qty < 1 || qty > 2000) throw new ModbusException(0x03);
            if (start + (uint)qty > (uint)store.CoilLimit) throw new ModbusException(0x02);

            bool[] coils = store.ReadCoils(start, qty);
            int byteCount = (qty + 7) / 8;
            var resp = new byte[2 + byteCount];
            resp[0] = 0x01;
            resp[1] = (byte)byteCount;
            for (int i = 0; i < qty; i++)
                if (coils[i])
                    resp[2 + i / 8] |= (byte)(1 << (i % 8));
            return resp;
        }

        // ── 0x05 写单线圈：0xFF00=ON 0x0000=OFF；响应原样回显请求 ──
        private static byte[] WriteSingleCoil(byte[] pdu, DataStore store, ScenarioEngine engine)
        {
            Interlocked.Increment(ref WriteCoilRequests);
            if (pdu.Length < 5) throw new ModbusException(0x01);
            ushort addr = Be16(pdu, 1);
            ushort val = Be16(pdu, 3);
            bool on = val switch
            {
                0xFF00 => true,
                0x0000 => false,
                _ => throw new ModbusException(0x03),
            };
            store.AssertCoilWritable(addr);
            store.WriteCoil(addr, on);
            // 泵联动：识别 PumpN_Run 写线圈，驱动转速/功率/频率缓升缓降
            engine.NotifyCoilWrite(addr, on);
            return (byte[])pdu.Clone();
        }

        // ── 0x06 写单寄存器：响应原样回显请求 ──
        private static byte[] WriteSingleRegister(byte[] pdu, DataStore store)
        {
            if (pdu.Length < 5) throw new ModbusException(0x01);
            ushort addr = Be16(pdu, 1);
            ushort val = Be16(pdu, 3);
            store.AssertRegisterWritable(addr);
            store.WriteRegister(addr, val);
            return (byte[])pdu.Clone();
        }

        private static byte[] ExceptionPdu(byte fc, byte code)
        {
            var resp = new byte[2];
            resp[0] = (byte)(fc | 0x80);
            resp[1] = code;
            return resp;
        }

        private static ushort Be16(byte[] b, int offset) => (ushort)((b[offset] << 8) | b[offset + 1]);
    }
}
