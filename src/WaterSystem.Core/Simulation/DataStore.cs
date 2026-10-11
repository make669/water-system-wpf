using Intelligent_water_storage_system.Model;
using Intelligent_water_storage_system.Utils;

namespace Intelligent_water_storage_system.Simulation
{
    /// <summary>
    /// 从站数据存储：模拟 PLC 的保持寄存器区和线圈区。
    ///
    /// 【地址空间】两个独立的一维数组各 65536 个单元：
    ///   _registers —— 保持寄存器（FC03 读 / FC06 写）
    ///   _coils     —— 线圈（FC01 读 / FC05 写）
    ///
    /// 【与标签表的关系】
    /// 启动时从 TagTable.csv（与上位机同源，由 TagTableLoader 加载）构建两个映射：
    ///   地址占用图 regOwner/coilOwner（用于写入权限校验：R 标签拒绝写入，回异常码）
    ///   有效地址范围 RegisterLimit/CoilLimit（读写越界回异常码 0x02 非法地址）
    ///
    /// 【工程值与原始值】
    /// 标签表存的是"工程值+缩放系数"，寄存器里存的是原始值（raw = 工程值 / Scale）。
    /// 编解码统一走 RegisterCodec（与上位机同源，保证 float 字序一致）。
    /// </summary>
    public class DataStore
    {
        private readonly ushort[] _registers = new ushort[65536];
        private readonly bool[] _coils = new bool[65536];
        private readonly object _gate = new();
        private readonly List<TagAddress> _tags;
        private readonly Dictionary<int, TagAddress> _regOwner = new();
        private readonly Dictionary<int, TagAddress> _coilOwner = new();

        /// <summary>寄存器区有效上限（排他）：读/写超出即异常码 0x02</summary>
        public int RegisterLimit { get; }

        /// <summary>线圈区有效上限（排他）</summary>
        public int CoilLimit { get; }

        /// <summary>全部标签（来自 TagTable.csv）</summary>
        public IReadOnlyList<TagAddress> Tags => _tags;

        /// <summary>
        /// 稳态初值（工程值）。选择"看起来像一套真实运行中的水站"的合理数值：
        /// 水位 2.5m（远离 1.0/300 报警线）、主管压力 0.35Mpa（远低于 10Mpa 报警线）。
        /// </summary>
        public static readonly Dictionary<string, double> DefaultValues = new()
        {
            ["Pressure-1"] = 0.30, ["Flow-1"] = 12.0,
            ["Pressure-2"] = 0.30, ["Flow-2"] = 12.0,
            ["Pressure-3"] = 0.30, ["Flow-3"] = 12.0,
            ["MainOutPressure"] = 0.35, ["MainOutFlow"] = 26.0,
            ["CurTemperature"] = 26.5, ["SoilHumidity"] = 45, ["Precipitation"] = 0,
            ["CurWaterPos"] = 2.5, ["PlanWaters"] = 120.0, ["ActualWaters"] = 85.0,
            // PumpN_F / PumpN_P / PumpN_S 缺省 0（停机），线圈全部默认 OFF
        };

        public DataStore(List<TagAddress> tags)
        {
            _tags = tags;

            // 构建地址占用图与有效范围
            int regLimit = 0, coilLimit = 0;
            foreach (var tag in tags)
            {
                if (tag.IsCoil)
                {
                    _coilOwner[tag.Address] = tag;
                    coilLimit = Math.Max(coilLimit, tag.Address + 1);
                }
                else
                {
                    for (int a = tag.Address; a < tag.Address + tag.RegisterCount; a++)
                        _regOwner[a] = tag;
                    regLimit = Math.Max(regLimit, tag.Address + tag.RegisterCount);
                }
            }
            RegisterLimit = regLimit;
            CoilLimit = coilLimit;

            // 按默认值初始化（停机稳态）
            foreach (var tag in tags)
                WriteEngineering(tag, DefaultValues.GetValueOrDefault(tag.TagName, 0.0));
        }

        // ═══════════════════════════════════════════════
        // 工程值读写（ScenarioEngine / set 命令使用）
        // ═══════════════════════════════════════════════

        /// <summary>按工程值写入一个点：raw = 工程值 / Scale 后编码进寄存器/线圈</summary>
        public void WriteEngineering(TagAddress tag, double engineering)
        {
            lock (_gate)
            {
                if (tag.IsCoil)
                {
                    _coils[tag.Address] = engineering != 0;
                    return;
                }
                RegisterCodec.Encode(tag.Type, engineering / tag.Scale, _registers, tag.Address);
            }
        }

        /// <summary>读取一个点的工程值（原始值 × Scale）</summary>
        public double ReadEngineering(TagAddress tag)
        {
            lock (_gate)
            {
                if (tag.IsCoil)
                    return _coils[tag.Address] ? 1 : 0;
                return RegisterCodec.Decode(tag.Type, _registers, tag.Address) * tag.Scale;
            }
        }

        /// <summary>按名称查找标签（找不到返回 null）</summary>
        public TagAddress? FindTag(string tagName) =>
            _tags.FirstOrDefault(t => t.TagName.Equals(tagName, StringComparison.OrdinalIgnoreCase));

        // ═══════════════════════════════════════════════
        // 寄存器/线圈级访问（ProtocolProcessor 使用）
        // ═══════════════════════════════════════════════

        public ushort[] ReadRegisters(int start, int count)
        {
            lock (_gate)
            {
                var result = new ushort[count];
                Array.Copy(_registers, start, result, 0, count);
                return result;
            }
        }

        public bool[] ReadCoils(int start, int count)
        {
            lock (_gate)
            {
                var result = new bool[count];
                Array.Copy(_coils, start, result, 0, count);
                return result;
            }
        }

        public void WriteRegister(int address, ushort value)
        {
            lock (_gate) _registers[address] = value;
        }

        public void WriteCoil(int address, bool value)
        {
            lock (_gate) _coils[address] = value;
        }

        /// <summary>校验寄存器地址可写：必须在标签表定义内且 Access=RW，否则异常码 0x02</summary>
        public void AssertRegisterWritable(int address)
        {
            if (!_regOwner.TryGetValue(address, out var tag) || tag.Access != TagAccess.RW)
                throw new ModbusException(0x02);
        }

        /// <summary>校验线圈地址可写：必须在标签表定义内且 Access=RW，否则异常码 0x02</summary>
        public void AssertCoilWritable(int address)
        {
            if (!_coilOwner.TryGetValue(address, out var tag) || tag.Access != TagAccess.RW)
                throw new ModbusException(0x02);
        }

        /// <summary>查找占用某线圈地址的标签（供泵联动识别 PumpN_Run 写入）</summary>
        public TagAddress? FindCoilOwner(int address) =>
            _coilOwner.TryGetValue(address, out var tag) ? tag : null;
    }
}
