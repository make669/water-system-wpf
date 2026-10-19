using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WaterSystem.Wpf.ViewModels;

namespace WaterSystem.Wpf.Views
{
    /// <summary>
    /// 系统说明窗（顶栏 ⓘ 入口，设计书 §1.0 & §2.1，T4.10 验收）：
    /// 1. 系统名称与版本
    /// 2. 监控对象（36 点标签表摘要）
    /// 3. 通信方式（Modbus TCP）与当前连接状态
    /// 4. 日志目录与数据库路径（支持一键打开）
    /// 5. 陌生人自解释指南（设计书 §1.0 四问逐条回答，10 秒内理解系统）
    /// </summary>
    public class SystemInfoWindow : Window
    {
        public SystemInfoWindow(MainViewModel? vm = null)
        {
            Title = "系统说明与运行信息";
            Width = 760;
            Height = 640;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = ResourceBrush("BgWindow", "#0B1220");

            var mainGrid = new Grid { Margin = new Thickness(20) };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Buttons

            // 1. Header
            var headerPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
            var titleBlock = new TextBlock
            {
                Text = "智能水箱上位机监控系统",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = ResourceBrush("TextPrimary", "#E5EDF5"),
            };
            var subtitleBlock = new TextBlock
            {
                Text = "Intelligent Water Storage & Multi-Pump Supply SCADA System · v0.4-graphics (M4 定稿)",
                FontSize = 12,
                Foreground = ResourceBrush("Accent", "#3B82F6"),
                Margin = new Thickness(0, 4, 0, 0),
            };
            headerPanel.Children.Add(titleBlock);
            headerPanel.Children.Add(subtitleBlock);
            Grid.SetRow(headerPanel, 0);
            mainGrid.Children.Add(headerPanel);

            // 2. Scrollable Body
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Margin = new Thickness(0, 0, 0, 16),
            };
            var contentStack = new StackPanel();

            // ── Section A: 陌生人自解释原则指南（设计书 §1.0 四问） ──
            contentStack.Children.Add(CreateSectionCard("一、陌生人指南（自解释原则 · 10 秒识别系统）", new UIElement[]
            {
                CreateQAPair("Q1: 这是什么系统？",
                    "本系统为智能储水与多泵供水上位机控制监控系统（HMI/SCADA），负责对蓄水池水源、主管路、3 台离心水泵和 4 组阀门进行实时监视、调度与安全控制。"),
                CreateQAPair("Q2: 监控与操作哪些对象？",
                    "• 蓄水池：0~6.0m 水位实时监视、动态波浪、超高/低限声光报警；\n" +
                    "• 水泵机组（1#、2#、3#）：频率 (Hz)、功率 (kW)、转速 (r/min) 监视、启停控制（旋钮交互）及运行/故障双状态指示；\n" +
                    "• 阀门组：主进水阀门（MainValve）、1#~3# 分管支路阀门（Valve1~3）开闭控制与状态呈现；\n" +
                    "• 管网水路：主进水管、供水短柱、水泵吸入段、出口短柱、下汇管、主管输出端（11 段流向动画与水路因果联动）；\n" +
                    "• 环境量：实时温度 (-10~50 ℃)、土壤湿度 (0~100 %)、降水量 (0~100 mm)、实际用水量 (0~500 m³)。"),
                CreateQAPair("Q3: 现在系统状态正常吗？",
                    "• 顶栏通信 Chip 实时显示链路健康度（已连接 / 连接中 / 通信中断·第 N 次重连）；\n" +
                    "• 顶栏报警徽标实时汇聚未确认报警总数（报警中红显、正常灰显，点击直达报警卡）；\n" +
                    "• 状态徽章带 7 组设备状态圆点灯与动态文字永可见；\n" +
                    "• 异常时过程图描红、实时报警卡与历史日志三处同步可见。"),
                CreateQAPair("Q4: 水往哪流？颜色分别代表什么意思？",
                    "• 水流方向：蓄水池 → 主进水管 → 3 组分水短柱 → 1#/2#/3# 水泵 → 汇水总管 → 主管出口；\n" +
                    "• 灰蓝/深灰 (#3D4E6B)：管路静止/无流动；\n" +
                    "• 翠绿虚线流动 (#3FB950)：正常水流沿流向动画循环；\n" +
                    "• 鲜红 (#F87171)：压力过高/水泵故障/越限告警描红。"),
            }));

            // ── Section B: 监控对象（36 点标签表摘要） ──
            contentStack.Children.Add(CreateSectionCard("二、监控对象摘要（36 点数据契约）", new UIElement[]
            {
                CreateKeyValueRow("数据点总数", "36 个点位（严格对应 configFiles/TagTable.csv，启动前 fail-fast E01~E09 零缺陷校验）"),
                CreateKeyValueRow("点位类型分布", "线圈 (Coil) 13 点（控制开关与设备状态） · 浮点数 (Float) 12 点（模拟量测量） · 寄存器 (UShort) 11 点（转速/频率/功率）"),
                CreateKeyValueRow("安全控制契约", "控制类点位具备单一属主、防重入、手势自翻转等价与 3 层回写防护（轮询 100 轮零回写）"),
            }));

            // ── Section C: 通信机制与当前状态 ──
            string connState = vm?.ConnStateText ?? "未知";
            string currentIp = vm?.IpText ?? "127.0.0.1";
            string currentPort = vm?.PortText ?? "502";
            contentStack.Children.Add(CreateSectionCard("三、通信协议与连接信息", new UIElement[]
            {
                CreateKeyValueRow("通信协议", "Modbus TCP（工业标准协议，功能码 FC01/FC02/FC03/FC04/FC05/FC06）"),
                CreateKeyValueRow("目标地址与端口", $"{currentIp} : {currentPort}"),
                CreateKeyValueRow("当前连接状态", connState),
                CreateKeyValueRow("轮询周期与看门狗", "1000ms 周期轮询 · 200ms UI 数据看门狗 · 1s~30s 指数退避自动重连"),
            }));

            // ── Section D: 系统环境与日志存储 ──
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string logsDir = Path.Combine(baseDir, "logs");
            string dbPath = Path.Combine(baseDir, "data", "history.db");
            contentStack.Children.Add(CreateSectionCard("四、系统环境与数据持久化", new UIElement[]
            {
                CreateKeyValueRow("运行环境", ".NET 8.0 (net8.0-windows) / WPF / ISA-101 深色工业风格"),
                CreateKeyValueRow("日志目录", logsDir),
                CreateKeyValueRow("历史数据库", dbPath + " (SQLite 闭环存储与查询)"),
            }));

            scroll.Content = contentStack;
            Grid.SetRow(scroll, 1);
            mainGrid.Children.Add(scroll);

            // 3. Bottom Button Panel
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
            };

            var openLogsBtn = new Button
            {
                Content = "打开日志目录",
                Width = 110,
                Height = 32,
                Margin = new Thickness(0, 0, 12, 0),
                Background = ResourceBrush("BgHover", "#1A2740"),
                Foreground = ResourceBrush("TextPrimary", "#E5EDF5"),
                BorderBrush = ResourceBrush("Stroke", "#223047"),
            };
            openLogsBtn.Click += (_, _) =>
            {
                try
                {
                    if (!Directory.Exists(logsDir)) Directory.CreateDirectory(logsDir);
                    Process.Start(new ProcessStartInfo { FileName = logsDir, UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"打开目录失败: {ex.Message}", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };

            var closeBtn = new Button
            {
                Content = "关闭",
                Width = 88,
                Height = 32,
                IsDefault = true,
                IsCancel = true,
                Background = ResourceBrush("Accent", "#3B82F6"),
                Foreground = ResourceBrush("TextPrimary", "#E5EDF5"),
                BorderThickness = new Thickness(0),
            };
            closeBtn.Click += (_, _) => Close();

            buttonPanel.Children.Add(openLogsBtn);
            buttonPanel.Children.Add(closeBtn);
            Grid.SetRow(buttonPanel, 2);
            mainGrid.Children.Add(buttonPanel);

            Content = mainGrid;
        }

        private static Border CreateSectionCard(string title, UIElement[] children)
        {
            var card = new Border
            {
                Background = ResourceBrush("BgCard", "#131C2E"),
                BorderBrush = ResourceBrush("Stroke", "#223047"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(14, 10, 14, 12),
                Margin = new Thickness(0, 0, 0, 12),
            };

            var stack = new StackPanel();
            var titleTb = new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = ResourceBrush("Accent", "#3B82F6"),
                Margin = new Thickness(0, 0, 0, 8),
            };
            stack.Children.Add(titleTb);

            foreach (var child in children)
                stack.Children.Add(child);

            card.Child = stack;
            return card;
        }

        private static UIElement CreateQAPair(string question, string answer)
        {
            var p = new StackPanel { Margin = new Thickness(0, 2, 0, 6) };
            var q = new TextBlock
            {
                Text = question,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = ResourceBrush("TextPrimary", "#E5EDF5"),
            };
            var a = new TextBlock
            {
                Text = answer,
                FontSize = 11,
                Foreground = ResourceBrush("TextSecondary", "#8FA3BF"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(8, 2, 0, 0),
                LineHeight = 16,
            };
            p.Children.Add(q);
            p.Children.Add(a);
            return p;
        }

        private static UIElement CreateKeyValueRow(string key, string value)
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var k = new TextBlock
            {
                Text = key,
                FontSize = 11,
                Foreground = ResourceBrush("TextSecondary", "#8FA3BF"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var v = new TextBlock
            {
                Text = value,
                FontSize = 11,
                Foreground = ResourceBrush("TextPrimary", "#E5EDF5"),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(k, 0);
            Grid.SetColumn(v, 1);
            g.Children.Add(k);
            g.Children.Add(v);
            return g;
        }

        private static Brush ResourceBrush(string key, string fallbackHex)
        {
            return Application.Current?.TryFindResource(key) as Brush
                ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallbackHex));
        }
    }
}
