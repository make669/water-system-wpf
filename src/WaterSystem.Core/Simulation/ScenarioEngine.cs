using Intelligent_water_storage_system.Model;

namespace Intelligent_water_storage_system.Simulation
{
    /// <summary>
    /// 剧情引擎：让模拟器"活起来"，提供可演示、可测试的现场行为。
    ///
    /// 【剧本】（对应 docs/设计文档.md 5.4 节）
    ///   S1 稳态    —— 全部值静止（默认）
    ///   S2 wave    —— 水位正弦波动（周期 120s，2.5±1.2m = 1.3~3.7m，避开 1.0/6.0 报警阈值留余量），
    ///                 实际用水量缓慢增长（《界面定稿设计》3.4：原 ±1.5 时谷值恰为 1.0m，浮点擦边偶发误报低水位）
    ///   S3 泵联动   —— 自动：写 PumpN_Run=ON → 延迟 3s → F/P/S 在 3s 内缓升至额定值；
    ///                 OFF → 3s 内缓降回 0（模拟变频器加速/自由停车）
    ///   S4 故障注入 —— fault N on/off 置位/复位 PumpN_Fault
    ///   S5 压力浪涌 —— surge on 时主管压力阶跃到 12.0Mpa（超上限 10，触发报警），off 恢复 0.35
    ///
    /// 【线程模型】
    /// 500ms 周期 Tick（Timer 线程池）+ 命令线程（SetScenario/SetSurge/SetPumpTarget），
    /// 全部状态变更在 _gate 内完成；对 DataStore 的写入经由其内部锁，锁序固定
    /// （_gate → store 锁），无死锁风险。
    /// </summary>
    public class ScenarioEngine
    {
        private readonly DataStore _store;
        private readonly System.Threading.Timer _timer;
        private readonly object _gate = new();

        // 剧本状态
        private double _waveSeconds;
        private bool _wave;
        private bool _surge;
        private bool _lastSurge;

        // 泵联动状态（下标 = 泵编号-1）
        private readonly bool[] _pumpOn = new bool[3];
        private readonly double[] _pumpProgress = new double[3];
        private readonly int[] _pumpDelayTicks = new int[3];

        // 关联标签（启动时从标签表解析，缺失则对应行为静默跳过）
        private readonly TagAddress? _water;
        private readonly TagAddress? _actual;
        private readonly TagAddress? _pressure;
        private readonly TagAddress?[][] _pumpTags;   // [泵][F,P,S]

        private const double RatedFrequency = 50.0;   // Hz
        private const double RatedPower = 75.0;       // KW
        private const double RatedSpeed = 1450.0;     // r/m
        private const int StartDelayTicks = 6;        // 启动延迟 3s（6 × 500ms）
        private const int RampTicks = 6;              // 缓升/缓降时长 3s

        public ScenarioEngine(DataStore store)
        {
            _store = store;
            _water = store.FindTag("CurWaterPos");
            _actual = store.FindTag("ActualWaters");
            _pressure = store.FindTag("MainOutPressure");
            _pumpTags = new TagAddress?[3][];
            for (int i = 0; i < 3; i++)
            {
                _pumpTags[i] = new TagAddress?[3];
                _pumpTags[i][0] = store.FindTag($"Pump{i + 1}_F");
                _pumpTags[i][1] = store.FindTag($"Pump{i + 1}_P");
                _pumpTags[i][2] = store.FindTag($"Pump{i + 1}_S");
            }
            _timer = new System.Threading.Timer(_ => Tick(), null, 500, 500);
        }

        /// <summary>切换剧本：steady（稳态）/ wave（水位波动）</summary>
        public void SetScenario(string scenario)
        {
            lock (_gate)
            {
                bool wave = scenario == "wave";
                if (wave == _wave) return;
                _wave = wave;
                if (wave)
                {
                    _waveSeconds = 0;
                }
                else
                {
                    // 退出波动剧本时把水位恢复到稳态基准
                    if (_water != null) _store.WriteEngineering(_water, 2.5);
                }
            }
        }

        /// <summary>压力浪涌开关</summary>
        public void SetSurge(bool on)
        {
            lock (_gate) _surge = on;
        }

        /// <summary>FC05 写 PumpN_Run 线圈时由协议层回调，驱动泵联动</summary>
        public void NotifyCoilWrite(int address, bool on)
        {
            var tag = _store.FindCoilOwner(address);
            if (tag == null || !tag.TagName.StartsWith("Pump") || !tag.TagName.EndsWith("_Run"))
                return;
            if (!int.TryParse(tag.TagName.Substring(4, 1), out int pumpNo) || pumpNo is < 1 or > 3)
                return;
            SetPumpTarget(pumpNo, on);
        }

        /// <summary>设定某台泵的目标运行状态（触发缓升/缓降）</summary>
        public void SetPumpTarget(int pumpNo, bool on)
        {
            lock (_gate)
            {
                int i = pumpNo - 1;
                if (_pumpOn[i] == on) return;
                _pumpOn[i] = on;
                // 启动：延迟 3s 再缓升；停止：立即缓降
                _pumpDelayTicks[i] = on ? StartDelayTicks : 0;
            }
        }

        /// <summary>500ms 周期 tick：推进所有剧本</summary>
        private void Tick()
        {
            try
            {
                lock (_gate)
                {
                    // S2 波动：水位正弦 + 实际用水量缓增
                    if (_wave)
                    {
                        _waveSeconds += 0.5;
                        if (_water != null)
                            // 振幅 1.2（非 1.5）：谷值 1.3m 高于低限 1.0m，避免浮点擦边误报
                            _store.WriteEngineering(_water, 2.5 + 1.2 * Math.Sin(2 * Math.PI * _waveSeconds / 120.0));
                        if (_actual != null)
                            _store.WriteEngineering(_actual, _store.ReadEngineering(_actual) + 0.01);
                    }

                    // S5 浪涌：压力阶跃/恢复（只在状态切换或浪涌期间写，避免覆盖手动 set）
                    if (_surge)
                    {
                        if (_pressure != null) _store.WriteEngineering(_pressure, 12.0);
                    }
                    else if (_lastSurge)
                    {
                        if (_pressure != null) _store.WriteEngineering(_pressure, 0.35);
                    }
                    _lastSurge = _surge;

                    // S3 泵联动：延迟 → 缓升/缓降（只在进度变化的那一拍写，避免覆盖手动 set）
                    for (int i = 0; i < 3; i++)
                    {
                        if (_pumpOn[i])
                        {
                            if (_pumpDelayTicks[i] > 0)
                            {
                                _pumpDelayTicks[i]--;
                                continue;
                            }
                            _pumpProgress[i] = Math.Min(1.0, _pumpProgress[i] + 1.0 / RampTicks);
                        }
                        else
                        {
                            _pumpProgress[i] = Math.Max(0.0, _pumpProgress[i] - 1.0 / RampTicks);
                        }

                        if (_pumpProgress[i] > 0)
                        {
                            double p = _pumpProgress[i];
                            if (_pumpTags[i][0] != null) _store.WriteEngineering(_pumpTags[i][0]!, RatedFrequency * p);
                            if (_pumpTags[i][1] != null) _store.WriteEngineering(_pumpTags[i][1]!, RatedPower * p);
                            if (_pumpTags[i][2] != null) _store.WriteEngineering(_pumpTags[i][2]!, RatedSpeed * p);
                        }
                    }
                }
            }
            catch
            {
                // 演示工具：剧本 tick 异常不拖垮服务
            }
        }
    }
}
