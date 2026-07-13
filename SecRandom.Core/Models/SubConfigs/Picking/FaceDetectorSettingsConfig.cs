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

    private FaceDetectorMode _detectorMode = FaceDetectorMode.Lightweight;
    private string _detectorType = FaceDetectorModeResolver.YuNetModelFileName;
    private bool _hasDeclaredDetectorMode;
    [ObservableProperty] private int _modelInputWidth = 640;
    [ObservableProperty] private int _modelInputHeight = 480;

    [JsonIgnore] internal bool DetectorTypeWasNormalized { get; private set; }

    public FaceDetectorMode DetectorMode
    {
        get => _detectorMode;
        set
        {
            _hasDeclaredDetectorMode = true;
            var mode = value == FaceDetectorMode.Enhanced
                ? FaceDetectorMode.Enhanced
                : FaceDetectorMode.Lightweight;
            var canonicalModelName = FaceDetectorModeResolver.GetModelFileName(mode);
            DetectorTypeWasNormalized |= !string.Equals(_detectorType, canonicalModelName, StringComparison.Ordinal);
            SetProperty(ref _detectorMode, mode);
            SetProperty(ref _detectorType, canonicalModelName);
        }
    }

    public string DetectorType
    {
        get => _detectorType;
        set
        {
            if (_hasDeclaredDetectorMode)
            {
                var declaredModelName = FaceDetectorModeResolver.GetModelFileName(_detectorMode);
                DetectorTypeWasNormalized |= !string.Equals(value, declaredModelName, StringComparison.Ordinal);
                SetProperty(ref _detectorType, declaredModelName);
                return;
            }

            var mode = FaceDetectorModeResolver.ResolveMode(value);
            var canonicalModelName = FaceDetectorModeResolver.GetModelFileName(mode);
            SetProperty(ref _detectorMode, mode);
            SetProperty(ref _detectorType, canonicalModelName);
            DetectorTypeWasNormalized |= !string.Equals(value, canonicalModelName, StringComparison.Ordinal);
        }
    }
}
