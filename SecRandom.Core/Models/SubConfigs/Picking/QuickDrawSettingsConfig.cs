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
    ///     闪抽默认锁定的历史记录组（空串 = 默认记录组，即只算未分组的记录）。
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         组名跟随 <see cref="SecRandom.Core.Enums.HistoryGroupKind.RollCall" /> 用途，与点名页共用
    ///         同一批组。闪抽没有自己的选择器，所以这项配置就是它的默认值：锁在某个组时权重只读该组
    ///         记录，新记录也写进该组。
    ///     </para>
    ///     <para>
    ///         空串不是「不分组」而是「默认记录组」：功能关闭时才真正不分组。每次抽取前都会重新校验，
    ///         组已被删除或改名时回落到默认记录组（删除组会把记录移回默认组）。
    ///     </para>
    /// </remarks>
    [ObservableProperty] private string _historyGroup = string.Empty;
}
