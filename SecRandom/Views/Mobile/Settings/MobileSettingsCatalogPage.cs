using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;
using SecRandom.Core.Services;
using SecRandom.Services.Mobile;

namespace SecRandom.Views.Mobile.Settings;

/// <summary>
/// SettingsView 的隐藏目录页。SettingsView 依据注册元数据生成分组和入口。
/// </summary>
[PageInfo(MobilePageIds.Settings, FluentIcons.SettingsFilled, isHide: true)]
public sealed partial class MobileSettingsCatalogPage : MobileSettingsPageBase
{
    private readonly IMobileSettingsNavigator _settingsNavigator;

    public MobileSettingsCatalogPage(
        IMobileCapabilities capabilities,
        IMobileSettingsNavigator settingsNavigator)
        : base(capabilities)
    {
        _settingsNavigator = settingsNavigator;
        InitializeComponent();
        Groups = PagesRegistryService.GroupItems
            .Select(group => new MobileSettingsCatalogGroup(
                group.Name,
                group.IconGlyph,
                PagesRegistryService.SettingsItems
                    .Where(page => !page.IsSeparator && !page.IsHide && page.GroupId == group.Id)
                    .Select(page => new MobileSettingsCatalogEntry(page.Id, page.Name, page.IconGlyph))
                    .ToArray()))
            .Where(group => group.Pages.Count != 0)
            .ToArray();
        DataContext = this;
    }

    public IReadOnlyList<MobileSettingsCatalogGroup> Groups { get; }

    private void CatalogItem_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Avalonia.Controls.Control { Tag: string pageId })
            return;

        // FASettingsExpanderItem keeps processing its routed click after this handler returns.
        // Replacing the FAFrame content must wait until that input cycle has completed.
        Dispatcher.UIThread.Post(
            () => _ = _settingsNavigator.NavigateAsync(pageId),
            DispatcherPriority.Background);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

public sealed record MobileSettingsCatalogGroup(
    string Name,
    string IconGlyph,
    IReadOnlyList<MobileSettingsCatalogEntry> Pages);

public sealed record MobileSettingsCatalogEntry(string Id, string Name, string IconGlyph);
