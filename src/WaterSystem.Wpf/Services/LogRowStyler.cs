using System.Windows.Media;
using Intelligent_water_storage_system.Services;

namespace WaterSystem.Wpf.Services
{
    /// <summary>
    /// 日志行着色唯一出处（设计书 §3/源 FrmMain.cs:268-294 平移）：主日志卡与 F12 窗口共用，
    /// 禁止第二处实现（界面定稿"唯一出处"原则）。
    /// 规则：ERROR 深红/淡红；WARN 橙（发生）；WARN 且消息以"恢复正常"/"已恢复"结尾 → 绿（恢复）；
    /// INFO/DEBUG 默认（返回 null）。恢复行识别依赖 AlarmService 恢复文案（既有测试约束，文案改动须同步）。
    /// </summary>
    public static class LogRowStyler
    {
        public sealed record RowStyle(Color Fore, Color Back);

        public static RowStyle? Resolve(string level, string message)
        {
            if (level == "ERROR")
                return new(From("#C00000"), From("#FDECEC"));
            if (level == "WARN")
            {
                if (message.EndsWith("恢复正常", StringComparison.Ordinal)
                    || message.EndsWith("已恢复", StringComparison.Ordinal))
                    return new(From("#1B7A34"), From("#EEF9F0"));
                return new(From("#B85C00"), From("#FFF7EA"));
            }
            return null;
        }

        /// <summary>DataGrid 行着色入口（主卡与 F12 的 LoadingRow 共用）。</summary>
        public static void Apply(System.Windows.Controls.DataGridRow row, LogEntry entry)
        {
            var style = Resolve(entry.Level, entry.Message);
            if (style == null) return;
            row.Background = new SolidColorBrush(style.Back);
            row.Foreground = new SolidColorBrush(style.Fore);
        }

        private static Color From(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    }
}
