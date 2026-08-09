using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SecRandom.Core.Icons;
using SecRandom.Views.Mobile;

namespace SecRandom.Controls.Mobile;

/// <summary>
/// 段落页眉：主色小图标 + 加粗标题。
/// </summary>
public sealed partial class MobileSectionHeader : UserControl
{
    public MobileSectionHeader(string text, string? glyph = null)
    {
        InitializeComponent();
        var icon = this.FindControl<TextBlock>("Icon")!;
        icon.Text = glyph ?? FluentIcons.AppsListFilled;
        var title = this.FindControl<TextBlock>("TitleText")!;
        title.Text = text;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
