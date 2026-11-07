using System;
using System.Globalization;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Models.Linkage;
using SecRandom.Views;
using LR = SecRandom.Langs.SettingsPages.Linkage.Resources;

namespace SecRandom.Services.Linkage;

/// <summary>
///     “课间与下课禁止抽取”被拦下时的用户提示。
/// </summary>
/// <remarks>
///     点名、即时抽取、抽奖三处入口都只经过 <see cref="LinkageDrawCoordinator.AuthorizeAsync" /> 这一道门，
///     所以提示也只在这里发一次，不往各页面里散写判空。集控远程路径走
///     <see cref="LinkageDrawCoordinator.EvaluateGate" />，它刻意不提示 —— 教室机可能没人，
///     弹提示只会让远程命令一直挂在没人看的窗口上。
///     <para>
///         没有活动主壳（手机单视图、浮窗单独抽、宿主尚未建窗口）时 <see cref="MainView.Current" /> 为空，
///         提示静默跳过，拦截结果不受影响；<see cref="MainView.ShowToast" /> 内部会自己切到 UI 线程。
///     </para>
///     <para>
///         节流窗口 <see cref="ThrottleWindow" /> 内的重复拦截只提示一次，按住按钮连点不会刷屏。
///     </para>
/// </remarks>
internal static class ClassTimeDrawRestrictionNotice
{
    private static readonly TimeSpan ThrottleWindow = TimeSpan.FromSeconds(5);
    private static readonly object ThrottleGate = new();
    private static long _lastShownAt = long.MinValue;

    /// <summary>提示一次“当前不能抽取”，并带上下一节课的恢复时间。</summary>
    public static void Show(CourseScheduleSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!TryEnterThrottleWindow())
            return;

        MainView.ShowToast(BuildMessage(snapshot), FAInfoBarSeverity.Warning);
    }

    internal static string BuildMessage(CourseScheduleSnapshot snapshot)
    {
        var nextCourseName = snapshot.NextCourse?.Name;
        var minutesUntilNext = snapshot.TimeUntilNextCourse is { } untilNext && untilNext > TimeSpan.Zero
            ? (int)Math.Ceiling(untilNext.TotalMinutes)
            : 0;

        if (!string.IsNullOrWhiteSpace(nextCourseName) && minutesUntilNext > 0)
            return string.Format(CultureInfo.CurrentCulture, Text("M_ClassTimeDrawBlocked"), nextCourseName,
                minutesUntilNext);

        return Text("M_ClassTimeDrawBlockedGeneric");
    }

    private static string Text(string key)
    {
        return LR.ResourceManager.GetString(key, LR.Culture) ?? key;
    }

    private static bool TryEnterThrottleWindow()
    {
        var now = Environment.TickCount64;
        lock (ThrottleGate)
        {
            if (_lastShownAt != long.MinValue && now - _lastShownAt < (long)ThrottleWindow.TotalMilliseconds)
                return false;
            _lastShownAt = now;
            return true;
        }
    }
}
