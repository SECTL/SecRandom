using CommunityToolkit.Mvvm.ComponentModel;

namespace SecRandom.Core.Models.SubConfigs;

public partial class SecuritySettingsConfig : ObservableObject
{
    [ObservableProperty] private bool _passwordEnabled = false;
    [ObservableProperty] private bool _totpEnabled = false;
    [ObservableProperty] private bool _usbBindingEnabled = false;
    [ObservableProperty] private bool _verifyBeforeSensitiveOperations = true;
    [ObservableProperty] private bool _verifyBeforeLinkageOperations = true;
}
