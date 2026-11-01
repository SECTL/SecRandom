using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums.Configs;

namespace SecRandom.Core.Models.SubConfigs.Picking;

public partial class QuickDrawSettingsConfig : OverridableDrawSettings
{
    [ObservableProperty] private DrawMode _drawMode = DrawMode.NoRepeat;
    [ObservableProperty] private int _halfRepeat = 1;
    [ObservableProperty] private DrawType _drawType = DrawType.Fair;
    [ObservableProperty] private string _algorithmId = "builtin.fair";
    [ObservableProperty] private string _defaultClass = string.Empty;
    [ObservableProperty] private int _disableAfterClick = 1;

    /// <summary>
    ///     The history record group Quick Draw uses by default (an empty string means "use global history").
    /// </summary>
    /// <remarks>
    ///     The group name follows the <see cref="SecRandom.Core.Enums.HistoryGroupKind.RollCall" /> purpose and shares
    ///     the same group set as the roll-call page. Quick Draw has no group selector of its own, so this setting is
    ///     its default: while a group is picked here, its weights only read that group's records and its new records
    ///     are written into it. The group is re-validated on every draw, because a saved name may already have been
    ///     renamed or deleted.
    /// </remarks>
    [ObservableProperty] private string _historyGroup = string.Empty;
}
