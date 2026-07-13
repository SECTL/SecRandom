using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums.Configs;

namespace SecRandom.Core.Models.SubConfigs;

public partial class FloatingWindowSettingsConfig : ObservableObject
{
    [ObservableProperty] private bool _startupDisplayFloatingWindow = true;
    [ObservableProperty] private int _floatingWindowOpacity = 80;
    [ObservableProperty] private TopmostMode _floatingWindowTopmostMode = TopmostMode.Topmost;
    [ObservableProperty] private bool _showRollCallButton = true;
    [ObservableProperty] private bool _showQuickDrawButton = true;
    [ObservableProperty] private bool _showLotteryButton = false;
    [ObservableProperty] private int _floatingWindowPlacement = 1;
    [ObservableProperty] private int _floatingWindowDisplayStyle = 0;
    [ObservableProperty] private int _floatingWindowTheme = 0;
    [ObservableProperty] private bool _stickToEdge = true;
    [ObservableProperty] private int _stickToEdgeRecoverSeconds = 3;
    [ObservableProperty] private int _stickToEdgeDisplayStyle = 1;
    [ObservableProperty] private int _dockedWindowSize = 32;
    [ObservableProperty] private bool _draggable = true;
    [ObservableProperty] private int _floatingWindowSize = 56;
    [ObservableProperty] private int _longPressDuration = 500;
    [ObservableProperty] private bool _doNotStealFocus = true;
    [ObservableProperty] private bool _hideOnForeground = false;
    [ObservableProperty] private string _hideOnForegroundWindowTitles = string.Empty;
    [ObservableProperty] private string _hideOnForegroundProcessNames = string.Empty;
}
