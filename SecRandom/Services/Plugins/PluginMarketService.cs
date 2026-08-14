using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Platform;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using SecRandom.PluginSdk;
using SecRandom.Shared;
using SecRandom.Shared.Models.Plugins;
using SR = SecRandom.Langs.SettingsPages.Plugins.Overview.Resources;

namespace SecRandom.Services.Plugins;

/// <summary>
///     Result of resolving a market install together with its dependency closure.
/// </summary>
public sealed record PluginMarketInstallPlan(
    IReadOnlyList<PluginCatalogEntry> Entries,
    bool HasDependencies)
{
    public PluginCatalogEntry Target => Entries[^1];
}

/// <summary>
///     Client for the signed SecRandom plugin market. The index is Ed25519-signed as a whole (including
///     each entry's SHA-256), downloaded mirror-first with a GitHub fallback, then every package is
///     SHA-256-verified before it is staged. Dependencies are resolved in topological order.
/// </summary>
public sealed class PluginMarketService(
    ILogger<PluginMarketService> logger,
    HttpClient httpClient,
    IPluginManager pluginManager)
{
    public const string IndexReleaseTag = "generated";
    private const string IndexRepository = "SECTL/SecRandom-PluginIndex";
    private const string IndexFileName = "index.json";
    private const string SignatureFileName = "index.json.sig";
    private static readonly Uri GitHubMirrorPrefix = new("https://ghproxy.sectl.cn/");

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public IReadOnlyList<PluginCatalogEntry> Entries { get; private set; } = [];

    public bool IsBusy { get; private set; }

    /// <summary>
    ///     Refreshes the market index. Fetches mirror-first with a GitHub fallback, verifies the Ed25519
    ///     signature over the raw index bytes, then replaces <see cref="Entries"/>.
    /// </summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        try
        {
            var (indexBytes, signatureBytes) = await DownloadIndexAsync(cancellationToken).ConfigureAwait(false);
            VerifyIndexSignature(indexBytes, signatureBytes);

            var document = JsonSerializer.Deserialize<PluginIndexDocument>(indexBytes, JsonOptions)
                           ?? throw new InvalidDataException(SR.M_IndexInvalid);
            if (document.SchemaVersion != 1
                || !string.Equals(document.Product, "SecRandom", StringComparison.Ordinal)
                || document.Plugins is null)
                throw new InvalidDataException(SR.M_IndexInvalid);

            Entries = document.Plugins
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Id))
                .GroupBy(entry => entry.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray();
            logger.LogInformation("Plugin market refreshed: {Count} entries.", Entries.Count);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to refresh the plugin market index.");
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    ///     Resolves the target entry together with its required dependency closure in install order
    ///     (dependencies first, target last). Throws on a missing or cyclic required dependency.
    /// </summary>
    public PluginMarketInstallPlan ResolveInstallPlan(PluginCatalogEntry target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var byId = Entries.ToDictionary(entry => entry.Id, StringComparer.OrdinalIgnoreCase);
        var ordered = new List<PluginCatalogEntry>();
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Visit(target);

        return new PluginMarketInstallPlan(ordered, ordered.Count > 1);

        void Visit(PluginCatalogEntry entry)
        {
            if (visited.Contains(entry.Id))
                return;
            if (!visiting.Add(entry.Id))
                throw new InvalidDataException(string.Format(SR.M_DependencyCycle, entry.Id));

            foreach (var dependency in entry.Dependencies)
            {
                if (!dependency.Required)
                    continue;
                if (!byId.TryGetValue(dependency.Id, out var dependencyEntry))
                    throw new InvalidDataException(string.Format(SR.M_DependencyMissing, dependency.Id, entry.Id));
                Visit(dependencyEntry);
            }

            visiting.Remove(entry.Id);
            visited.Add(entry.Id);
            ordered.Add(entry);
        }
    }

    /// <summary>
    ///     Downloads every entry in the plan, verifies its SHA-256, and stages it. Packages are processed
    ///     in dependency order so a failed dependency never leaves a partial target install.
    /// </summary>
    public async Task InstallAsync(PluginMarketInstallPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        foreach (var entry in plan.Entries)
        {
            var packagePath = await DownloadPackageAsync(entry, cancellationToken).ConfigureAwait(false);
            try
            {
                VerifyPackageHash(packagePath, entry.Sha256);
                pluginManager.StagePackage(packagePath);
                logger.LogInformation("Staged plugin {PluginId} {Version}.", entry.Id, entry.Version);
            }
            finally
            {
                if (File.Exists(packagePath))
                    File.Delete(packagePath);
            }
        }
    }

    /// <summary>
    ///     True when the entry's apiVersion major is not below the host major and its minimum host
    ///     version is not above the current application version.
    /// </summary>
    public static bool IsCompatible(PluginCatalogEntry entry, string currentVersion)
    {
        if (!Version.TryParse(entry.ApiVersion, out var apiVersion)
            || apiVersion.Major < PluginApiVersions.Current.Major)
            return false;

        if (!string.IsNullOrWhiteSpace(entry.MinimumHostVersion)
            && Version.TryParse(entry.MinimumHostVersion.TrimStart('v', 'V'), out var minimumHost)
            && Version.TryParse(currentVersion.TrimStart('v', 'V'), out var current)
            && minimumHost > current)
            return false;

        return true;
    }

    private async Task<(byte[] Index, byte[] Signature)> DownloadIndexAsync(CancellationToken cancellationToken)
    {
        var indexBytes = await DownloadWithFallbackAsync(
            $"{IndexRepository}/releases/download/{IndexReleaseTag}/{IndexFileName}",
            cancellationToken).ConfigureAwait(false);
        var signatureBytes = await DownloadWithFallbackAsync(
            $"{IndexRepository}/releases/download/{IndexReleaseTag}/{SignatureFileName}",
            cancellationToken).ConfigureAwait(false);
        return (indexBytes, signatureBytes);
    }

    private async Task<byte[]> DownloadWithFallbackAsync(string resource, CancellationToken cancellationToken)
    {
        var direct = resource.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || resource.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? new Uri(resource)
            : new Uri($"https://github.com/{resource}");
        var mirror = new Uri($"{GitHubMirrorPrefix}{direct.AbsoluteUri}");
        try
        {
            return await httpClient.GetByteArrayAsync(mirror, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception mirrorException) when (mirrorException is not OperationCanceledException)
        {
            logger.LogDebug("Plugin market mirror failed; falling back to GitHub: {Url}", direct);
            return await httpClient.GetByteArrayAsync(direct, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<string> DownloadPackageAsync(PluginCatalogEntry entry, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(entry.DownloadUrl))
            throw new InvalidDataException(string.Format(SR.M_DownloadUrlMissing, entry.Id));

        var bytes = await DownloadWithFallbackAsync(entry.DownloadUrl, cancellationToken).ConfigureAwait(false);

        var tempDirectory = Path.Combine(Path.GetTempPath(), "SecRandomPluginMarket");
        Directory.CreateDirectory(tempDirectory);
        var packagePath = Path.Combine(tempDirectory, $"{entry.Id}-{Guid.NewGuid():N}.srpx");
        await File.WriteAllBytesAsync(packagePath, bytes, cancellationToken).ConfigureAwait(false);
        return packagePath;
    }

    private static void VerifyPackageHash(string packagePath, string expectedSha256)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256))
            throw new InvalidDataException("The plugin market entry does not provide a SHA-256.");

        using var stream = File.OpenRead(packagePath);
        var hash = Convert.ToHexString(SHA256.HashData(stream));
        if (!string.Equals(hash, expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new CryptographicException(string.Format(SR.M_PackageHashMismatch, expectedSha256, hash));
    }

    private static void VerifyIndexSignature(byte[] indexBytes, byte[] signatureBytes)
    {
        var publicKey = ReadEmbeddedPublicKey();
        var signer = new Ed25519Signer();
        signer.Init(false, new Ed25519PublicKeyParameters(publicKey, 0));
        signer.BlockUpdate(indexBytes, 0, indexBytes.Length);
        if (!signer.VerifySignature(signatureBytes))
            throw new CryptographicException(SR.M_IndexSignatureInvalid);
    }

    private static byte[] ReadEmbeddedPublicKey()
    {
        using var stream = AssetLoader.Open(new Uri("avares://SecRandom/Assets/Plugins/plugin-market-public-key.txt"));
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var key = Convert.FromBase64String(reader.ReadToEnd().Trim());
        if (key.Length != Ed25519PublicKeyParameters.KeySize || key.All(static value => value == 0))
            throw new CryptographicException(SR.M_PublicKeyInvalid);
        return key;
    }
}
