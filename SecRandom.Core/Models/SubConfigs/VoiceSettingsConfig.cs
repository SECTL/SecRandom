using CommunityToolkit.Mvvm.ComponentModel;

namespace SecRandom.Core.Models.SubConfigs;

public partial class VoiceSettingsConfig : ObservableObject
{
    [ObservableProperty] private bool _voiceEnable = false;
    [ObservableProperty] private int _voiceEngine = 0;
    [ObservableProperty] private string _systemTtsVoiceName = string.Empty;
    [ObservableProperty] private string _edgeTtsVoiceName = "zh-CN-XiaoxiaoNeural";
    [ObservableProperty] private int _volumeSize = 80;
    [ObservableProperty] private int _speechRate = 100;
    [ObservableProperty] private bool _systemVolumeControl = false;
    [ObservableProperty] private int _systemVolumeSize = 50;
    [ObservableProperty] private bool _voiceWaitComplete = true;
    [ObservableProperty] private bool _specificAnnouncementsEnabled = true;
    [ObservableProperty] private bool _announceId = true;
    [ObservableProperty] private bool _announceName = true;
    [ObservableProperty] private string _announcementPrefix = string.Empty;
    [ObservableProperty] private string _announcementSuffix = string.Empty;
}
