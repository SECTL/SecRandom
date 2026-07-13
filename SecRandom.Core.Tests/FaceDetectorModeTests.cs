using System.Text.Json;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models;
using SecRandom.Core.Models.SubConfigs.Picking;
using SecRandom.Core.Services.Config;

namespace SecRandom.Core.Tests;

public class FaceDetectorModeTests
{
    [Fact]
    public void FaceDetectorSettingsConfig_UsesLightweightYuNetByDefault()
    {
        FaceDetectorSettingsConfig settings = new();

        Assert.Equal(FaceDetectorMode.Lightweight, settings.DetectorMode);
        Assert.Equal(FaceDetectorModeResolver.YuNetModelFileName, settings.DetectorType);
    }

    [Theory]
    [InlineData(FaceDetectorMode.Lightweight, FaceDetectorModeResolver.YuNetModelFileName)]
    [InlineData(FaceDetectorMode.Enhanced, FaceDetectorModeResolver.DamoyoloModelFileName)]
    public void FaceDetectorSettingsConfig_ModeUsesCanonicalModelFileName(
        FaceDetectorMode mode,
        string expectedModelFileName)
    {
        FaceDetectorSettingsConfig settings = new();

        settings.DetectorMode = mode;

        Assert.Equal(expectedModelFileName, settings.DetectorType);
    }

    [Theory]
    [InlineData("face_detection_yunet_2023mar_int8bq.onnx", FaceDetectorMode.Lightweight)]
    [InlineData("damoyolo_end2end_simplified.onnx", FaceDetectorMode.Enhanced)]
    [InlineData("version-RFB-640.onnx", FaceDetectorMode.Lightweight)]
    [InlineData("other-yolo-model.onnx", FaceDetectorMode.Lightweight)]
    public void MainConfigModel_MigratesLegacyDetectorFileNameToCanonicalMode(
        string legacyModelFileName,
        FaceDetectorMode expectedMode)
    {
        var json = $$"""
                     {
                       "face_detector_settings": {
                         "detector_type": "{{legacyModelFileName}}"
                       }
                     }
                     """;

        var config = JsonSerializer.Deserialize<MainConfigModel>(json, ConfigServiceBase.JsonOptions);

        Assert.NotNull(config);
        Assert.Equal(expectedMode, config.FaceDetectorSettings.DetectorMode);
        Assert.Equal(FaceDetectorModeResolver.GetModelFileName(expectedMode), config.FaceDetectorSettings.DetectorType);
    }

    [Fact]
    public void FaceDetectorModeResolver_UsesLongerIntervalForEnhancedMode()
    {
        var lightweightInterval = FaceDetectorModeResolver.GetInferenceInterval(FaceDetectorMode.Lightweight);
        var enhancedInterval = FaceDetectorModeResolver.GetInferenceInterval(FaceDetectorMode.Enhanced);

        Assert.Equal(TimeSpan.FromMilliseconds(100), lightweightInterval);
        Assert.Equal(TimeSpan.FromMilliseconds(250), enhancedInterval);
        Assert.True(enhancedInterval > lightweightInterval);
    }
}
