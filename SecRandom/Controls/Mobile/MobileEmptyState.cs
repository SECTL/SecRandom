using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using AvaloniaButton = Avalonia.Controls.Button;

namespace SecRandom.Controls.Mobile;

/// <summary>
/// 空态占位：图标 + 标题 + 可选描述 + 可选引导按钮。引导按钮应跳转到对应管理界面，
/// 而不是解释平台内部细节。
/// </summary>
public sealed partial class MobileEmptyState : UserControl
{
    public MobileEmptyState(string glyph, string title, string? description = null, string? actionText = null, Action? action = null)
    {
        InitializeComponent();
        this.FindControl<TextBlock>("Icon")!.Text = glyph;
        this.FindControl<TextBlock>("TitleText")!.Text = title;
        var descriptionText = this.FindControl<TextBlock>("DescriptionText")!;
        descriptionText.Text = description;
        descriptionText.IsVisible = !string.IsNullOrEmpty(description);
        var actionButton = this.FindControl<AvaloniaButton>("ActionButton")!;
        actionButton.Content = actionText;
        actionButton.IsVisible = !string.IsNullOrEmpty(actionText) && action is not null;
        if (action is not null)
            actionButton.Click += (_, _) => action();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
