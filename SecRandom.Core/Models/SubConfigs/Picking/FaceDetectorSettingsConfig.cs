using System.Text.Json.Serialization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums.Configs;
using SecRandom.Shared.ComponentModels;

namespace SecRandom.Core.Models.SubConfigs.Picking;

public partial class FaceDetectorSettingsConfig : ObservableObject, IJsonOnDeserialized
{
    [ObservableProperty] private string _cameraSource = string.Empty;
    [ObservableProperty] private ObservableDictionary<string, string> _cameraDisplayResolutionMap = [];
    [ObservableProperty] private CameraPreviewMode _cameraPreviewMode = CameraPreviewMode.Pick;
    [ObservableProperty] private int _pickingDurationSeconds = 3;
    [ObservableProperty] private bool _playProcessAudio = true;
    [ObservableProperty] private bool _playResultAudio = true;
    [ObservableProperty] private Color _pickerFrameColor = Color.Parse(GlobalConstants.DefaultThemeColor);

    [ObservableProperty]
    [property: JsonIgnore]
    private FaceDetectorMode _detectorMode = FaceDetectorMode.Lightweight;

    [ObservableProperty]
    [property: JsonIgnore]
    private string _detectorType = FaceDetectorModeResolver.YuNetModelFileName;
    [ObservableProperty] private int _modelInputWidth = 640;
    [ObservableProperty] private int _modelInputHeight = 480;

    [JsonIgnore] internal bool DetectorTypeWasNormalized { get; private set; }

    [JsonPropertyName("detector_mode")]
    public FaceDetectorMode? SerializedDetectorMode
    {
        get => DetectorMode;
        set => _deserializedDetectorMode = value;
    }

    [JsonPropertyName("detector_type")]
    public string SerializedDetectorType
    {
        get => DetectorType;
        set
        {
            _deserializedDetectorType = value;
            _hasDeserializedDetectorType = true;
        }
    }

    private FaceDetectorMode? _deserializedDetectorMode;
    private string? _deserializedDetectorType;
    private bool _hasDeserializedDetectorType;

    void IJsonOnDeserialized.OnDeserialized()
    {
        if (_deserializedDetectorMode is { } detectorMode)
        {
            var normalizedDetectorMode = Enum.IsDefined(detectorMode)
                ? detectorMode
                : FaceDetectorMode.Lightweight;
            DetectorMode = normalizedDetectorMode;
            DetectorTypeWasNormalized = normalizedDetectorMode != detectorMode || !_hasDeserializedDetectorType || !string.Equals(
                _deserializedDetectorType,
                DetectorType,
                StringComparison.Ordinal);
            return;
        }

        if (_hasDeserializedDetectorType)
        {
            DetectorTypeWasNormalized = true;
            DetectorType = _deserializedDetectorType!;
        }
    }

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
