using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SecRandom.Core.Models.SubConfigs.General;

public partial class GeneralSettingsConfig : ObservableObject
{
    [ObservableProperty] private BasicSettingsConfig _basic = new();
    [ObservableProperty] private BackupConfig _backup = new();
    [ObservableProperty] private PrivacySettingsConfig _privacySettings = new();
    [ObservableProperty] private CrashRecoverySettingsConfig _crashRecovery = new();
    [ObservableProperty] private ProofRetentionConfig _proofRetention = new();

    public void ApplyLegacyBasic(BasicSettingsConfig? legacyBasic)
    {
        if (legacyBasic is null)
            return;

        Basic = legacyBasic;
        PrivacySettings.ApplyLegacyTelemetry(legacyBasic.LegacyTelemetryEnabled, legacyBasic.LegacyTelemetryMode);
    }

    public void ApplyLegacyBackup(BackupConfig? legacyBackup)
    {
        if (legacyBackup is null)
            return;

        Backup = legacyBackup;
    }
}
