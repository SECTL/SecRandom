using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums.Configs;

namespace SecRandom.Core.Models.SubConfigs.General;

public partial class CrashRecoverySettingsConfig : ObservableObject
{
    [ObservableProperty] private CrashRecoveryMode _mode = CrashRecoveryMode.PromptAndRestart;

    /// <summary>
    ///     When a crash stack belongs to a plugin load context, the plugin is disabled for the next
    ///     startup so a recurring plugin crash is contained without affecting other plugins.
    /// </summary>
    [ObservableProperty] private bool _disableCrashedPlugin = true;
}
