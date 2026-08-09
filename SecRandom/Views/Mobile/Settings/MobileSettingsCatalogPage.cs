using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;
using SecRandom.Core.Services;
using SecRandom.Services.Mobile;
using SecRandom.Views.Mobile;

namespace SecRandom.Views.Mobile.Settings;

/// <summary>
/// Mobile settings navigation shell. It lists the registered desktop settings pages plus the mobile update page.
/// </summary>
[PageInfo(MobilePageIds.Settings, FluentIcons.SettingsFilled, isHide: true)]
public sealed partial class MobileSettingsCatalogPage : Avalonia.Controls.UserControl
{
    private readonly IMobileSettingsNavigator _settingsNavigator;

    public MobileSettingsCatalogPage(IMobileSettingsNavigator settingsNavigator)
    {
        _settingsNavigator = settingsNavigator;
        InitializeComponent();
        var pages = PagesRegistryService.SettingsItems
            .Where(page => !page.IsSeparator && !page.IsHide && page.Id != MobilePageIds.Settings)
            .ToArray();
        var addedGroups = new HashSet<string>();
        var items = new List<MobileSettingsCatalogItem>();

        foreach (var page in pages)
        {
            if (page.GroupId is { } groupId && addedGroups.Add(groupId))
            {
                var group = PagesRegistryService.GroupItems.FirstOrDefault(item => item.Id == groupId);
                if (group is not null)
                {
                    items.Add(new MobileSettingsCatalogItem(
                        group.Name,
                        group.IconGlyph,
                        null,
                        pages.Where(item => item.GroupId == groupId).Select(CreateEntry).ToArray()));
                    continue;
                }
            }

            if (page.GroupId is null || PagesRegistryService.GroupItems.All(item => item.Id != page.GroupId))
                items.Add(new MobileSettingsCatalogItem(page.Name, page.IconGlyph, page.Id, []));
        }

        Items = items;
        DataContext = this;
    }

    public IReadOnlyList<MobileSettingsCatalogItem> Items { get; }

    private static MobileSettingsCatalogEntry CreateEntry(PageInfo page) => new(page.Id, page.Name, page.IconGlyph);

    private void CatalogItem_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Avalonia.Controls.Control { Tag: string pageId })
            return;

        Dispatcher.UIThread.Post(
            () => _ = _settingsNavigator.NavigateAsync(pageId),
            DispatcherPriority.Background);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

public sealed record MobileSettingsCatalogItem(
    string Name,
    string IconGlyph,
    string? PageId,
    IReadOnlyList<MobileSettingsCatalogEntry> Pages)
{
    public bool IsPage => PageId is not null;
}

public sealed record MobileSettingsCatalogEntry(string Id, string Name, string IconGlyph);
