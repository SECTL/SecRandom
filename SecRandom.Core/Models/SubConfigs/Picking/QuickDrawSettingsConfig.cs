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
    ///     Whether the take-over window shows the "drawing" status line while a draw is running.
    /// </summary>
    /// <remarks>
    ///     The animation itself already tells the class that a draw is in progress, so a text line on top of it
    ///     costs a re-layout in the middle of a lesson. Turning it off keeps the overlay steady from the moment
    ///     the button is pressed until the result lands; every other status (no members, draw failed, result
    ///     count) stays as it is, because those carry information the animation cannot.
    /// </remarks>
    [ObservableProperty] private bool _showDrawingStatus = true;

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
