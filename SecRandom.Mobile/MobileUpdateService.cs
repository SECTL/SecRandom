using System.ComponentModel;
using System.Globalization;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Platform;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using SecRandom.Shared.Updates;
using YamlDotNet.Serialization;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;

namespace SecRandom.Mobile;

public sealed class MobileUpdateService(HttpClient httpClient) : INotifyPropertyChanged
{
    private const string Repository = "SECTL/SecRandom";
    private const string ManifestFileName = "SecRandom-update-manifest.v1.json";
    private const string SignatureFileName = "SecRandom-update-manifest.v1.sig";
    private static readonly Uri MetadataUri = new("https://raw.githubusercontent.com/SECTL/SecRandom/master/metadata.yaml");
    private static readonly Uri MirrorPrefix = new("https://ghproxy.sectl.cn/");
    private readonly IDeserializer _yaml = new DeserializerBuilder().IgnoreUnmatchedProperties().Build();
    private UpdateArtifact? _artifact;
    private string _availableVersion = string.Empty;
    private string _status = string.Empty;
    private bool _isBusy;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string AvailableVersion
    {
        get => _availableVersion;
        private set => SetField(ref _availableVersion, value);
    }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public bool IsUpdateAvailable => _artifact is not null;
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            Status = LR.M_CheckingUpdates;
            var (tag, manifest) = await GetManifestAsync(cancellationToken);
            var artifact = manifest.Artifacts.SingleOrDefault(artifact =>
                string.Equals(artifact.Os, "android", StringComparison.OrdinalIgnoreCase)
                && string.Equals(artifact.Arch, "arm64", StringComparison.OrdinalIgnoreCase)
                && string.Equals(artifact.Kind, "android-apk", StringComparison.OrdinalIgnoreCase));
            if (artifact is null || !IsNewerVersion(manifest.Version, GetCurrentVersion()))
            {
                _artifact = null;
                AvailableVersion = string.Empty;
                Status = LR.M_UpToDate;
                OnPropertyChanged(nameof(IsUpdateAvailable));
                return;
            }

            _artifact = artifact;
            AvailableVersion = manifest.Version;
            Status = string.Format(CultureInfo.CurrentCulture, LR.M_UpdateAvailable, manifest.Version);
            OnPropertyChanged(nameof(IsUpdateAvailable));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Status = string.Format(CultureInfo.CurrentCulture, LR.M_CheckUpdatesFailed, exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DownloadAndInstallAsync(CancellationToken cancellationToken = default)
    {
        if (_artifact is null || IsBusy)
            return;

        try
        {
            IsBusy = true;
            Status = LR.M_DownloadingUpdate;
            var bytes = await DownloadWithFallbackAsync(_artifact.AssetName, cancellationToken);
            VerifyArtifact(bytes, _artifact);
            var path = await WritePackageAsync(bytes, _artifact.AssetName, cancellationToken);
            Status = LR.M_OpeningInstaller;
            OpenSystemInstaller(path);
            Status = LR.M_InstallerOpened;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Status = string.Format(CultureInfo.CurrentCulture, LR.M_InstallUpdateFailed, exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<(string Tag, UpdateManifest Manifest)> GetManifestAsync(CancellationToken cancellationToken)
    {
        foreach (var source in GetSources())
        {
            try
            {
                var metadata = await httpClient.GetStringAsync(GetMetadataUri(source), cancellationToken);
                var document = _yaml.Deserialize<MetadataDocument>(metadata) ?? throw new InvalidDataException(LR.M_EmptyMetadata);
                var tag = document.Channels?.GetValueOrDefault("release")?.Tag;
                if (string.IsNullOrWhiteSpace(tag))
                    throw new InvalidDataException(LR.M_MissingReleaseChannel);

                var manifestBytes = await DownloadAsync(source, tag, ManifestFileName, cancellationToken);
                var signatureBytes = await DownloadAsync(source, tag, SignatureFileName, cancellationToken);
                VerifyManifest(manifestBytes, signatureBytes, tag);
                var manifest = JsonSerializer.Deserialize<UpdateManifest>(manifestBytes)
                                ?? throw new InvalidDataException(LR.M_ManifestInvalid);
                return (tag, manifest);
            }
            catch (Exception) when (source != UpdateSource.GitHub)
            {
                // The direct GitHub source is the fallback when the mirror is unavailable.
            }
        }

        throw new InvalidOperationException(LR.M_ManifestUnavailable);
    }

    private async Task<byte[]> DownloadWithFallbackAsync(string assetName, CancellationToken cancellationToken)
    {
        foreach (var source in GetSources())
        {
            try
            {
                var metadata = await httpClient.GetStringAsync(GetMetadataUri(source), cancellationToken);
                var document = _yaml.Deserialize<MetadataDocument>(metadata) ?? throw new InvalidDataException(LR.M_EmptyMetadata);
                var tag = document.Channels?.GetValueOrDefault("release")?.Tag;
                if (!string.IsNullOrWhiteSpace(tag))
                    return await DownloadAsync(source, tag, assetName, cancellationToken);
            }
            catch (Exception) when (source != UpdateSource.GitHub)
            {
            }
        }

        throw new InvalidOperationException(LR.M_PackageUnavailable);
    }

    private static IEnumerable<UpdateSource> GetSources() => [UpdateSource.GitHubMirror, UpdateSource.GitHub];

    private static Uri GetMetadataUri(UpdateSource source) => source == UpdateSource.GitHub
        ? MetadataUri
        : new Uri($"{MirrorPrefix}{MetadataUri.AbsoluteUri}");

    private Task<byte[]> DownloadAsync(UpdateSource source, string tag, string assetName, CancellationToken cancellationToken)
    {
        var direct = $"https://github.com/{Repository}/releases/download/{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(assetName)}";
        var uri = source == UpdateSource.GitHub ? new Uri(direct) : new Uri($"{MirrorPrefix}{direct}");
        return httpClient.GetByteArrayAsync(uri, cancellationToken);
    }

    private static void VerifyArtifact(byte[] bytes, UpdateArtifact artifact)
    {
        if (bytes.LongLength != artifact.ByteLength)
            throw new CryptographicException(LR.M_PackageLengthInvalid);
        if (!string.Equals(Convert.ToHexString(SHA512.HashData(bytes)), artifact.Sha512, StringComparison.OrdinalIgnoreCase))
            throw new CryptographicException(LR.M_PackageHashInvalid);
    }

    private static void VerifyManifest(byte[] manifest, byte[] signature, string tag)
    {
        var signer = new Ed25519Signer();
        signer.Init(false, new Ed25519PublicKeyParameters(ReadPublicKey(), 0));
        signer.BlockUpdate(manifest, 0, manifest.Length);
        if (!signer.VerifySignature(signature))
            throw new CryptographicException(LR.M_ManifestSignatureInvalid);

        var parsed = JsonSerializer.Deserialize<UpdateManifest>(manifest)
                      ?? throw new InvalidDataException(LR.M_ManifestInvalid);
        if (parsed.SchemaVersion != 1 || parsed.Product != "SecRandom" || parsed.Tag != tag)
            throw new InvalidDataException(LR.M_ManifestTagMismatch);
    }

    private static byte[] ReadPublicKey()
    {
        using var stream = AssetLoader.Open(new Uri("avares://SecRandom.Mobile/Assets/Updates/release-public-key.txt"));
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return Convert.FromBase64String(reader.ReadToEnd().Trim());
    }

    private static async Task<string> WritePackageAsync(byte[] bytes, string assetName, CancellationToken cancellationToken)
    {
#if ANDROID
#pragma warning disable CA1416
        return await WriteAndroidPackageAsync(bytes, assetName, cancellationToken);
#pragma warning restore CA1416
#else
        var directory = Path.Combine(Path.GetTempPath(), "SecRandom", "updates");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, assetName);
        await File.WriteAllBytesAsync(path + ".partial", bytes, cancellationToken);
        File.Move(path + ".partial", path, true);
        return path;
#endif
    }

    private static void OpenSystemInstaller(string path)
    {
#if ANDROID
#pragma warning disable CA1416
        OpenAndroidSystemInstaller(path);
#pragma warning restore CA1416
#else
        throw new PlatformNotSupportedException(LR.M_AndroidOnlyInstaller);
#endif
    }

#if ANDROID
    [System.Runtime.Versioning.SupportedOSPlatform("android24.0")]
    private static async Task<string> WriteAndroidPackageAsync(byte[] bytes, string assetName, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Android.App.Application.Context!.CacheDir!.AbsolutePath!, "updates");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, assetName);
        await File.WriteAllBytesAsync(path + ".partial", bytes, cancellationToken);
        File.Move(path + ".partial", path, true);
        return path;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("android24.0")]
    private static void OpenAndroidSystemInstaller(string path)
    {
        var context = Android.App.Application.Context ?? throw new InvalidOperationException(LR.M_AndroidContextUnavailable);
        var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(context,
            $"{context.PackageName}.updatefileprovider", new Java.IO.File(path));
        var intent = new Android.Content.Intent(Android.Content.Intent.ActionView)
            .SetDataAndType(uri, "application/vnd.android.package-archive")
            .AddFlags(Android.Content.ActivityFlags.GrantReadUriPermission | Android.Content.ActivityFlags.NewTask);
        context.StartActivity(intent);
    }
#endif

    private static string GetCurrentVersion() => typeof(MobileUpdateService).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";

    private static bool IsNewerVersion(string candidate, string current) =>
        Version.TryParse(candidate.TrimStart('v', 'V'), out var candidateVersion)
        && Version.TryParse(current.TrimStart('v', 'V'), out var currentVersion)
        && candidateVersion > currentVersion;

    private void SetField<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed class MetadataDocument
    {
        [YamlMember(Alias = "channels")]
        public Dictionary<string, MetadataChannel>? Channels { get; init; }
    }

    private sealed class MetadataChannel
    {
        [YamlMember(Alias = "tag")]
        public string Tag { get; init; } = string.Empty;
    }
}
