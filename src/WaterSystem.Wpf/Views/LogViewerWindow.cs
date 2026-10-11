using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Intelligent_water_storage_system.Services;

namespace WaterSystem.Wpf.Views
{
    /// <summary>
    /// F12 全量日志窗口（对应源 LogViewerForm 嵌套类，FrmMain.cs:960-1229 平移）：
    /// 级别筛选/关键字/自动滚动/暂停/导出 TXT/清屏/双击详情（含确认报警）/打开日志目录；缓冲 5000 含 DEBUG。
    /// 着色 = LogRowStyler（唯一出处）。Esc 关闭。
    /// </summary>
    public sealed class LogViewerWindow : Window
    {
        private const int MaxBuffer = 5000;

        private readonly LogService _log;
        private readonly AlarmCenter? _center;
        private readonly List<LogEntry> _buffer = new();
        private readonly List<LogEntry> _view = new();
        private readonly object _gate = new();
        private bool _paused;

        private readonly CheckBox _chkInfo = MakeCheck("INFO", true);
        private readonly CheckBox _chkWarn = MakeCheck("WARN", true);
        private readonly CheckBox _chkError = MakeCheck("ERROR", true);
        private readonly CheckBox _chkDebug = MakeCheck("DEBUG", false);
        private readonly TextBox _keyword = new() { Width = 150, Height = 24 };
        private readonly CheckBox _autoScroll = MakeCheck("自动滚动", true);
        private readonly Button _pauseButton = MakeButton("暂停", 72);
        private readonly DataGrid _grid = new() { IsReadOnly = true, AutoGenerateColumns = false, HeadersVisibility = DataGridHeadersVisibility.Column };

        private readonly DataGridTextColumn _colTime = new() { Header = "时间", Binding = new System.Windows.Data.Binding("Time") { StringFormat = "HH:mm:ss" }, Width = new DataGridLength(90) };
        private readonly DataGridTextColumn _colLevel = new() { Header = "级别", Binding = new System.Windows.Data.Binding("Level"), Width = new DataGridLength(60) };
        private readonly DataGridTextColumn _colMessage = new() { Header = "消息", Binding = new System.Windows.Data.Binding("Message"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) };

        public LogViewerWindow(LogService log, AlarmCenter? center)
        {
            _log = log;
            _center = center;
            Title = "运行日志（全量，F12）";
            Width = 960;
            Height = 640;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            ShowInTaskbar = false;
            FontFamily = new FontFamily("微软雅黑");
            Background = (Brush)FindResource("BgWindow");
            PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };

            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 6, 8, 6) };
            foreach (var c in new[] { (UIElement)_chkInfo, _chkWarn, _chkError, _chkDebug })
            {
                ((CheckBox)c).Checked += (_, _) => RebuildView();
                ((CheckBox)c).Unchecked += (_, _) => RebuildView();
                toolbar.Children.Add(c);
            }
            var kwLabel = new TextBlock { Text = "关键字", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 6, 0), Foreground = (Brush)FindResource("TextSecondary") };
            toolbar.Children.Add(kwLabel);
            _keyword.TextChanged += (_, _) => RebuildView();
            toolbar.Children.Add(_keyword);
            _autoScroll.Checked += (_, _) => RebuildView();
            _autoScroll.Unchecked += (_, _) => RebuildView();
            toolbar.Children.Add(_autoScroll);
            _pauseButton.Click += (_, _) => TogglePause();
            toolbar.Children.Add(_pauseButton);
            toolbar.Children.Add(MakeActionButton("导出 TXT", ExportView));
            toolbar.Children.Add(MakeActionButton("清屏", ClearView));
            toolbar.Children.Add(MakeActionButton("打开日志目录", OpenLogDir));

            _grid.Columns.Add(_colTime);
            _grid.Columns.Add(_colLevel);
            _grid.Columns.Add(_colMessage);
            _grid.LoadingRow += (_, e) => Services.LogRowStyler.Apply(e.Row, (LogEntry)e.Row.Item);
            _grid.MouseDoubleClick += (_, _) =>
            {
                if (_grid.SelectedItem is LogEntry entry)
                    new LogDetailDialog(entry, _center) { Owner = this }.ShowDialog();
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(toolbar, 0);
            Grid.SetRow(_grid, 1);
            grid.Children.Add(toolbar);
            grid.Children.Add(_grid);
            Content = grid;

            foreach (var entry in log.Entries) Append(entry);   // 回放主界面现有条目
            log.OnEntry += Append;
            Closed += (_, _) => log.OnEntry -= Append;
            RebuildView();
        }

        private static CheckBox MakeCheck(string text, bool @checked) =>
            new() { Content = text, IsChecked = @checked, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), Foreground = (Brush)Application.Current!.TryFindResource("TextPrimary")! };

        private static Button MakeButton(string content, double width) =>
            new() { Content = content, Width = width, Height = 26, Margin = new Thickness(0, 0, 8, 0) };

        private Button MakeActionButton(string content, Action click)
        {
            var b = MakeButton(content, content.Length > 4 ? 96 : 72);
            b.Click += (_, _) => click();
            return b;
        }

        private void Append(LogEntry entry)
        {
            lock (_gate)
            {
                _buffer.Add(entry);
                if (_buffer.Count > MaxBuffer) _buffer.RemoveAt(0);
            }
            if (_paused) return;
            if (PassesFilter(entry))
            {
                _view.Add(entry);
                if (_view.Count > MaxBuffer) _view.RemoveAt(0);
                if (_grid.ItemsSource == null) _grid.ItemsSource = _view;
                if (_autoScroll.IsChecked == true && _view.Count > 0)
                    _grid.ScrollIntoView(_view[^1]);
            }
        }

        private void RebuildView()
        {
            if (_paused) return;
            lock (_gate) _view.AddRange(_buffer.Where(PassesFilter));
            while (_view.Count > MaxBuffer) _view.RemoveAt(0);
            _grid.ItemsSource = _view;
            if (_autoScroll.IsChecked == true && _view.Count > 0) _grid.ScrollIntoView(_view[^1]);
        }

        private bool PassesFilter(LogEntry e) => Passes(
            e.Level, e.Message,
            _chkInfo.IsChecked == true, _chkWarn.IsChecked == true, _chkError.IsChecked == true,
            _chkDebug.IsChecked == true, _keyword.Text);

        /// <summary>过滤纯函数（可测）。</summary>
        public static bool Passes(string level, string message, bool info, bool warn, bool error, bool debug, string keyword)
        {
            bool levelOk = level switch
            {
                "INFO" => info,
                "WARN" => warn,
                "ERROR" => error,
                "DEBUG" => debug,
                _ => true,
            };
            if (!levelOk) return false;
            return string.IsNullOrWhiteSpace(keyword) || message.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        }

        private void TogglePause()
        {
            _paused = !_paused;
            _pauseButton.Content = _paused ? "继续" : "暂停";
            if (!_paused) RebuildView();
        }

        private void ExportView()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "文本文件|*.txt",
                FileName = $"日志导出_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
            };
            if (dlg.ShowDialog(this) != true) return;
            File.WriteAllLines(dlg.FileName, _view.Select(e => $"[{e.Time:HH:mm:ss}] [{e.Level}] {e.Message}"));
            MessageBox.Show(this, $"已导出 {_view.Count} 条到：\n{dlg.FileName}", "导出完成",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ClearView()
        {
            if (MessageBox.Show(this, "仅清空日志窗口显示缓冲（日志文件不受影响），确定吗？",
                    "清屏确认", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            lock (_gate) _buffer.Clear();
            _view.Clear();
            _grid.ItemsSource = null;
            _grid.ItemsSource = _view;
        }

        private void OpenLogDir()
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start("explorer.exe", dir);
        }
    }
}
