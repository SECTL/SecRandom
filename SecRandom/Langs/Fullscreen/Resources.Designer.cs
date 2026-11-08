using System.Globalization;
using System.Resources;

#nullable enable

namespace SecRandom.Langs.Fullscreen;

/// <summary>
///     全屏结果提示窗（<see cref="SecRandom.Views.FullscreenNotificationWindow" />）以及它内部三种动画的
///     可见文案。
/// </summary>
/// <remarks>
///     这是一个手写的强类型资源类：每个 resx 键都必须在这里有一个同名静态属性，否则调用方拿不到该键
///     （Avalonia 的 XAML 标记编译与 resx 生成不会替我们检查这一点）。
///     取词条时不缓存结果：窗口每次打开都重新读，切换语言后重开窗口即可生效。
/// </remarks>
internal static class Resources
{
    private static readonly ResourceManager Manager =
        new("SecRandom.Langs.Fullscreen.Resources", typeof(Resources).Assembly);

    /// <summary>
    ///     覆盖当前线程的 UI 文化；保持 <c>null</c> 时按 <see cref="CultureInfo.CurrentUICulture" /> 取词条，
    ///     这正是应用统一设置语言（<c>App</c> 切换 <c>CultureInfo.CurrentUICulture</c>）后的期望行为。
    /// </summary>
    internal static CultureInfo? Culture { get; set; }

    private static string Text(string key) =>
        Manager.GetString(key, Culture ?? CultureInfo.CurrentUICulture) ?? key;

    internal static string Fullscreen_Title => Text(nameof(Fullscreen_Title));
    internal static string Fullscreen_IdleTitle => Text(nameof(Fullscreen_IdleTitle));
    internal static string Fullscreen_IdleRolling => Text(nameof(Fullscreen_IdleRolling));
    internal static string Fullscreen_IdleHint => Text(nameof(Fullscreen_IdleHint));
    internal static string Fullscreen_CloseHint => Text(nameof(Fullscreen_CloseHint));
    internal static string Fullscreen_KindRollCall => Text(nameof(Fullscreen_KindRollCall));
    internal static string Fullscreen_KindLottery => Text(nameof(Fullscreen_KindLottery));
    internal static string Fullscreen_KindQuickDraw => Text(nameof(Fullscreen_KindQuickDraw));

    /// <summary>倒计时读秒，<c>{0}</c> 是剩余秒数。</summary>
    internal static string Fullscreen_RemainingCountdown => Text(nameof(Fullscreen_RemainingCountdown));

    /// <summary>剩余人数，<c>{0}</c> 是剩余人数。</summary>
    internal static string Fullscreen_RemainingPeople => Text(nameof(Fullscreen_RemainingPeople));

    /// <summary>剩余奖品数，<c>{0}</c> 是剩余件数。</summary>
    internal static string Fullscreen_RemainingPrizes => Text(nameof(Fullscreen_RemainingPrizes));

    /// <summary>剩余 / 总人数，<c>{0}</c> 是剩余人数、<c>{1}</c> 是总人数。</summary>
    internal static string Fullscreen_RemainingPeopleWithTotal =>
        Text(nameof(Fullscreen_RemainingPeopleWithTotal));

    /// <summary>剩余 / 总奖品数，<c>{0}</c> 是剩余件数、<c>{1}</c> 是总件数。</summary>
    internal static string Fullscreen_RemainingPrizesWithTotal =>
        Text(nameof(Fullscreen_RemainingPrizesWithTotal));

    internal static string Fullscreen_GridOverflow => Text(nameof(Fullscreen_GridOverflow));
}
