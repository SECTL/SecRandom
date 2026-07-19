using LR = SecRandom.Mobile.Langs.Mobile.Resources;

namespace SecRandom.Mobile;

/// <summary>
/// Fallback used when the running platform head did not supply an installer (iOS, neutral builds).
/// </summary>
public sealed class UnsupportedMobileUpdateInstaller : IMobileUpdateInstaller
{
    public bool IsSupported => false;

    public async Task<string> StagePackageAsync(byte[] bytes, string assetName, CancellationToken ct)
    {
        var directory = Path.Combine(Path.GetTempPath(), "SecRandom", "updates");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, assetName);
        await File.WriteAllBytesAsync(path + ".partial", bytes, ct);
        File.Move(path + ".partial", path, true);
        return path;
    }

    public void OpenInstaller(string packagePath) =>
        throw new PlatformNotSupportedException(LR.M_AndroidOnlyInstaller);
}
