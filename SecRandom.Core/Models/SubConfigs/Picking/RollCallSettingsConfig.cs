using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums.Configs;

namespace SecRandom.Core.Models.SubConfigs.Picking;

public partial class RollCallSettingsConfig : OverridableDrawSettings
{
    [ObservableProperty] private DrawMode _drawMode = DrawMode.NoRepeat;
    [ObservableProperty] private ClearRecordMode _clearRecord = ClearRecordMode.Restarted;
    [ObservableProperty] private int _halfRepeat = 1;
    [ObservableProperty] private DrawType _drawType = DrawType.Fair;
    [ObservableProperty] private string _algorithmId = "builtin.fair";
    [ObservableProperty] private string _defaultClass = string.Empty;

    /// <summary>
    ///     点名页选择器的初始记录组（空串 = 默认记录组，即只算未分组的记录）。
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         这只是「初始值」，不是锁定：页面打开时选择器先落到这里，用户当场换组仍然是纯界面
    ///         状态，不会写回配置。把组固定在这里的目的是让「这台机器上的点名默认按哪个组的记录
    ///         算权重」在重启后不再退回默认记录组。
    ///     </para>
    ///     <para>
    ///         组名按 <see cref="SecRandom.Core.Enums.HistoryGroupKind.RollCall" /> 登记，与闪抽共用
    ///         同一批组。每次抽取前都会重新校验：组已被删除或改名时回落到默认记录组（删除组会把
    ///         记录移回默认组），绝不拿一个不存在的组名去读权重。
    ///     </para>
    /// </remarks>
    [ObservableProperty] private string _historyGroup = string.Empty;
}
