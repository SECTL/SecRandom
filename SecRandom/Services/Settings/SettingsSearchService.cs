using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Attributes;
using SecRandom.Core.Services;
using SecRandom.Models;

namespace SecRandom.Services.Settings;

public class SettingsSearchService
{
    private readonly ILogger<SettingsSearchService> _logger;

    public SettingsSearchService(ILogger<SettingsSearchService> logger)
    {
        _logger = logger;
        GenerateMetadata();
    }

    public List<SettingsMetadata> SettingsMetadata { get; } = [];

    public void GenerateMetadata()
    {
        SettingsMetadata.Clear();

        var resources = Assembly.GetExecutingAssembly().DefinedTypes
            .Where(info => info.Namespace?.StartsWith(@"SecRandom.Langs.SettingsPages") ?? false)
            .OrderBy(info => info.FullName ?? @"???")
            .ToList();

        foreach (var resourceType in resources)
        {
            // 解析设置界面
            var settingsPageResourceId = resourceType.FullName?
                .Replace(@"SecRandom.Langs.SettingsPages.", "").Replace(@".Resources", "");
            if (settingsPageResourceId == null) continue;

            var resourcePageInfo = FindSettingsPageInfo(settingsPageResourceId);
            if (resourcePageInfo == null && settingsPageResourceId != @"Notification")
            {
                _logger.LogDebug("Skipping settings search metadata for resource without page: {Resource}",
                    resourceType.FullName);
                continue;
            }

            if (resourcePageInfo != null)
            {
                SettingsMetadata.Add(new SettingsMetadata
                {
                    IsPage = true,
                    PageId = resourcePageInfo.Id,
                    PageName = resourcePageInfo.Name,
                    Id = resourcePageInfo.Id,
                    Name = resourcePageInfo.Name
                });
            }

            // 解析子设置
            var properties = resourceType.DeclaredProperties.ToList();

            List<string> rootSettings = [];
            Dictionary<string, List<string>> subSettings = [];
            foreach (var declaredProperty in properties)
            {
                if (!declaredProperty.Name.StartsWith(@"S_") ||
                    declaredProperty.Name.EndsWith(@"_R") ||
                    declaredProperty.Name.EndsWith(@"_D"))
                    continue;

                if (declaredProperty.Name.Count(c => c == '_') == 1) rootSettings.Add(declaredProperty.Name);

                if (declaredProperty.Name.Count(c => c == '_') == 2)
                {
                    var parts = declaredProperty.Name.Split('_');
                    var category = parts[0] + @"_" + parts[1];

                    if (!subSettings.ContainsKey(category)) subSettings[category] = [];

                    subSettings[category].Add(parts[2]);
                }
            }

            foreach (var rootId in rootSettings)
            {
                var settingsPageInfo = FindSettingsPageInfo(settingsPageResourceId, rootId);
                if (settingsPageInfo == null)
                {
                    _logger.LogDebug(
                        "Skipping settings search metadata for resource root without page: {Resource}.{RootId}",
                        resourceType.FullName,
                        rootId);
                    continue;
                }

                var rootName = (string)properties.First(property => property.Name == rootId).GetValue(null)!;
                var rootDescription =
                    (string?)properties.FirstOrDefault(property => property.Name == rootId + "_D")?.GetValue(null) ??
                    string.Empty;
                SettingsMetadata.Add(new SettingsMetadata
                {
                    PageId = settingsPageInfo.Id,
                    PageName = settingsPageInfo.Name,
                    IsCategory = true,
                    CategoryId = rootId,
                    CategoryName = rootName,
                    Id = rootId,
                    Name = rootName,
                    Description = rootDescription
                });

                foreach (var subId in subSettings.GetValueOrDefault(rootId, []))
                {
                    var fullId = rootId + @"_" + subId;
                    var subDescription =
                        (string?)properties.FirstOrDefault(property => property.Name == fullId + "_D")
                            ?.GetValue(null) ?? string.Empty;
                    SettingsMetadata.Add(new SettingsMetadata
                    {
                        PageId = settingsPageInfo.Id,
                        PageName = settingsPageInfo.Name,
                        CategoryId = rootId,
                        CategoryName = rootName,
                        Id = fullId,
                        Name = (string)properties.First(property => property.Name == fullId).GetValue(null)!,
                        Description = subDescription
                    });
                }
            }
        }
    }

    public void LogTestInformation()
    {
        foreach (var metadata in SettingsMetadata)
            _logger.LogDebug(@"{Content} [{Id}]", metadata.ToString(), metadata.Id);
    }

    private static PageInfo? FindSettingsPageInfo(string settingsPageResourceId, string? rootSettingId = null)
    {
        var candidates = BuildPageClassNameCandidates(settingsPageResourceId, rootSettingId).ToHashSet();
        return PagesRegistryService.SettingsItems.FirstOrDefault(info =>
            info.SettingsPageType?.FullName is { } fullName && candidates.Contains(fullName));
    }

    private static IEnumerable<string> BuildPageClassNameCandidates(string settingsPageResourceId, string? rootSettingId)
    {
        if (settingsPageResourceId == @"Notification" && rootSettingId != null)
        {
            var notificationPageName = rootSettingId switch
            {
                @"S_RollCall" => @"RollCallNotificationSettingsPage",
                @"S_QuickDraw" => @"QuickDrawNotificationSettingsPage",
                @"S_Lottery" => @"LotteryNotificationSettingsPage",
                _ => @"DefaultNotificationSettingsPage"
            };

            yield return @"SecRandom.Views.SettingsPages.Notification." + notificationPageName;
        }

        var segments = settingsPageResourceId.Split('.');
        var lastSegment = segments[^1];

        yield return @"SecRandom.Views.SettingsPages." + settingsPageResourceId + @"SettingsPage";
        yield return @"SecRandom.Views.SettingsPages." + settingsPageResourceId + @"." + lastSegment + @"SettingsPage";

        if (segments.Length > 1)
        {
            var parentSegment = segments[^2];
            yield return @"SecRandom.Views.SettingsPages." + settingsPageResourceId + @"." + parentSegment + @"SettingsPage";
        }

        var pageName = settingsPageResourceId.Replace(@".", string.Empty) + @"SettingsPage";
        foreach (var info in PagesRegistryService.SettingsItems)
        {
            if (info.SettingsPageType?.Name == pageName && info.SettingsPageType.FullName is { } fullName)
                yield return fullName;
        }
    }
}
