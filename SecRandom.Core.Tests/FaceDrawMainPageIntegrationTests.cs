namespace SecRandom.Core.Tests;

public class FaceDrawMainPageIntegrationTests
{
    [Fact]
    public void CameraPreviewPage_IsRegisteredAsTheFaceDrawMainPage()
    {
        var repositoryRoot = FindRepositoryRoot();
        var pagePath = Path.Combine(repositoryRoot, "SecRandom", "Views", "MainPages", "CameraPreviewTestPage.axaml.cs");
        var appPath = Path.Combine(repositoryRoot, "SecRandom", "App.axaml.cs");
        var pageSource = File.ReadAllText(pagePath);
        var appSource = File.ReadAllText(appPath);

        Assert.Contains(
            "[PageInfo(\"main.faceDraw\", FluentIcons.VideoPersonSparkleFilled, location: PageLocation.Bottom, useFullWidth: true, hidePageTitle: true)]",
            pageSource);
        Assert.Contains("services.AddMainPage<CameraPreviewTestPage>(\"人脸抽取\");", appSource);
        Assert.DoesNotContain("#if DEBUG\r\n                services.AddMainPage<CameraPreviewTestPage>", appSource);
        Assert.DoesNotContain("#if DEBUG\n                services.AddMainPage<CameraPreviewTestPage>", appSource);
    }

    [Fact]
    public void FaceDrawMainPage_UsesPersonCameraIconBelowLottery()
    {
        var repositoryRoot = FindRepositoryRoot();
        var pagePath = Path.Combine(repositoryRoot, "SecRandom", "Views", "MainPages", "CameraPreviewTestPage.axaml.cs");
        var appPath = Path.Combine(repositoryRoot, "SecRandom", "App.axaml.cs");
        var pageSource = File.ReadAllText(pagePath);
        var appSource = File.ReadAllText(appPath);

        Assert.Contains(
            "[PageInfo(\"main.faceDraw\", FluentIcons.VideoPersonSparkleFilled, location: PageLocation.Bottom, useFullWidth: true, hidePageTitle: true)]",
            pageSource);
        Assert.True(
            appSource.IndexOf("services.AddMainPage<LotteryPage>", StringComparison.Ordinal) <
            appSource.IndexOf("services.AddMainPage<CameraPreviewTestPage>", StringComparison.Ordinal));
    }

    [Fact]
    public void FaceDrawMainPage_UsesReleaseControlsWithoutEmbeddingSettings()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xamlPath = Path.Combine(repositoryRoot, "SecRandom", "Views", "MainPages", "CameraPreviewTestPage.axaml");
        var pagePath = Path.Combine(repositoryRoot, "SecRandom", "Views", "MainPages", "CameraPreviewTestPage.axaml.cs");
        var appPath = Path.Combine(repositoryRoot, "SecRandom", "App.axaml.cs");
        var xamlSource = File.ReadAllText(xamlPath);
        var pageSource = File.ReadAllText(pagePath);
        var appSource = File.ReadAllText(appPath);

        Assert.DoesNotContain("摄像头检测测试", xamlSource);
        Assert.DoesNotContain("CameraComboBox", xamlSource);
        Assert.DoesNotContain("ResolutionComboBox", xamlSource);
        Assert.DoesNotContain("DetectorModeComboBox", xamlSource);
        Assert.DoesNotContain("PickingSecondsBox", xamlSource);
        Assert.DoesNotContain("CameraComboBox", pageSource);
        Assert.DoesNotContain("ResolutionComboBox", pageSource);
        Assert.DoesNotContain("DetectorModeComboBox", pageSource);
        Assert.DoesNotContain("PickingSecondsBox", pageSource);
        Assert.DoesNotContain("new CameraDrawEngine", pageSource);
        Assert.DoesNotContain("DetectorType =", pageSource);
        Assert.Contains("IAppHost.GetService<CameraDrawEngine>()", pageSource);
        Assert.Contains("services.AddSingleton<CameraDrawEngine>();", appSource);
        Assert.Contains("CameraPreviewMode.Recognize", pageSource);
        Assert.Contains("StartPickButton_OnClick", pageSource);
    }

    [Fact]
    public void FaceDrawMainPage_FollowsV2PreviewAndPickingFlow()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(repositoryRoot, "SecRandom", "Views", "MainPages", "CameraPreviewTestPage.axaml"));
        var codeBehind = File.ReadAllText(Path.Combine(repositoryRoot, "SecRandom", "Views", "MainPages", "CameraPreviewTestPage.axaml.cs"));

        Assert.Contains("x:Name=\"RecognizedCountPanel\"", xaml);
        Assert.Contains("x:Name=\"PickCountPanel\"", xaml);
        Assert.Contains("x:Name=\"StartPickButton\"", xaml);
        Assert.DoesNotContain("CameraComboBox", xaml);
        Assert.DoesNotContain("ResolutionComboBox", xaml);
        Assert.DoesNotContain("DetectorModeComboBox", xaml);
        Assert.Contains("StartPreviewAsync", codeBehind);
        Assert.Contains("CameraPreviewMode.Recognize", codeBehind);
        Assert.Contains("_pickingTimer", codeBehind);
        Assert.Contains("RandomNumberGenerator.GetInt32", codeBehind);
    }

    [Fact]
    public void FaceDrawMainPage_AppliesModeAndFreezesSelectedFrameAtRuntime()
    {
        var repositoryRoot = FindRepositoryRoot();
        var codeBehind = File.ReadAllText(Path.Combine(repositoryRoot, "SecRandom", "Views", "MainPages", "CameraPreviewTestPage.axaml.cs"));

        Assert.Contains("FaceDetectorSettingsOnPropertyChanged", codeBehind);
        Assert.Contains("nameof(FaceDetectorSettingsConfig.CameraPreviewMode)", codeBehind);
        Assert.Contains("SetPickerFrameColor", codeBehind);
        Assert.Contains("CopyCurrentBitmap", codeBehind);
        Assert.Contains("_resultBitmap", codeBehind);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SecRandom.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate SecRandom.sln.");
    }
}
