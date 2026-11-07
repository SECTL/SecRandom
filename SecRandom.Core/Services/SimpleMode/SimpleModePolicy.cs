using System;
using System.Collections.Generic;
using SecRandom.Core.Models;

namespace SecRandom.Core.Services.SimpleMode;

/// <summary>
///     简洁模式的页面白名单策略。
/// </summary>
/// <remarks>
///     这里只描述「简洁模式下还能进哪些页面」。功能本身的开关一律由
///     <c>IFeatureAvailabilityService</c> 与各窗口/命令入口判断，避免两边各写一套。
///     页面 ID 是稳定的契约字符串，因此可以放在 Core 里被桌面与移动端共用。
/// </remarks>
public static class SimpleModePolicy
{
    /// <summary>简洁模式下唯一可见的主页面。</summary>
    public const string DefaultMainPageId = @"main.rollCall";

    /// <summary>设置窗口的默认页面。</summary>
    public const string DefaultSettingsPageId = @"settings.overview";

    private static readonly HashSet<string> AllowedMainPages = new(StringComparer.Ordinal)
    {
        DefaultMainPageId
    };

    private static readonly HashSet<string> AllowedSettingsPages = new(StringComparer.Ordinal)
    {
        @"settings.overview",
        @"settings.general.basic",
        @"settings.personalized.appearance",
        @"settings.listManagement.rollCallList",
        @"settings.general.privacy",
        @"settings.update",
        @"settings.about"
    };

    /// <summary>简洁模式下是否允许进入该主页面。空值表示"不指定页面"，允许。</summary>
    public static bool IsMainPageAllowed(string? pageId)
    {
        return string.IsNullOrWhiteSpace(pageId) || AllowedMainPages.Contains(pageId);
    }

    /// <summary>简洁模式下是否允许进入该设置页面。空值表示"不指定页面"，允许。</summary>
    public static bool IsSettingsPageAllowed(string? pageId)
    {
        return string.IsNullOrWhiteSpace(pageId) || AllowedSettingsPages.Contains(pageId);
    }

    /// <summary>把主页面 ID 归一成简洁模式允许的页面；不允许时退回点名页。</summary>
    public static string NormalizeMainPageId(string? pageId)
    {
        return IsMainPageAllowed(pageId) && !string.IsNullOrWhiteSpace(pageId)
            ? pageId
            : DefaultMainPageId;
    }

    /// <summary>把设置页面 ID 归一成简洁模式允许的页面；不允许时返回 <see langword="null" />（使用默认页）。</summary>
    public static string? NormalizeSettingsPageId(string? pageId)
    {
        if (string.IsNullOrWhiteSpace(pageId))
            return null;

        return IsSettingsPageAllowed(pageId) ? pageId : null;
    }

    /// <summary>
    ///     只保留简洁模式允许的设置页，用于生成设置窗口导航。分组头由子项是否保留决定，
    ///     因此过滤完之后没有子项的分组会自然消失。
    /// </summary>
    public static IEnumerable<T> FilterSettingsPages<T>(IEnumerable<T> items, Func<T, string?> pageIdSelector)
    {
        foreach (var item in items)
        {
            if (IsSettingsPageAllowed(pageIdSelector(item)))
                yield return item;
        }
    }
}

/// <summary>
///     简洁模式运行时判定：唯一的事实来源是配置模型里的开关。
/// </summary>
public static class SimpleModeRuntime
{
    /// <summary>该配置是否处于简洁模式。</summary>
    public static bool IsEnabled(MainConfigModel config)
    {
        return config.General.SimpleMode.Enabled;
    }
}
