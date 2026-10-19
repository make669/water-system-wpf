using System.Windows;
using System.Windows.Controls;

namespace WaterSystem.Wpf.Controls
{
    /// <summary>卡片容器（附录 B CardChrome）：内容区 + 卡底标签条（BgCardLabel 26px）。</summary>
    public class CardView : ContentControl
    {
        public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
            nameof(Title), typeof(string), typeof(CardView), new PropertyMetadata(""));

        public static readonly DependencyProperty BadgeProperty = DependencyProperty.Register(
            nameof(Badge), typeof(object), typeof(CardView), new PropertyMetadata(null));

        public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
        public object? Badge { get => GetValue(BadgeProperty); set => SetValue(BadgeProperty, value); }
    }
}
