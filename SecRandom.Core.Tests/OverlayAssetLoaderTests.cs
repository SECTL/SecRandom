using System.Reflection;
using Avalonia.Platform;
using SecRandom;

namespace SecRandom.Core.Tests;

/// <summary>
/// Desktop builds ship <c>SecRandom/Assets</c> beside the executable instead of embedding it, so every
/// <c>avares://SecRandom/Assets/...</c> URI must be resolved from disk by <see cref="OverlayAssetLoader"/>.
/// <para>
/// <see cref="Uri"/> lower-cases the authority of an absolute URI while the <c>avares</c> scheme is
/// unregistered, and preserves the spelling once something registers that scheme (Avalonia's
/// <c>AppBuilder.Setup</c> does), so the loader must accept either spelling. Production hits the
/// lower-cased one: <c>GlobalConstants</c> creates its static font families from the first statement of
/// <c>Program.Main</c>, before Avalonia starts, and the first-run OOBE's first <c>FluentIcon</c> then asks
/// for the <c>Assets/Fonts/</c> collection. A directory-keyed family is resolved by enumerating the assets
/// under that key; an empty enumeration makes Avalonia throw
/// <c>InvalidOperationException: Could not create glyphTypeface</c> during the first layout pass.
/// </para>
/// </summary>
public sealed class OverlayAssetLoaderTests : IDisposable
{
    private const string EmbeddedOnlyPath = "Updates/release-public-key.txt";

    private static readonly string[] EmbeddedOnlyPaths =
    [
        EmbeddedOnlyPath,
        "Plugins/plugin-market-public-key.txt"
    ];

    private readonly string _assetRoot = Path.Combine(
        Path.GetTempPath(), "SecRandom", "overlay-asset-tests", Guid.NewGuid().ToString("N"));

    public OverlayAssetLoaderTests()
    {
        Directory.CreateDirectory(Path.Combine(_assetRoot, "Fonts", "MiSans"));
        File.WriteAllBytes(Path.Combine(_assetRoot, "AppLogo.png"), [0x89, 0x50, 0x4E, 0x47]);
        File.WriteAllBytes(Path.Combine(_assetRoot, "Fonts", "FluentSystemIcons-Resizable.ttf"), [0x00, 0x01, 0x00, 0x00]);
        File.WriteAllBytes(Path.Combine(_assetRoot, "Fonts", "MiSans", "MiSans-Regular.ttf"), [0x00, 0x01, 0x00, 0x00]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_assetRoot))
            Directory.Delete(_assetRoot, recursive: true);
    }

    [Fact]
    public void Exists_ResolvesPhysicalAssetsForEitherAuthoritySpelling()
    {
        // Neither spelling may be rejected: which one the runtime produces for "avares://SecRandom/..."
        // depends on whether the "avares" scheme was already registered when the Uri was created.
        var loader = CreateLoader();

        Assert.True(loader.Exists(new Uri("avares://secrandom/Assets/AppLogo.png")));
        Assert.True(loader.Exists(new Uri("avares://SecRandom/Assets/AppLogo.png")));
        Assert.True(loader.Exists(new Uri("avares://secrandom/Assets/Fonts/FluentSystemIcons-Resizable.ttf")));

        using var stream = loader.Open(new Uri("avares://secrandom/Assets/Fonts/MiSans/MiSans-Regular.ttf"));
        Assert.True(stream.CanRead);
        Assert.True(stream.Length > 0);
    }

    [Fact]
    public void GetAssets_EnumeratesPhysicalFontCollectionForDirectoryKey()
    {
        var assets = CreateLoader()
            .GetAssets(new Uri("avares://secrandom/Assets/Fonts/"), null)
            .Select(asset => asset.ToString())
            .ToList();

        Assert.Contains(assets, asset => asset.EndsWith("/Assets/Fonts/FluentSystemIcons-Resizable.ttf", StringComparison.Ordinal));
        Assert.Contains(assets, asset => asset.EndsWith("/Assets/Fonts/MiSans/MiSans-Regular.ttf", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(EmbeddedOnlyPath)]
    [InlineData("Plugins/plugin-market-public-key.txt")]
    public void Exists_LeavesEmbeddedOnlyTrustAnchorsToTheEmbeddedFallback(string relativePath)
    {
        // The update and plugin-market public keys stay embedded on purpose, so a file dropped next to
        // the executable must never become the trust root (desktop publishing does copy the plugin key
        // into the physical Assets folder).
        var physicalFile = Path.Combine(_assetRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(physicalFile)!);
        File.WriteAllBytes(physicalFile, [0xAA]);

        var embeddedUri = new Uri($"avares://SecRandom/Assets/{relativePath}");
        var loader = CreateLoader(new StubAssetLoader([embeddedUri]));

        Assert.True(loader.Exists(embeddedUri));

        using var stream = loader.Open(embeddedUri);
        Assert.Equal(0x01, stream.ReadByte());
    }

    private OverlayAssetLoader CreateLoader(StubAssetLoader? fallback = null)
    {
        var localAssembly = typeof(OverlayAssetLoader).Assembly;
        return new OverlayAssetLoader(
            fallback ?? new StubAssetLoader([]),
            localAssembly,
            localAssembly.GetName().Name!,
            "/Assets/",
            _assetRoot,
            new HashSet<string>(EmbeddedOnlyPaths, StringComparer.Ordinal));
    }

    /// <summary>Stands in for the embedded <c>avares</c> resources of a desktop build, which no longer carry assets.</summary>
    private sealed class StubAssetLoader(IEnumerable<Uri> embeddedAssets) : IAssetLoader
    {
        private readonly HashSet<string> _embeddedAssets = new(
            embeddedAssets.Select(uri => uri.ToString()), StringComparer.Ordinal);

        public void SetDefaultAssembly(Assembly assembly)
        {
        }

        public bool Exists(Uri uri, Uri? baseUri = null)
        {
            return _embeddedAssets.Contains(uri.ToString());
        }

        public Stream Open(Uri uri, Uri? baseUri = null)
        {
            if (!Exists(uri, baseUri))
                throw new FileNotFoundException(uri.ToString());

            return new MemoryStream([0x01]);
        }

        public (Stream stream, Assembly assembly) OpenAndGetAssembly(Uri uri, Uri? baseUri = null)
            => (Open(uri, baseUri), typeof(StubAssetLoader).Assembly);

        public Assembly? GetAssembly(Uri uri, Uri? baseUri = null)
            => Exists(uri, baseUri) ? typeof(StubAssetLoader).Assembly : null;

        public IEnumerable<Uri> GetAssets(Uri uri, Uri? baseUri)
        {
            return embeddedAssets.Where(asset => asset.ToString().StartsWith(uri.ToString(), StringComparison.Ordinal));
        }

        public void InvalidateAssemblyCache(string name)
        {
        }

        public void InvalidateAssemblyCache()
        {
        }
    }
}
