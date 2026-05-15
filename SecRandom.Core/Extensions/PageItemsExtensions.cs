using System.Collections.ObjectModel;
using DynamicData;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Attributes;
using SecRandom.Core.Controls;
using SecRandom.Core.Services;

namespace SecRandom.Core.Extensions;

public static class PageItemsExtensions
{
    public static List<FANavigationViewItemBase> ToNavigationViewItems(this IEnumerable<PageInfo> infosEnumerable,
        ObservableCollection<FANavigationViewItemBase> flattenNavigationItems)
    {
        var infos = infosEnumerable
            .Where(info => !info.IsHide)
            .ToList();
        var groups = infos
            .GroupBy(x => x.GroupId)
            .ToList();
        var addedGroups = new HashSet<string>();
        List<FANavigationViewItemBase> navigationViewItems = [];

        foreach (var i in infos)
        {
            if (i.GroupId != null && addedGroups.Contains(i.GroupId)) continue;

            FANavigationViewItemBase item;

            var group = PagesRegistryService.GroupItems.FirstOrDefault(group => group.Id == i.GroupId);
            if (i.GroupId != null && group != null)
            {
                var groupItem = new FANavigationViewItem
                {
                    IconSource = new FluentIconSource(group.IconGlyph),
                    Content = group.Name,
                    Tag = group
                };

                if (groups.FirstOrDefault(x => x.Key == i.GroupId) is { } groupItems)
                {
                    var children = groupItems
                        .Select(x => x.ToNavigationViewItemBase())
                        .ToList();
                    flattenNavigationItems.AddRange(children);
                    groupItem.MenuItems.AddRange(children);
                }

                addedGroups.Add(i.GroupId);
                item = groupItem;
            }
            else
            {
                item = i.ToNavigationViewItemBase();
                flattenNavigationItems.Add(item);
            }

            navigationViewItems.Add(item);
        }

        return navigationViewItems;
    }
}