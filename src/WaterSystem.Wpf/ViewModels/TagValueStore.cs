using System.ComponentModel;

namespace WaterSystem.Wpf.ViewModels
{
    /// <summary>
    /// Tag 值仓库（回写防护层 1 的数据面）：VM 持有的状态唯一属主。
    /// XAML 经 {ui:TagBinding XXX} 绑定到 TagValues[XXX] 索引器；
    /// 更新按点发送 PropertyChanged("TagValues[Tag]")，只刷新对应绑定。
    /// PLC 数据同步只改这里（抑制标志在 MainViewModel 层），控件视觉一律 OneWay——
    /// 对应 WinForms"编程赋值不触发事件"的 E1a 语义（迁移方案 §2.4）。
    /// </summary>
    public sealed class TagValueStore : INotifyPropertyChanged
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, double> _values = new(StringComparer.OrdinalIgnoreCase);

        public event PropertyChangedEventHandler? PropertyChanged;

        public double this[string tag]
        {
            get { lock (_gate) return _values.TryGetValue(tag, out var v) ? v : 0; }
        }

        /// <summary>整批更新（来自轮询数据字典），只对发生变化的点发通知。</summary>
        public void Update(IEnumerable<KeyValuePair<string, double>> data)
        {
            List<string>? changed = null;
            lock (_gate)
            {
                foreach (var kv in data)
                {
                    _values.TryGetValue(kv.Key, out var old);
                    if (Math.Abs(old - kv.Value) > 1e-9 || (_values.ContainsKey(kv.Key) == false))
                    {
                        _values[kv.Key] = kv.Value;
                        (changed ??= new List<string>()).Add(kv.Key);
                    }
                }
            }
            if (changed != null)
            {
                foreach (var name in changed)
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs($"TagValues[{name}]"));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            }
        }

        /// <summary>空态归零（ResetDisplayValues 语义：断开/启动时清零，不显示过期值）。</summary>
        public void ResetToZero()
        {
            List<string> names;
            lock (_gate) names = _values.Keys.ToList();
            Update(names.Select(n => new KeyValuePair<string, double>(n, 0)));
        }
    }
}
