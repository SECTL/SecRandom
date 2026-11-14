using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Attributes;
using SecRandom.Core.Services;
using SecRandom.Core.Services.Ipc;
using SecRandom.Core.Services.SimpleMode;
using SecRandom.Shared.Models;

namespace SecRandom.Services.Ipc;

public enum PageRouteKind
{
    Main,
    Settings
}

public sealed record PageRouteResult(
    bool Success,
    string? PageId,
    string? ErrorCode = null,
    string? Message = null)
{
    public static PageRouteResult Ok(string pageId) => new(true, pageId);

    public static PageRouteResult Fail(string code, string message) =>
        new(false, null, code, message);
}

/// <summary>
/// Resolves a page id against the real page registry instead of a hard-coded alias table.
/// Legacy aliases stay accepted; hidden pages stay unreachable; simple mode and the lottery
/// switch are applied after registry resolution so a plugin page follows the same rules.
/// </summary>
public sealed class PageRouteResolver(IFeatureAvailabilityService featureAvailability)
{
    public const string DefaultMainPage = "main.rollCall";
    public const string DefaultSettingsPage = "settings.general.basic";

    private static readonly Dictionary<string, string> MainAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["roll_call_page"] = "main.rollCall",
        ["roll"] = "main.rollCall",
        ["lottery_page"] = "main.lottery",
        ["lottery"] = "main.lottery",
        ["history_page"] = "main.history",
        ["history"] = "main.history"
    };

    private static readonly Dictionary<string, string> SettingsAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["basicsettingsinterface"] = "settings.general.basic",
        ["listmanagementinterface"] = "settings.listManagement.rollCallList",
        ["extractionsettingsinterface"] = "settings.picking.default",
        ["floatingwindowmanagementinterface"] = "settings.personalized.floatingWindow",
        ["notificationsettingsinterface"] = "settings.notification.default",
        ["safetysettingsinterface"] = "settings.general.security",
        ["customsettingsinterface"] = "settings.more",
        ["moresettingsinterface"] = "settings.more",
        ["voicesettingsinterface"] = "settings.notification.voiceMusic",
        ["historyinterface"] = "settings.history.management",
        ["updateinterface"] = "settings.update",
        ["aboutinterface"] = "settings.about"
    };

    public PageRouteResult Resolve(PageRouteKind kind, string? page)
    {
        var requested = page?.Trim();
        if (kind == PageRouteKind.Main)
        {
            if (string.IsNullOrEmpty(requested))
                requested = DefaultMainPage;
            else if (MainAliases.TryGetValue(requested, out var alias))
                requested = alias;

            var pageInfo = FindMainPage(requested);
            if (pageInfo is null)
                return PageRouteResult.Fail(AutomationErrorCodes.PageNotFound,
                    $"主页未注册: {requested}");

            if (pageInfo.IsHide)
                return PageRouteResult.Fail(AutomationErrorCodes.PageNotAvailable,
                    $"主页不可访问: {requested}");

            if (featureAvailability.IsSimpleModeEnabled &&
                !SimpleModePolicy.IsMainPageAllowed(pageInfo.Id))
                return PageRouteResult.Fail(AutomationErrorCodes.PageNotAvailable,
                    "简洁模式下该主页不可访问。");

            if (string.Equals(pageInfo.Id, "main.lottery", StringComparison.OrdinalIgnoreCase) &&
                !featureAvailability.IsLotteryEnabled)
                return PageRouteResult.Fail(AutomationErrorCodes.FeatureDisabled,
                    "抽奖功能已关闭。");

            return PageRouteResult.Ok(pageInfo.Id);
        }

        if (string.IsNullOrEmpty(requested))
            requested = DefaultSettingsPage;
        else if (SettingsAliases.TryGetValue(requested, out var settingsAlias))
            requested = settingsAlias;

        var settingsPage = FindSettingsPage(requested);
        if (settingsPage is null)
            return PageRouteResult.Fail(AutomationErrorCodes.PageNotFound,
                $"设置页未注册: {requested}");

        if (settingsPage.IsHide)
            return PageRouteResult.Fail(AutomationErrorCodes.PageNotAvailable,
                $"设置页不可访问: {requested}");

        if (featureAvailability.IsSimpleModeEnabled &&
            !SimpleModePolicy.IsSettingsPageAllowed(settingsPage.Id))
            return PageRouteResult.Fail(AutomationErrorCodes.PageNotAvailable,
                "简洁模式下该设置页不可访问。");

        return PageRouteResult.Ok(settingsPage.Id);
    }

    public string? NormalizeSettingsPage(string? page)
    {
        var result = Resolve(PageRouteKind.Settings, page);
        return result.Success ? result.PageId : null;
    }

    private static PageInfo? FindMainPage(string pageId) =>
        PagesRegistryService.MainItems.FirstOrDefault(item =>
            !item.IsSeparator &&
            string.Equals(item.Id, pageId, StringComparison.OrdinalIgnoreCase));

    private static PageInfo? FindSettingsPage(string pageId) =>
        PagesRegistryService.SettingsItems.FirstOrDefault(item =>
            !item.IsSeparator &&
            string.Equals(item.Id, pageId, StringComparison.OrdinalIgnoreCase));
}