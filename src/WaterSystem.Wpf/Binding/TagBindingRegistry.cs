using System.Windows.Data;
using System.Windows.Markup;
using Intelligent_water_storage_system.Model;
using Intelligent_water_storage_system.Utils;

namespace WaterSystem.Wpf.Binding
{
    /// <summary>绑定类别：Display=显示类（ParaBox/仪表/液位…），Control=控制类（旋钮/开关）。</summary>
    public enum TagBindingKind
    {
        Display,
        Control,
    }

    /// <summary>
    /// Tag 绑定注册表（迁移方案 §2.5）：所有与 Tag 挂钩的 XAML 绑定经 <see cref="TagBindingExtension"/>
    /// 在解析时登记到这里；启动流程在主窗 InitializeComponent 后调用 <see cref="Check"/>，
    /// 把"界面实际用了哪些 Tag"喂给 TagTableLoader.CheckUiBindings（其签名本就收字符串集合，
    /// TagTableLoader.cs:254-260）——E08（RW 无控制入口）/E09（绑定不存在的标签）继续 fail-fast。
    /// 防假绿纪律（风险 R3）：注册表漏注册 = 校验静默通过，单测须逐码断言（T2.5）。
    /// </summary>
    public static class TagBindingRegistry
    {
        private static readonly object Gate = new();
        private static readonly HashSet<string> Control = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> Display = new(StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyCollection<string> ControlNames { get { lock (Gate) return Control.ToArray(); } }
        public static IReadOnlyCollection<string> DisplayNames { get { lock (Gate) return Display.ToArray(); } }

        public static void Register(string tag, TagBindingKind kind)
        {
            lock (Gate)
            {
                if (kind == TagBindingKind.Control) Control.Add(tag);
                else Display.Add(tag);
            }
        }

        /// <summary>仅供单元测试复位（线程安全）。</summary>
        public static void Reset()
        {
            lock (Gate)
            {
                Control.Clear();
                Display.Clear();
            }
        }

        /// <summary>用登记结果做双向核对，返回 E08/E09 错误与 W01 警告（不抛异常，调用方决定 fail-fast）。</summary>
        public static (List<string> Errors, List<string> Warnings) Check(IEnumerable<TagAddress> tags)
        {
            var tagList = tags.ToList();
            List<string> control, display;
            lock (Gate)
            {
                control = Control.ToList();
                display = Display.ToList();
            }
            var errors = new List<string>();
            var warnings = new List<string>();
            TagTableLoader.CheckUiBindings(tagList, control, display, errors, warnings);
            return (errors, warnings);
        }
    }

    /// <summary>
    /// XAML 用法（M3/M4 接线）：
    ///   显示类：Text="{ui:TagBinding Pump1_F}"
    ///   控制类：&lt;controls:USwitchW Value="{ui:TagBinding Valve1, Kind=Control}" /&gt;
    /// 继承 Binding（ProvideValue 交给基类——从普通 MarkupExtension 返回裸 Binding 对象不会自动
    /// 应用到目标属性，首次接线时踩过）：构造即登记 Tag 名，并对 VM TagValues 索引器生成
    /// OneWay 绑定（控制开关的视觉一律 OneWay 绑定 VM——回写防护层 1，迁移方案 §2.4；
    /// 用户手势走命令，不走绑定）。
    /// </summary>
    public class TagBindingExtension : System.Windows.Data.Binding
    {
        public TagBindingKind Kind { get; set; } = TagBindingKind.Display;

        public TagBindingExtension(string tagName) : base($"TagValues[{tagName}]")
        {
            Mode = BindingMode.OneWay;
            TagBindingRegistry.Register(tagName, Kind);
        }
    }
}
