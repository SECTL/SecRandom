using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SecRandom.Core.Models.SubConfigs;

public partial class ThemeManagementSettingsConfig : ObservableObject
{
    [ObservableProperty] private string _rollCallThemeId = string.Empty;
    [ObservableProperty] private string _lotteryThemeId = string.Empty;
    [ObservableProperty] private string _rollCallThemeType = string.Empty;
    [ObservableProperty] private string _lotteryThemeType = string.Empty;
    [ObservableProperty] private int _mainWindowBackgroundMode = 0;
    [ObservableProperty] private Color _mainWindowBackgroundColor = Color.Parse("#ffd9d2");
    [ObservableProperty] private Color _mainWindowGradientStart = Color.Parse("#ffd9d2");
    [ObservableProperty] private Color _mainWindowGradientEnd = Color.Parse("#d2fffb");
    [ObservableProperty] private int _mainWindowGradientDirection = 0;
    [ObservableProperty] private bool _mainWindowGradientDirectionV2 = true;
    [ObservableProperty] private string _mainWindowBackgroundImage = string.Empty;
    [ObservableProperty] private int _mainWindowBackgroundBrightness = 10;
    [ObservableProperty] private bool _mainWindowBackgroundBlurEnable = false;
    [ObservableProperty] private int _mainWindowBackgroundBlurRadius = 15;
    [ObservableProperty] private int _settingsWindowBackgroundMode = 0;
    [ObservableProperty] private Color _settingsWindowBackgroundColor = Color.Parse("#ffd9d2");
    [ObservableProperty] private Color _settingsWindowGradientStart = Color.Parse("#ffd9d2");
    [ObservableProperty] private Color _settingsWindowGradientEnd = Color.Parse("#d2fffb");
    [ObservableProperty] private int _settingsWindowGradientDirection = 0;
    [ObservableProperty] private bool _settingsWindowGradientDirectionV2 = true;
    [ObservableProperty] private string _settingsWindowBackgroundImage = string.Empty;
    [ObservableProperty] private int _settingsWindowBackgroundBrightness = 10;
    [ObservableProperty] private bool _settingsWindowBackgroundBlurEnable = false;
    [ObservableProperty] private int _settingsWindowBackgroundBlurRadius = 15;
    [ObservableProperty] private int _notificationFloatingWindowBackgroundMode = 0;
    [ObservableProperty] private Color _notificationFloatingWindowBackgroundColor = Color.Parse("#ffd9d2");
    [ObservableProperty] private Color _notificationFloatingWindowGradientStart = Color.Parse("#ffd9d2");
    [ObservableProperty] private Color _notificationFloatingWindowGradientEnd = Color.Parse("#d2fffb");
    [ObservableProperty] private int _notificationFloatingWindowGradientDirection = 0;
    [ObservableProperty] private bool _notificationFloatingWindowGradientDirectionV2 = true;
    [ObservableProperty] private string _notificationFloatingWindowBackgroundImage = string.Empty;
    [ObservableProperty] private int _notificationFloatingWindowBackgroundBrightness = 10;
    [ObservableProperty] private bool _notificationFloatingWindowBackgroundBlurEnable = false;
    [ObservableProperty] private int _notificationFloatingWindowBackgroundBlurRadius = 15;
}
