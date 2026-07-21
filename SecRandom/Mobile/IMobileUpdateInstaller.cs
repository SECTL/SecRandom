namespace SecRandom.Mobile;

/// <summary>
/// Platform seam for staging a downloaded update package and handing it to the system installer.
/// The shared library compiles as neutral net10.0, so platform heads provide the real implementation.
/// </summary>
public interface IMobileUpdateInstaller
{
    bool IsSupported { get; }

    Task<string> StagePackageAsync(byte[] bytes, string assetName, CancellationToken ct);

    void OpenInstaller(string packagePath);
}
