using CommunityToolkit.Mvvm.ComponentModel;

namespace SecRandom.Core.Models.SubConfigs;

public partial class SecuritySettingsConfig : ObservableObject
{
    [ObservableProperty] private bool _securityEnabled;
    [ObservableProperty] private bool _passwordEnabled = false;
    [ObservableProperty] private bool _totpEnabled = false;
    [ObservableProperty] private bool _usbBindingEnabled = false;
    [ObservableProperty] private bool _requireAllSelectedFactors;
    [ObservableProperty] private bool _allowSettingsPreview;

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

    /// <summary>
    ///     Sudo 模式总开关。关闭后每一次受保护操作都要重新验证。
    /// </summary>
    [ObservableProperty] private bool _sudoModeEnabled = true;

    [ObservableProperty] private int _sudoModeDurationSeconds = 20;

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
