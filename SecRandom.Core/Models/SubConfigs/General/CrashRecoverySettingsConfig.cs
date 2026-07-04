using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums.Configs;

namespace SecRandom.Core.Models.SubConfigs.General;

public partial class CrashRecoverySettingsConfig : ObservableObject
{
    [ObservableProperty] private CrashRecoveryMode _mode = CrashRecoveryMode.PromptAndRestart;
}
