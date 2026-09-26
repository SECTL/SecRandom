using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums.Configs;

namespace SecRandom.Core.Models.SubConfigs;

public partial class SecuritySettingsConfig : ObservableObject
{
    [ObservableProperty] private bool _securityEnabled;
    [ObservableProperty] private bool _passwordEnabled = false;
    [ObservableProperty] private bool _totpEnabled = false;
    [ObservableProperty] private bool _usbBindingEnabled = false;
    [ObservableProperty] private bool _requireAllSelectedFactors;
    [ObservableProperty] private bool _allowSettingsPreview;

    /// <summary>
    ///     开启后由 <c>SettingsIntegrityService</c> 记录整份 settings.json 的指纹，
    ///     并在启动时校验该文件是否被程序之外的改动修改过。
    /// </summary>
    [ObservableProperty] private bool _settingsIntegrityCheckEnabled;

    /// <summary>
    ///     校验发现改动后要执行的操作；默认仍然是原有的「用安全密码确认后继续」。
    /// </summary>
    [ObservableProperty] private SettingsIntegrityAction _settingsIntegrityAction = SettingsIntegrityAction.Confirm;

    /// <summary>
    ///     自动恢复时依次尝试的备份来源，与处理方式分开配置，因为来源只影响自动恢复这一次操作。
    /// </summary>
    [ObservableProperty]
    private SettingsIntegrityRestoreSource _settingsIntegrityRestoreSource = SettingsIntegrityRestoreSource.Local;

    [ObservableProperty] private bool _protectOpenSettings;
    [ObservableProperty] private bool _protectToggleMainWindow;
    [ObservableProperty] private bool _protectToggleFloatingWindow;
    [ObservableProperty] private bool _protectRestart;
    [ObservableProperty] private bool _protectExit;
    [ObservableProperty] private bool _protectRollCallStart;
    [ObservableProperty] private bool _protectRollCallReset;
    [ObservableProperty] private bool _protectQuickDrawStart;
    [ObservableProperty] private bool _protectQuickDrawReset;
    [ObservableProperty] private bool _protectLotteryStart;
    [ObservableProperty] private bool _protectLotteryReset;
    [ObservableProperty] private bool _protectLinkage;

    [ObservableProperty] private int _sudoModeDurationSeconds = 60;

    // Compatibility bridges for the original placeholder fields.
    public bool VerifyBeforeSensitiveOperations
    {
        get => ProtectRollCallStart || ProtectQuickDrawStart || ProtectLotteryStart;
        set
        {
            ProtectRollCallStart = value;
            ProtectQuickDrawStart = value;
            ProtectLotteryStart = value;
        }
    }

    public bool VerifyBeforeLinkageOperations
    {
        get => ProtectLinkage;
        set => ProtectLinkage = value;
    }
}
