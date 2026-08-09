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
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> ExcludedSettingIds =
        new Dictionary<string, IReadOnlySet<string>>
        {
            ["About"] = new HashSet<string> { "S_UserInformation" },
            ["FloatingWindow"] = new HashSet<string>
            {
                "S_Buttons_RollCall", "S_Buttons_QuickDraw", "S_Buttons_Lottery",
                "S_Interaction_DoNotStealFocus", "S_Interaction_HideOnForeground",
                "S_Interaction_HideOnForegroundProcessNames", "S_Interaction_HideOnForegroundWindowTitles",
                "S_Interaction_LongPressDuration"
            },
            ["General.Security"] = new HashSet<string>
            {
                "S_Verification", "S_Verification_Password", "S_Verification_Totp", "S_Verification_UsbBinding",
                "S_Protection_SensitiveOperations", "S_Protection_LinkageOperations"
            },
            ["General.Verification"] = new HashSet<string> { "S_Verification" },
            ["HistoryManagement"] = new HashSet<string>
            {
                "S_History", "S_History_ShowRollCall", "S_History_ShowLottery", "S_History_SelectWeight",
                "S_Filter", "S_Filter_SelectedClassName", "S_Filter_SelectedPoolName"
            },
            ["General.Backup"] = new HashSet<string>
            {
                "S_Includes_Audio", "S_Includes_Config", "S_Includes_Cses", "S_Includes_History",
                "S_Includes_Images", "S_Includes_List", "S_Includes_Logs", "S_Includes_Proofs",
                "S_Includes_Themes"
            },
            ["Notification"] = new HashSet<string>
            {
                "S_Common_BasicSettings", "S_Common_EnabledMonitor", "S_Common_FloatingWindow",
                "S_Common_FloatingWindowAutoCloseTime", "S_Common_FloatingWindowEnabledMonitor",
                "S_Common_FloatingWindowOffset", "S_Common_FloatingWindowPosition",
                "S_Common_FloatingWindowTransparency", "S_Common_NotificationService",
                "S_Common_NotificationServiceFailureFallback", "S_Common_NotificationServiceType",
                "S_Common_NotificationWindowSettings", "S_Common_OverridableSettings"
            },
            ["Picking"] = new HashSet<string> { "S_MusicFadeInOut", "S_MusicVolume" },
            ["Update"] = new HashSet<string> { "S_Strategy_Source" },
            ["Voice"] = new HashSet<string>
            {
                "S_Playback_EdgeTtsVoice", "S_SystemVolume", "S_SystemVolume_Control", "S_SystemVolume_Size"
            }
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> PickingPageSettingIds =
        new Dictionary<string, IReadOnlySet<string>>
        {
            ["settings.picking.default"] = new HashSet<string>
            {
                "S_DrawMode", "S_HalfRepeat", "S_FontSource", "S_CustomFont", "S_DisplayStyle",
                "S_ShowWeightTransparency", "S_FontSize", "S_DisplayFormat", "S_ShowTags", "S_Animation",
                "S_AnimationInterval", "S_AutoplayCount", "S_AnimationStyle", "S_ColorTheme", "S_FixedColor",
                "S_StudentImage", "S_StudentImagePosition", "S_AnimationMusic", "S_ResultMusic",
                "S_AnimationMusicLoop", "S_AnimationMusicVolume", "S_ResultMusicVolume", "S_AnimationMusicFade",
                "S_ResultMusicFade", "S_VoiceAnnouncementEnabled", "S_ReminderText", "S_ReminderFontSize",
                "S_ReminderTextColor", "S_ReminderTextOpacity"
            },
            ["settings.picking.rollCall"] = new HashSet<string>
            {
                "S_DrawMode", "S_HalfRepeat", "S_DrawType", "S_DefaultClass", "S_ClearRecord", "S_FontSource",
                "S_CustomFont", "S_FontSize", "S_DisplayFormat", "S_DisplayStyle", "S_ShowWeightTransparency",
                "S_ShowTags", "S_Animation", "S_AnimationInterval", "S_AutoplayCount", "S_AnimationStyle",
                "S_ColorTheme", "S_FixedColor", "S_StudentImage", "S_StudentImagePosition", "S_AnimationMusic",
                "S_ResultMusic", "S_AnimationMusicLoop", "S_AnimationMusicVolume", "S_ResultMusicVolume",
                "S_AnimationMusicFade", "S_ResultMusicFade", "S_VoiceAnnouncementEnabled", "S_ReminderText",
                "S_ReminderFontSize", "S_ReminderTextColor", "S_ReminderTextOpacity"
            },
            ["settings.picking.quickDraw"] = new HashSet<string>
            {
                "S_DrawMode", "S_HalfRepeat", "S_DrawType", "S_DefaultClass", "S_DisableAfterClick", "S_FontSource",
                "S_CustomFont", "S_FontSize", "S_DisplayFormat", "S_ShowTags", "S_Animation", "S_AnimationInterval",
                "S_AutoplayCount", "S_AnimationStyle", "S_ColorTheme", "S_FixedColor", "S_StudentImage",
                "S_StudentImagePosition", "S_AnimationMusic", "S_ResultMusic", "S_AnimationMusicLoop",
                "S_AnimationMusicVolume", "S_ResultMusicVolume", "S_AnimationMusicFade", "S_ResultMusicFade",
                "S_VoiceAnnouncementEnabled"
            },
            ["settings.picking.lottery"] = new HashSet<string>
            {
                "S_DrawMode", "S_HalfRepeat", "S_LotteryDrawType", "S_DefaultPool", "S_ClearRecord", "S_FontSource",
                "S_CustomFont", "S_FontSize", "S_DisplayStyle", "S_LotteryShowRandom", "S_LotteryShowRandomFormat", "S_ShowTags",
                "S_ShowWeightTransparency", "S_Animation", "S_AnimationInterval", "S_AutoplayCount",
                "S_AnimationStyle", "S_ColorTheme", "S_FixedColor", "S_LotteryImage", "S_LotteryImagePosition",
                "S_AnimationMusic", "S_ResultMusic", "S_AnimationMusicLoop", "S_AnimationMusicVolume",
                "S_ResultMusicVolume", "S_AnimationMusicFade", "S_ResultMusicFade", "S_VoiceAnnouncementEnabled",
                "S_ReminderText", "S_ReminderFontSize", "S_ReminderTextColor", "S_ReminderTextOpacity"
            }
        };

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
        var pageMetadataIds = new HashSet<string>();

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

            var resourcePageInfos = FindSettingsPageInfos(settingsPageResourceId).ToList();
            if (resourcePageInfos.Count == 0 && settingsPageResourceId != @"Notification")
            {
                _logger.LogDebug("Skipping settings search metadata for resource without page: {Resource}",
                    resourceType.FullName);
                continue;
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
                IEnumerable<PageInfo> settingsPageInfos = settingsPageResourceId == @"Notification"
                    ? new[] { FindSettingsPageInfo(settingsPageResourceId, rootId) }.OfType<PageInfo>()
                    : resourcePageInfos;

                foreach (var settingsPageInfo in settingsPageInfos)
                {
                    if (pageMetadataIds.Add(settingsPageInfo.Id))
                    {
                        SettingsMetadata.Add(new SettingsMetadata
                        {
                            IsPage = true,
                            PageId = settingsPageInfo.Id,
                            PageName = settingsPageInfo.Name,
                            Id = settingsPageInfo.Id,
                            ControlId = settingsPageInfo.Id,
                            Name = settingsPageInfo.Name
                        });
                    }

                    if (!IsSearchableSetting(settingsPageResourceId, settingsPageInfo.Id, rootId))
                        continue;

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
                        CategoryControlId = GetCategoryControlId(settingsPageResourceId, rootId),
                        Id = rootId,
                        ControlId = GetControlId(settingsPageResourceId, rootId, false),
                        Name = rootName,
                        Description = rootDescription
                    });

                    foreach (var subId in subSettings.GetValueOrDefault(rootId, []))
                    {
                        var fullId = rootId + @"_" + subId;
                        if (!IsSearchableSetting(settingsPageResourceId, settingsPageInfo.Id, fullId))
                            continue;

                        var subDescription =
                            (string?)properties.FirstOrDefault(property => property.Name == fullId + "_D")
                                ?.GetValue(null) ?? string.Empty;
                        SettingsMetadata.Add(new SettingsMetadata
                        {
                            PageId = settingsPageInfo.Id,
                            PageName = settingsPageInfo.Name,
                            CategoryId = rootId,
                            CategoryName = rootName,
                            CategoryControlId = GetCategoryControlId(settingsPageResourceId, fullId),
                            Id = fullId,
                            ControlId = GetControlId(settingsPageResourceId, fullId, false),
                            Name = (string)properties.First(property => property.Name == fullId).GetValue(null)!,
                            Description = subDescription
                        });
                    }
                }
            }
        }
    }

    private static bool IsSearchableSetting(string resourcePageId, string pageId, string settingId)
    {
        if (ExcludedSettingIds.TryGetValue(resourcePageId, out var excluded) && excluded.Contains(settingId))
            return false;

        if (resourcePageId == @"Notification" && pageId == @"settings.notification.default"
            && settingId is "S_Default_Enabled" or "S_Default_Animation")
            return false;

        return resourcePageId != @"Picking"
               || PickingPageSettingIds[pageId].Contains(settingId);
    }

    private static string GetControlId(string resourcePageId, string settingId, bool category)
    {
        if (resourcePageId != @"Notification") return settingId;

        if (category || settingId == @"S_Default") return settingId;
        return @"S_Default_" + settingId[(settingId.IndexOf('_') + 1)..];
    }

    private static string GetCategoryControlId(string resourcePageId, string settingId)
    {
        if (resourcePageId != @"Notification") return settingId;

        return settingId.EndsWith("_WindowPosition", StringComparison.Ordinal)
               || settingId.EndsWith("_EnabledMonitor", StringComparison.Ordinal)
               || settingId.EndsWith("_Offset", StringComparison.Ordinal)
               || settingId.EndsWith("_Transparency", StringComparison.Ordinal)
            ? @"S_Default_NotificationWindowSettings"
            : settingId.EndsWith("_NotificationServiceType", StringComparison.Ordinal)
              || settingId.EndsWith("_NotificationServiceFailureFallback", StringComparison.Ordinal)
              || settingId.EndsWith("_DisplayDuration", StringComparison.Ordinal)
              || settingId.EndsWith("_UseMainWindowWhenExceedThreshold", StringComparison.Ordinal)
              || settingId.EndsWith("_MainWindowDisplayThreshold", StringComparison.Ordinal)
                ? @"S_Default_NotificationService"
                : settingId.IndexOf('_', 2) is var separator && separator >= 0
                    ? settingId[..separator]
                    : settingId;
    }

    public void LogTestInformation()
    {
        foreach (var metadata in SettingsMetadata)
            _logger.LogDebug(@"{Content} [{Id}]", metadata.ToString(), metadata.Id);
    }

    private static PageInfo? FindSettingsPageInfo(string settingsPageResourceId, string? rootSettingId = null)
    {
        return FindSettingsPageInfos(settingsPageResourceId, rootSettingId).FirstOrDefault();
    }

    private static IEnumerable<PageInfo> FindSettingsPageInfos(string settingsPageResourceId, string? rootSettingId = null)
    {
        var candidates = BuildPageClassNameCandidates(settingsPageResourceId, rootSettingId).ToHashSet();
        return PagesRegistryService.SettingsItems.Where(info =>
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

        if (settingsPageResourceId == @"Picking")
        {
            yield return @"SecRandom.Views.SettingsPages.Picking.DefaultDrawSettingsPage";
            yield return @"SecRandom.Views.SettingsPages.Picking.RollCallDrawSettingsPage";
            yield return @"SecRandom.Views.SettingsPages.Picking.QuickDrawSettingsPage";
            yield return @"SecRandom.Views.SettingsPages.Picking.LotteryDrawSettingsPage";
            yield break;
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
