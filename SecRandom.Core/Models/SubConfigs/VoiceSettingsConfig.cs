using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums.Configs;

namespace SecRandom.Core.Models.SubConfigs;

public partial class VoiceSettingsConfig : ObservableObject
{
    public static string GetDefaultEdgeTtsVoiceName(LanguageMode language) => language switch
    {
        LanguageMode.English => "en-US-JennyNeural",
        LanguageMode.Japanese => "ja-JP-NanamiNeural",
        _ => "zh-CN-XiaoxiaoNeural"
    };

    [ObservableProperty] private bool _voiceEnable = false;
    [ObservableProperty] private int _voiceEngine = 1;
    [ObservableProperty] private string _systemTtsVoiceName = string.Empty;
    [ObservableProperty] private string _edgeTtsVoiceName = "zh-CN-XiaoxiaoNeural";
    [ObservableProperty] private int _volumeSize = 80;
    [ObservableProperty] private int _speechRate = 100;
    [ObservableProperty] private bool _systemVolumeControl = false;
    [ObservableProperty] private int _systemVolumeSize = 50;
    [ObservableProperty] private bool _voiceWaitComplete = true;
    [ObservableProperty] private bool _announceId = false;
    [ObservableProperty] private bool _announceName = true;
}
