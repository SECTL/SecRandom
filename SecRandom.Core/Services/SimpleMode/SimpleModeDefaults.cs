using System;
using System.Collections.Generic;
using SecRandom.Core.Enums;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Models.SubConfigs.General;
using SecRandom.Core.Models.SubConfigs.Picking;

namespace SecRandom.Core.Services.SimpleMode;

/// <summary>
///     简洁模式下的默认配置来源。
/// </summary>
/// <remarks>
///     <para>
///         简洁模式隐藏掉的功能不再读取用户设置，而是读取这里的一份默认配置模型。
///         这份模型只在内存里存在，永远不会被保存回 <c>settings.json</c>：
///         用户在完整模式下存过的设置仍然原样留在文件里，关掉简洁模式就全部恢复。
///     </para>
///     <para>
///         默认模型本身是一个 <see cref="MainConfigModel" /> 实例，它的
///         <see cref="MainConfigModel.SimpleModeEnabled" /> 恒为 <see langword="false" />，
///         因此默认模型自己取值时不会再次走进这个分支。
///     </para>
/// </remarks>
public static class SimpleModeDefaults
{
    private static readonly Lazy<MainConfigModel> LazyModel = new(() => new MainConfigModel());

    /// <summary>默认配置模型。所有读取都只读，调用方不得写入。</summary>
    public static MainConfigModel Model => LazyModel.Value;

    /// <summary>简洁模式下点名控件显隐使用的默认「更多设置」（全部控件默认可见）。</summary>
    public static MoreSettingsConfig MoreSettings => Model.MoreSettings;

    /// <summary>简洁模式下浮窗外观使用的默认浮窗设置。</summary>
    public static FloatingWindowSettingsConfig FloatingWindowSettings => Model.FloatingWindowSettings;

    /// <summary>简洁模式下课程联动使用的默认联动设置（数据源关闭、不阻止抽取）。</summary>
    public static LinkageSettingsConfig LinkageSettings => Model.LinkageSettings;

    /// <summary>简洁模式下语音播报使用的默认语音设置（总开关关闭）。</summary>
    public static VoiceSettingsConfig VoiceSettings => Model.VoiceSettings;

    /// <summary>简洁模式下通知使用的默认通知设置（默认通道关闭）。</summary>
    public static NotificationSettingsConfig NotificationSettings => Model.NotificationSettings;

    /// <summary>简洁模式下抽取设置使用的默认「点名设置」。</summary>
    public static RollCallSettingsConfig RollCallSettings => Model.RollCallSettings;

    /// <summary>简洁模式下抽取设置使用的默认「闪抽设置」。</summary>
    public static QuickDrawSettingsConfig QuickDrawSettings => Model.QuickDrawSettings;

    /// <summary>简洁模式下抽取设置使用的默认「默认抽取设置」。</summary>
    public static DefaultDrawSettingsConfig DefaultDrawSettings => Model.DefaultDrawSettings;

    /// <summary>简洁模式下历史记录组使用的默认设置（总开关关闭）。</summary>
    public static HistoryGroupSettingsConfig HistoryGroups => Model.General.HistoryGroups;

    /// <summary>简洁模式下历史管理使用的默认设置。</summary>
    public static HistoryManagementSettingsConfig HistoryManagementSettings => Model.HistoryManagementSettings;

    /// <summary>简洁模式下计时器使用的默认设置。</summary>
    public static TimerSettingsConfig TimerSettings => Model.TimerSettings;

    /// <summary>简洁模式下抽奖设置使用的默认值。</summary>
    public static LotterySettingsConfig LotterySettings => Model.LotterySettings;

    /// <summary>简洁模式下抽取设置使用的默认「公平抽取设置」。</summary>
    public static FairDrawSettingsConfig FairDrawSettings => Model.FairDrawSettings;

    /// <summary>简洁模式下点名页显示/动画/音乐等可覆盖设置使用的默认模型。</summary>
    public static DrawSettingsConfigBase GetOverrideDrawSettings(
        DrawSettingsType drawSettingsType,
        OverridableDrawSettingsType settingsType)
    {
        return Model.GetOverrideDrawSettings(drawSettingsType, settingsType);
    }
}
