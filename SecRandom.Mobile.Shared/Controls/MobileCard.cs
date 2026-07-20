using Avalonia.Controls;
using SecRandom.Mobile.Views;

namespace SecRandom.Mobile.Controls;

/// <summary>
/// 圆角卡片容器。默认外观（Surface 底色、边框、圆角、内边距）由
/// Styles/MobileStyles.axaml 中以类型为键的 ControlTheme 提供；
/// 传入背景资源键可换成 PrimaryWash/WarmWash 等主题感知的 wash 底色。
/// </summary>
public class MobileCard : ContentControl
{
    public MobileCard()
    {
    }

    public MobileCard(string? backgroundResourceKey, Control? content = null)
    {
        if (backgroundResourceKey is not null)
            MobileResources.BindBrush(this, BackgroundProperty, backgroundResourceKey);
        if (content is not null)
            Content = content;
    }
}
