namespace SecRandom.Core.Tests;

public class FaceDetectorSettingsPageTests
{
    [Fact]
    public void FaceDetectorSettingsPage_UsesDeviceAwareCameraControls()
    {
        var xaml = ReadSettingsPageXaml();
        var codeBehind = ReadSettingsPageCodeBehind();

        Assert.Contains("x:Name=\"CameraComboBox\"", xaml);
        Assert.Contains("x:Name=\"ResolutionComboBox\"", xaml);
        Assert.Contains("Settings.DetectorMode", xaml);
        Assert.Contains("CameraDrawEngine.GetAllCameraDevices()", codeBehind);
        Assert.Contains("CameraDisplayResolutionMap", codeBehind);
        Assert.Contains("_isLoading", codeBehind);
        Assert.Contains("public override string ToString() => Name", codeBehind);
        Assert.Contains("{Width} x {Height}", codeBehind);
    }

    [Fact]
    public void FaceDetectorSettingsPage_UsesLocalizedCameraDetectionAndPickingGroups()
    {
        var xaml = ReadSettingsPageXaml();
        var resources = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "SecRandom", "Langs", "Common", "Resources.resx"));
        var designer = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "SecRandom", "Langs", "Common", "Resources.Designer.cs"));

        foreach (var key in new[]
                 {
                     "Settings_FaceDetector_CameraGroup",
                     "Settings_FaceDetector_DetectionGroup",
                     "Settings_FaceDetector_PickingGroup"
                 })
        {
            Assert.Contains($"lc:Resources.{key}", xaml);
            Assert.Contains($"name=\"{key}\"", resources);
            Assert.Contains($"string {key}", designer);
        }
    }

    [Fact]
    public void FaceDetectorSettingsPage_RemovesRawCameraAndModelInputs()
    {
        var xaml = ReadSettingsPageXaml();

        Assert.DoesNotContain("<TextBox Text=\"{Binding Settings.CameraSource}\"", xaml);
        Assert.DoesNotContain("Settings.ModelInputWidth", xaml);
        Assert.DoesNotContain("Settings.ModelInputHeight", xaml);
        Assert.DoesNotContain("<sr:IconText", xaml);
    }

    [Fact]
    public void FaceDetectorSettingsPage_SavesAfterMutatingDisplayResolutionMap()
    {
        var codeBehind = ReadSettingsPageCodeBehind();
        const string mutation = "Settings.CameraDisplayResolutionMap[camera.Source] = $\"{resolution.Width}x{resolution.Height}\";";

        var mutationIndex = codeBehind.IndexOf(mutation, StringComparison.Ordinal);

        Assert.True(mutationIndex >= 0, "Expected a camera display resolution map mutation.");
        Assert.True(codeBehind.IndexOf("ConfigHandler.Save();", mutationIndex, StringComparison.Ordinal) > mutationIndex,
            "Expected the resolution map mutation to be followed by a config save.");
    }

    private static string ReadSettingsPageXaml()
    {
        return File.ReadAllText(Path.Combine(FindRepositoryRoot(), "SecRandom", "Views", "SettingsPages", "Picking",
            "FaceDetectorSettingsPage.axaml"));
    }

    private static string ReadSettingsPageCodeBehind()
    {
        return File.ReadAllText(Path.Combine(FindRepositoryRoot(), "SecRandom", "Views", "SettingsPages", "Picking",
            "FaceDetectorSettingsPage.axaml.cs"));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SecRandom.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate SecRandom.sln.");
    }
}
