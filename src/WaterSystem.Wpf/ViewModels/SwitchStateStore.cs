using System.ComponentModel;

namespace WaterSystem.Wpf.ViewModels
{
    /// <summary>控制开关视觉状态仓库（bool 索引器，按点 INPC）。
    /// 控制开关视觉一律 OneWay 绑定这里（回写防护层 1）；乐观翻转/回滚由 ControlViewModel 写入。</summary>
    public sealed class SwitchStateStore : INotifyPropertyChanged
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, bool> _states = new(StringComparer.OrdinalIgnoreCase);

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool this[string tag] { get { lock (_gate) return _states.TryGetValue(tag, out var v) && v; } }

        public void Set(string tag, bool value)
        {
            lock (_gate)
            {
                if (_states.TryGetValue(tag, out var old) && old == value) return;
                _states[tag] = value;
            }
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs($"SwitchStates[{tag}]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        }
    }
}
