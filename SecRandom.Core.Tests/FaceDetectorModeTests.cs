using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models;
using SecRandom.Core.Models.SubConfigs.Picking;
using SecRandom.Core.Services.Config;

namespace SecRandom.Core.Tests;

public class FaceDetectorModeTests
{
    [Fact]
    public void FaceDetectorSettingsPage_UsesModeSelectorInsteadOfRawModelFileName()
    {
        var xamlPath = Path.Combine(FindRepositoryRoot(), "SecRandom", "Views", "SettingsPages", "Picking",
            "FaceDetectorSettingsPage.axaml");
        var xaml = File.ReadAllText(xamlPath);

        Assert.Contains("SelectedIndex=\"{Binding Settings.DetectorMode}\"", xaml);
        Assert.DoesNotContain("<TextBox Text=\"{Binding Settings.DetectorType}\"", xaml);
    }

    [Fact]
    public void CameraPreviewTestPage_LeavesDetectorModeConfigurationToSettings()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xamlPath = Path.Combine(repositoryRoot, "SecRandom", "Views", "MainPages", "CameraPreviewTestPage.axaml");
        var codeBehindPath = xamlPath + ".cs";
        var xaml = File.ReadAllText(xamlPath);
        var codeBehind = File.ReadAllText(codeBehindPath);

        Assert.DoesNotContain("x:Name=\"DetectorModeComboBox\"", xaml);
        Assert.DoesNotContain("FaceDetectorMode.Lightweight", codeBehind);
        Assert.DoesNotContain("FaceDetectorMode.Enhanced", codeBehind);
        Assert.DoesNotContain("DetectorMode = mode", codeBehind);
        Assert.Contains("CameraPreviewMode.Recognize", codeBehind);
        Assert.Contains("StartPickButton_OnClick", codeBehind);
        Assert.DoesNotContain("ModelComboBox", xaml);
        Assert.DoesNotContain("DetectorType =", codeBehind);
        Assert.DoesNotContain("ListDetectorModels", codeBehind);
        Assert.DoesNotContain("*.onnx", codeBehind);
    }

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
    public void MainConfigHandler_ImmediatelyPersistsCanonicalDetectorSettingsFromLegacyConfig()
    {
        var configService = new InMemoryConfigService(
            """
            {
              "face_detector_settings": {
                "detector_type": "version-RFB-640.onnx"
              }
            }
            """);

        var handler = new MainConfigHandler(NullLogger<MainConfigHandler>.Instance, configService);

        Assert.Equal(FaceDetectorMode.Lightweight, handler.Data.FaceDetectorSettings.DetectorMode);
        Assert.Equal(FaceDetectorModeResolver.YuNetModelFileName, handler.Data.FaceDetectorSettings.DetectorType);
        Assert.NotNull(configService.LastSavedJson);
        using var savedDocument = JsonDocument.Parse(configService.LastSavedJson);
        Assert.Equal(FaceDetectorModeResolver.YuNetModelFileName,
            savedDocument.RootElement.GetProperty("face_detector_settings").GetProperty("detector_type").GetString());
        var savedConfig = JsonSerializer.Deserialize<MainConfigModel>(configService.LastSavedJson, ConfigServiceBase.JsonOptions);
        Assert.NotNull(savedConfig);
        Assert.Equal(FaceDetectorMode.Lightweight, savedConfig.FaceDetectorSettings.DetectorMode);
    }

    [Fact]
    public void MainConfigHandler_PreservesDeclaredDetectorModeWhenLegacyTypeConflictsRegardlessOfJsonOrder()
    {
        var settingsJson = new[]
        {
            "\"detector_mode\": 1, \"detector_type\": \"face_detection_yunet_2023mar_int8bq.onnx\"",
            "\"detector_type\": \"face_detection_yunet_2023mar_int8bq.onnx\", \"detector_mode\": 1"
        };

        foreach (var settings in settingsJson)
        {
            var configService = new InMemoryConfigService($$"""
                                                        {
                                                          "face_detector_settings": { {{settings}} }
                                                        }
                                                        """);

            var handler = new MainConfigHandler(NullLogger<MainConfigHandler>.Instance, configService);

            Assert.Equal(FaceDetectorMode.Enhanced, handler.Data.FaceDetectorSettings.DetectorMode);
            Assert.Equal(FaceDetectorModeResolver.DamoyoloModelFileName, handler.Data.FaceDetectorSettings.DetectorType);
            Assert.NotNull(configService.LastSavedJson);
            using var savedDocument = JsonDocument.Parse(configService.LastSavedJson);
            Assert.Equal(FaceDetectorModeResolver.DamoyoloModelFileName,
                savedDocument.RootElement.GetProperty("face_detector_settings").GetProperty("detector_type").GetString());
        }
    }

    [Fact]
    public void MainConfigModel_UsesLightweightModeForInvalidNumericDetectorMode()
    {
        const string json = """
                            {
                              "face_detector_settings": {
                                "detector_mode": 99
                              }
                            }
                            """;

        var config = JsonSerializer.Deserialize<MainConfigModel>(json, ConfigServiceBase.JsonOptions);

        Assert.NotNull(config);
        Assert.Equal(FaceDetectorMode.Lightweight, config.FaceDetectorSettings.DetectorMode);
        Assert.Equal(FaceDetectorModeResolver.YuNetModelFileName, config.FaceDetectorSettings.DetectorType);
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

    private sealed class InMemoryConfigService(string json) : ConfigServiceBase
    {
        public string? LastSavedJson { get; private set; }

        public override bool IsConfigExists<T>(T fallback) => true;

        public override T LoadConfig<T>(T fallback)
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? fallback;
        }

        public override void SaveConfig<T>(T config)
        {
            LastSavedJson = JsonSerializer.Serialize(config, JsonOptions);
        }

        public override void DeleteConfig<T>(T config)
        {
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SecRandom.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate SecRandom.sln.");
    }
}
