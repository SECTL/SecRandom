using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums.Configs;

namespace SecRandom.Core.Models.SubConfigs.Picking;

public partial class OverridableDrawSettings : DrawSettingsConfigBase
{
    [ObservableProperty] private bool _overrideDisplaySettings = false;
    [ObservableProperty] private bool _overrideAnimationSettings = false;
    [ObservableProperty] private bool _overrideColorSettings = false;
    [ObservableProperty] private bool _overrideStudentImageSettings = false;
    [ObservableProperty] private bool _overrideReminderSettings = false;
    [ObservableProperty] private bool _overrideMusicSettings = false;
    [ObservableProperty] private bool _overrideVoiceAnnouncementSettings = false;

    /// <summary>
    ///     进入编排抽取之前生效的重复策略。
    /// </summary>
    /// <remarks>
    ///     编排抽取不是重复策略（顺序早在抽取前就排定了），所以它只出现在「抽取方式」下拉里，和随机抽取、
    ///     公平抽取摆在同一组；用户一旦选了它，各通道配置的 <c>DrawMode</c> 就必须变成
    ///     <see cref="DrawMode.Orchestrated" />，否则抽取端根本不会走编排。这个字段把当时的
    ///     允许重复 / 不重复 / 半重复记下来，改回随机或公平抽取时恢复，
    ///     否则一次编排就会把用户的半重复档位悄悄压成不重复。
    /// </remarks>
    [ObservableProperty] private DrawMode _sampledDrawMode = DrawMode.NoRepeat;
}