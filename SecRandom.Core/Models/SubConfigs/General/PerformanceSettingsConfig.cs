using CommunityToolkit.Mvvm.ComponentModel;

namespace SecRandom.Core.Models.SubConfigs.General;

/// <summary>
///     低配模式：为低端设备关闭入场动画与高开销的渲染选项，降低常驻 CPU/GPU 占用
/// </summary>
public partial class PerformanceSettingsConfig : ObservableObject
{
    [ObservableProperty] private bool _lowSpecMode;
}
