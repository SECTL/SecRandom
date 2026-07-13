using System.Text.Json.Serialization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums.Configs;
using SecRandom.Shared.ComponentModels;

namespace SecRandom.Core.Models.SubConfigs.Picking;

public partial class FaceDetectorSettingsConfig : ObservableObject
{
    [ObservableProperty] private string _cameraSource = string.Empty;
    [ObservableProperty] private ObservableDictionary<string, string> _cameraDisplayResolutionMap = [];
    [ObservableProperty] private CameraPreviewMode _cameraPreviewMode = CameraPreviewMode.Pick;
    [ObservableProperty] private int _pickingDurationSeconds = 3;
    [ObservableProperty] private bool _playProcessAudio = true;
    [ObservableProperty] private bool _playResultAudio = true;
    [ObservableProperty] private Color _pickerFrameColor = Color.Parse(GlobalConstants.DefaultThemeColor);

    [ObservableProperty] private FaceDetectorMode _detectorMode = FaceDetectorMode.Lightweight;
    [ObservableProperty] private string _detectorType = FaceDetectorModeResolver.YuNetModelFileName;
    [ObservableProperty] private int _modelInputWidth = 640;
    [ObservableProperty] private int _modelInputHeight = 480;

    [JsonIgnore] internal bool DetectorTypeWasNormalized { get; private set; }

    partial void OnDetectorModeChanged(FaceDetectorMode value)
    {
        DetectorType = FaceDetectorModeResolver.GetModelFileName(value);
    }

    partial void OnDetectorTypeChanged(string value)
    {
        DetectorMode = FaceDetectorModeResolver.ResolveMode(value);
        var canonicalModelName = FaceDetectorModeResolver.GetModelFileName(DetectorMode);
        DetectorTypeWasNormalized |= !string.Equals(value, canonicalModelName, StringComparison.Ordinal);
        DetectorType = canonicalModelName;
    }
}
