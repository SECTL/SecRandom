using CommunityToolkit.Mvvm.ComponentModel;

namespace SecRandom.Core.Models.SubConfigs.General;

/// <summary>
///     简洁模式。开启后只保留点名相关的界面入口，其余功能的设置页不再显示，
///     这些功能在运行时改用默认配置，但用户已经存进 <c>settings.json</c> 的设置原样保留。
/// </summary>
public partial class SimpleModeSettingsConfig : ObservableObject
{
    [ObservableProperty] private bool _enabled;
}
