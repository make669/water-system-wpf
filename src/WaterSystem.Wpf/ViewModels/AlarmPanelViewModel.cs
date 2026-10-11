using System.Collections.ObjectModel;
using System.Windows.Input;
using System.ComponentModel;
using Intelligent_water_storage_system.Services;
using WaterSystem.Wpf.Infrastructure;

namespace WaterSystem.Wpf.ViewModels
{
    /// <summary>报警行（四态 ToneKey：Alarm=报警中 / Ack=报警中·已确认 / Recover=已恢复·未确认 / Off=已关闭）。</summary>
    public sealed class AlarmRowVm
    {
        public long Id { get; init; }
        public string Message { get; init; } = "";
        public string TriggeredText { get; init; } = "";
        public string DurationText { get; init; } = "";
        public string StateText { get; init; } = "";
        public string ToneKey { get; init; } = "Off";
        public bool NeedsAck { get; init; }
    }

    /// <summary>
    /// 实时报警面板（修 P4/P12）：数据源=AlarmCenter.OpenSnapshot/OnAlarmChanged/UnacknowledgedCount
    /// （AlarmCenter.cs:107-116 设计意图"标题栏徽标依据"的首次消费）；确认走 Acknowledge（落库）。
    /// </summary>
    public sealed class AlarmPanelViewModel : INotifyPropertyChanged
    {
        private readonly AlarmCenter _center;
        private readonly Func<string> _user;
        private AlarmRowVm? _selected;

        public ObservableCollection<AlarmRowVm> Rows { get; } = new();

        private int _openCount;
        public int OpenCount { get => _openCount; private set { _openCount = value; PropertyChanged?.Invoke(this, new(nameof(OpenCount))); } }

        private int _unackCount;
        public int UnackCount { get => _unackCount; private set { _unackCount = value; PropertyChanged?.Invoke(this, new(nameof(UnackCount))); } }

        public AlarmRowVm? Selected { get => _selected; set { _selected = value; PropertyChanged?.Invoke(this, new(nameof(Selected))); } }

        public ICommand AckSelectedCommand { get; }
        public ICommand AckAllCommand { get; }

        public event PropertyChangedEventHandler? PropertyChanged;

        public AlarmPanelViewModel(AlarmCenter center, Func<string>? user = null)
        {
            _center = center;
            _user = user ?? DefaultUser;
            AckSelectedCommand = new RelayCommand(AckSelected, () => Selected is { NeedsAck: true });
            AckAllCommand = new RelayCommand(() => _center.AcknowledgeAll(_user()));
            center.OnAlarmChanged += _ => UiDispatcher.Post(Refresh);
            Refresh();
        }

        private static string DefaultUser()
            => string.IsNullOrWhiteSpace(Environment.UserName) ? "操作员" : Environment.UserName;

        public void Refresh()
        {
            var snapshot = _center.OpenSnapshot();
            Rows.Clear();
            foreach (var r in snapshot)
            {
                Rows.Add(new AlarmRowVm
                {
                    Id = r.Id,
                    Message = r.Message,
                    TriggeredText = r.TriggeredAt.ToString("HH:mm"),
                    DurationText = $"{(int)r.Duration.TotalMinutes:00}m",
                    StateText = r.StateText,
                    ToneKey = r.StateText switch
                    {
                        "报警中" => "Alarm",
                        "报警中·已确认" => "Ack",
                        "已恢复·未确认" => "Recover",
                        _ => "Off",
                    },
                    NeedsAck = r.NeedsAck,
                });
            }
            OpenCount = snapshot.Count;
            UnackCount = _center.UnacknowledgedCount;
        }

        private void AckSelected()
        {
            if (Selected is { NeedsAck: true })
                _center.Acknowledge(Selected.Id, _user());
        }
    }
}
