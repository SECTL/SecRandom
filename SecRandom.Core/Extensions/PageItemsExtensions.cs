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

    /// <summary>
    /// 平铺生成导航项：不读取 <see cref="PagesRegistryService.GroupItems"/>，每个页面单独成项。
    /// 简洁模式下用它替换分组收纳，白名单页面直接列出。
    /// </summary>
    public static List<FANavigationViewItemBase> ToFlatNavigationViewItems(this IEnumerable<PageInfo> infosEnumerable,
        ObservableCollection<FANavigationViewItemBase> flattenNavigationItems)
    {
        List<FANavigationViewItemBase> navigationViewItems = [];

        foreach (var info in infosEnumerable)
        {
            if (info.IsHide) continue;

            var item = info.ToNavigationViewItemBase();
            flattenNavigationItems.Add(item);
            navigationViewItems.Add(item);
        }

        return navigationViewItems;
    }
}