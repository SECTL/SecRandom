using SecRandom.Mobile;
using SecRandom.Platforms.Abstractions;
using SecRandom.Services.RosterTransfer;

namespace SecRandom.Mobile.Tests;

/// <summary>
/// 移动端相机后端（CameraView 的 Android 实现）每次快门都会在应用外部目录写一张原图且不删除，
/// 这里验证回收策略只删后端自己的拍照文件，不碰应用数据与其它文件，并遵守宽限时间。
/// </summary>
public sealed class RosterQrCaptureFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(),
        $"secrandom-capture-{Guid.NewGuid():N}");

    public RosterQrCaptureFilesTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover temporary directory must never fail the test run.
        }
    }

    [Fact]
    public void ClearDeletesBackendCapturesAndReportsReclaimedBytes()
    {
        var first = WriteCapture("photo_20260830_101500.jpg", 1536);
        var second = WriteCapture("photo_20260830_101501.jpg", 1024);

        var reclaimed = RosterQrCaptureFiles.Clear(TimeSpan.Zero, _root);

        Assert.Equal(2560, reclaimed);
        Assert.False(File.Exists(first));
        Assert.False(File.Exists(second));
    }

    [Fact]
    public void ClearKeepsApplicationDataAndUnrelatedFiles()
    {
        var settings = Path.Combine(_root, "data", "config");
        Directory.CreateDirectory(settings);
        var settingsFile = Path.Combine(settings, "settings.json");
        File.WriteAllText(settingsFile, "{}");

        // 同一个目录里的其它文件、以及子目录中的同名文件都不属于后端拍照文件。
        var notes = Path.Combine(_root, "notes.txt");
        File.WriteAllText(notes, "keep");
        var photoWithoutStamp = Path.Combine(_root, "photo.jpg");
        File.WriteAllText(photoWithoutStamp, "keep");
        var photoWithForeignExtension = Path.Combine(_root, "photo_20260830_101500.jpgx");
        File.WriteAllText(photoWithForeignExtension, "keep");
        var nestedDirectory = Path.Combine(_root, "nested");
        Directory.CreateDirectory(nestedDirectory);
        var nestedCapture = Path.Combine(nestedDirectory, "photo_20260830_101500.jpg");
        File.WriteAllText(nestedCapture, "keep");

        var capture = WriteCapture("photo_20260830_101502.jpg", 512);

        var reclaimed = RosterQrCaptureFiles.Clear(TimeSpan.Zero, _root);

        Assert.Equal(512, reclaimed);
        Assert.False(File.Exists(capture));
        Assert.True(File.Exists(settingsFile));
        Assert.True(File.Exists(notes));
        Assert.True(File.Exists(photoWithoutStamp));
        Assert.True(File.Exists(photoWithForeignExtension));
        Assert.True(File.Exists(nestedCapture));
    }

    [Fact]
    public void ClearKeepsCapturesInsideTheGraceWindow()
    {
        var capture = WriteCapture("photo_20260830_101500.jpg", 700);

        var kept = RosterQrCaptureFiles.Clear(TimeSpan.FromHours(1), _root);

        Assert.Equal(0, kept);
        Assert.True(File.Exists(capture));

        var reclaimed = RosterQrCaptureFiles.Clear(TimeSpan.Zero, _root);

        Assert.Equal(700, reclaimed);
        Assert.False(File.Exists(capture));
    }

    [Fact]
    public void ClearSpansEverySuppliedDirectory()
    {
        var other = Path.Combine(Path.GetTempPath(), $"secrandom-capture-{Guid.NewGuid():N}");
        Directory.CreateDirectory(other);
        try
        {
            var first = WriteCapture("photo_20260830_101500.jpg", 100);
            var second = Path.Combine(other, "photo_20260830_101500.jpg");
            File.WriteAllBytes(second, new byte[200]);

            var reclaimed = RosterQrCaptureFiles.Clear(TimeSpan.Zero, _root, other);

            Assert.Equal(300, reclaimed);
            Assert.False(File.Exists(first));
            Assert.False(File.Exists(second));
        }
        finally
        {
            Directory.Delete(other, recursive: true);
        }
    }

    [Fact]
    public void ClearIgnoresBlankAndMissingDirectories()
    {
        var missing = Path.Combine(_root, "missing");

        var reclaimed = RosterQrCaptureFiles.Clear(TimeSpan.Zero, null, "", "   ", missing);

        Assert.Equal(0, reclaimed);
    }

    [Fact]
    public async Task UnsupportedStoreReclaimsNothing()
    {
        var store = UnsupportedMobileCameraCaptureStore.Instance;

        Assert.Equal(0, await store.ClearLeftoversAsync(TimeSpan.Zero, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void PlatformRootDefaultsToTheNoOpStore()
    {
        var platform = new MobilePlatformServiceRoot(PlatformKind.Android);

        Assert.Same(UnsupportedMobileCameraCaptureStore.Instance, platform.CameraCaptureStore);
    }

    private string WriteCapture(string fileName, int length)
    {
        var path = Path.Combine(_root, fileName);
        File.WriteAllBytes(path, new byte[length]);
        return path;
    }
}
