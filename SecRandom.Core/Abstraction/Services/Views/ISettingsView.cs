namespace SecRandom.Core.Abstraction.Services.Views;

/// <summary>
///     Host settings-view capability exposed to plugins: drawer access plus settings-page navigation by
///     <c>settings.xxx</c> page id. <see cref="NavigateToPreviewPage"/> enters the read-only settings
///     preview for a page; preview remains a security-prompt outcome and must not mutate configuration.
/// </summary>
public interface ISettingsView : IDrawerView
{
    void NavigateToPage(string id);

    void NavigateToPreviewPage(string id);
}
