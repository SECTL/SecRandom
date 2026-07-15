using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using SecRandom.Core;
using SecRandom.Core.Services.Config;
using SecRandom.Shared;
using SecRandom.Shared.Updates;
using YamlDotNet.Serialization;
using UpdateResources = SecRandom.Langs.SettingsPages.Update.Resources;

namespace SecRandom.Services.Updates;

public sealed class UpdateCenterService(
    MainConfigHandler configHandler,
    ILogger<UpdateCenterService> logger,
    HttpClient httpClient)
    : INotifyPropertyChanged
{
    private const string Product = "SecRandom";
    private const string Repository = "SECTL/SecRandom";
    private const string ManifestFileName = "SecRandom-update-manifest.v1.json";
    private const string SignatureFileName = "SecRandom-update-manifest.v1.sig";
    private static readonly Uri GitHubRawMetadataUri = new("https://raw.githubusercontent.com/SECTL/SecRandom/master/metadata.yaml");
    private static readonly Uri GitHubMirrorPrefix = new("https://ghproxy.sectl.cn/");
    private readonly HttpClient _httpClient = httpClient;
    private readonly IDeserializer _yamlDeserializer = new DeserializerBuilder().Build();
    private CancellationTokenSource? _operationCancellation;
    private UpdateOperationPhase _phase;
    private string _statusMessage = Text("M_StatusNotChecked");
    private UpdateManifest? _manifest;
    private UpdateArtifact? _selectedArtifact;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<UpdateArtifact> AvailableArtifacts { get; } = [];
    public UpdateOperationPhase Phase
    {
        get => _phase;
        private set => SetField(ref _phase, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetField(ref _statusMessage, value))
                OnPropertyChanged(nameof(Status));
        }
    }

    public string Status => StatusMessage;

    public string CurrentVersion => GlobalConstants.Version;
    public string AvailableVersion => _manifest?.Version ?? string.Empty;
    public string NotesUrl => _manifest?.NotesUrl ?? string.Empty;
    public UpdateArtifact? SelectedArtifact
    {
        get => _selectedArtifact;
        set
        {
            if (SetField(ref _selectedArtifact, value))
                OnStateChanged();
        }
    }

    public bool IsBusy => Phase is UpdateOperationPhase.Checking or UpdateOperationPhase.Downloading or UpdateOperationPhase.Verifying or UpdateOperationPhase.Installing;
    public bool CanInstall => Phase == UpdateOperationPhase.ReadyToInstall && SelectedArtifact is not null;
    public bool HasAvailableArtifacts => AvailableArtifacts.Count > 0;

    public async Task CheckAsync(bool force = false)
    {
        if (IsBusy)
            return;

        CancelCurrentOperation();
        _operationCancellation = new CancellationTokenSource();
        var cancellationToken = _operationCancellation.Token;
        try
        {
            Phase = UpdateOperationPhase.Checking;
            StatusMessage = Text("M_StatusChecking");
            var channel = GetChannel();
            var source = GetSource();
            if (source == UpdateSource.Sectl && channel != UpdateChannel.Release)
                throw new InvalidOperationException(Text("M_SectlReleaseOnly"));

            var tag = await GetChannelTagAsync(source, channel, cancellationToken);
            var manifestBytes = await DownloadAssetAsync(source, tag, ManifestFileName, cancellationToken);
            var signatureBytes = await DownloadAssetAsync(source, tag, SignatureFileName, cancellationToken);
            var manifest = VerifyAndDeserializeManifest(manifestBytes, signatureBytes, tag, channel);
            if (!force && !IsNewerVersion(manifest.Version, GlobalConstants.Version))
            {
                configHandler.Data.UpdateSettings.LastCheckTime = DateTime.Now;
                configHandler.Save();
                Phase = UpdateOperationPhase.UpToDate;
                StatusMessage = Text("M_StatusUpToDate");
                return;
            }

            var artifacts = manifest.Artifacts.Where(IsCompatibleArtifact).ToArray();
            if (artifacts.Length == 0)
                throw new InvalidOperationException(Text("M_NoCompatibleArtifact"));

            _manifest = manifest;
            AvailableArtifacts.Clear();
            foreach (var artifact in artifacts)
                AvailableArtifacts.Add(artifact);
            SelectedArtifact = ReadCurrentPackageMarker() is null ? null : SelectDefaultArtifact(artifacts);
            configHandler.Data.UpdateSettings.LastCheckTime = DateTime.Now;
            configHandler.Save();
            OnPropertyChanged(nameof(AvailableVersion));
            OnPropertyChanged(nameof(NotesUrl));
            Phase = UpdateOperationPhase.ReadyToInstall;
            StatusMessage = SelectedArtifact is null
                ? string.Format(CultureInfo.CurrentUICulture, Text("M_StatusUpdateAvailableSelect"), manifest.Version)
                : string.Format(CultureInfo.CurrentUICulture, Text("M_StatusUpdateAvailable"), manifest.Version);
        }
        catch (OperationCanceledException)
        {
            Phase = UpdateOperationPhase.Cancelled;
            StatusMessage = Text("M_StatusCancelled");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "检查应用更新失败。");
            Phase = UpdateOperationPhase.CheckFailed;
            StatusMessage = exception.Message;
        }
        finally
        {
            OnStateChanged();
        }
    }

    public async Task DownloadAndInstallAsync()
    {
        if (_manifest is null || SelectedArtifact is null || IsBusy)
            return;

        CancelCurrentOperation();
        _operationCancellation = new CancellationTokenSource();
        var cancellationToken = _operationCancellation.Token;
        try
        {
            Phase = UpdateOperationPhase.Downloading;
            StatusMessage = string.Format(CultureInfo.CurrentUICulture, Text("M_StatusDownloading"), SelectedArtifact.AssetName);
            var source = GetSource();
            var package = await DownloadAssetAsync(source, _manifest.Tag, SelectedArtifact.AssetName, cancellationToken);
            Phase = UpdateOperationPhase.Verifying;
            StatusMessage = Text("M_StatusVerifying");
            VerifyArtifact(package, SelectedArtifact);

            var updateDirectory = Utils.GetDirectoryPath("updates", "downloads");
            var extension = Path.GetExtension(SelectedArtifact.AssetName);
            var packagePath = Path.Combine(updateDirectory, SelectedArtifact.Sha512.ToLowerInvariant() + extension);
            await File.WriteAllBytesAsync(packagePath + ".partial", package, cancellationToken);
            File.Move(packagePath + ".partial", packagePath, true);

            Phase = UpdateOperationPhase.Installing;
            StatusMessage = Text("M_StatusPreparing");
            if (ParsePackageKind(SelectedArtifact.Kind) == UpdatePackageKind.PortableZip)
                await DeployPortableAsync(packagePath, SelectedArtifact, cancellationToken);
            else
                StartSystemInstaller(packagePath, ParsePackageKind(SelectedArtifact.Kind));

            Phase = UpdateOperationPhase.Restarting;
            StatusMessage = Text("M_StatusRestarting");
            if (ParsePackageKind(SelectedArtifact.Kind) == UpdatePackageKind.PortableZip)
                await App.Current.RestartThroughLauncherAsync();
            else
                App.Current.Stop();
        }
        catch (OperationCanceledException)
        {
            Phase = UpdateOperationPhase.Cancelled;
            StatusMessage = Text("M_StatusCancelled");
        }
        catch (CryptographicException exception)
        {
            logger.LogWarning(exception, "更新包完整性校验失败。");
            Phase = UpdateOperationPhase.VerificationFailed;
            StatusMessage = exception.Message;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "应用更新失败。");
            Phase = UpdateOperationPhase.InstallFailed;
            StatusMessage = exception.Message;
        }
        finally
        {
            OnStateChanged();
        }
    }

    public void CancelCurrentOperation() => _operationCancellation?.Cancel();

    private async Task<string> GetChannelTagAsync(UpdateSource source, UpdateChannel channel, CancellationToken cancellationToken)
    {
        if (source == UpdateSource.Sectl)
        {
            using var response = await _httpClient.GetAsync("https://appwrite.sectl.cn/api/software/latest-tag?projectSlug=SecRandom", cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            return document.RootElement.GetProperty("tag").GetString() ?? throw new InvalidDataException(Text("M_SectlMissingTag"));
        }

        var metadataUri = source == UpdateSource.GitHub
            ? GitHubRawMetadataUri
            : new Uri($"{GitHubMirrorPrefix}{GitHubRawMetadataUri.AbsoluteUri}");
        var yaml = await _httpClient.GetStringAsync(metadataUri, cancellationToken);
        var metadata = _yamlDeserializer.Deserialize<MetadataDocument>(yaml) ?? throw new InvalidDataException(Text("M_MetadataEmpty"));
        if (metadata.SchemaVersion != 2 || !string.Equals(metadata.Product, Product, StringComparison.Ordinal) || metadata.Channels is null)
            throw new InvalidDataException(Text("M_MetadataInvalid"));
        var name = channel.ToString().ToLowerInvariant();
        return metadata.Channels.TryGetValue(name, out var entry) && !string.IsNullOrWhiteSpace(entry.Tag)
            ? entry.Tag
            : throw new InvalidDataException(string.Format(CultureInfo.CurrentUICulture, Text("M_MetadataChannelMissing"), name));
    }

    private async Task<byte[]> DownloadAssetAsync(UpdateSource source, string tag, string assetName, CancellationToken cancellationToken)
    {
        Uri uri = source switch
        {
            UpdateSource.Sectl => new Uri($"https://appwrite.sectl.cn/api/software/download?projectSlug=SecRandom&tag={Uri.EscapeDataString(tag)}&fileName={Uri.EscapeDataString(assetName)}&source=server"),
            UpdateSource.GitHub => new Uri($"https://github.com/{Repository}/releases/download/{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(assetName)}"),
            UpdateSource.GitHubMirror => new Uri($"{GitHubMirrorPrefix}https://github.com/{Repository}/releases/download/{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(assetName)}"),
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };
        return await _httpClient.GetByteArrayAsync(uri, cancellationToken);
    }

    private UpdateManifest VerifyAndDeserializeManifest(byte[] manifestBytes, byte[] signatureBytes, string expectedTag, UpdateChannel expectedChannel)
    {
        var publicKey = ReadEmbeddedPublicKey();
        var signer = new Ed25519Signer();
        signer.Init(false, new Ed25519PublicKeyParameters(publicKey, 0));
        signer.BlockUpdate(manifestBytes, 0, manifestBytes.Length);
        if (!signer.VerifySignature(signatureBytes))
            throw new CryptographicException(Text("M_ManifestSignatureInvalid"));

        var manifest = JsonSerializer.Deserialize<UpdateManifest>(manifestBytes, JsonOptions)
                       ?? throw new InvalidDataException(Text("M_ManifestInvalid"));
        if (manifest.SchemaVersion != 1 || !string.Equals(manifest.Product, Product, StringComparison.Ordinal)
            || !string.Equals(manifest.Tag, expectedTag, StringComparison.Ordinal)
            || !string.Equals(manifest.Channel, expectedChannel.ToString().ToLowerInvariant(), StringComparison.Ordinal))
            throw new InvalidDataException(Text("M_ManifestMismatch"));
        return manifest;
    }

    private static byte[] ReadEmbeddedPublicKey()
    {
        using var stream = AssetLoader.Open(new Uri("avares://SecRandom/Assets/Updates/release-public-key.txt"));
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var key = Convert.FromBase64String(reader.ReadToEnd().Trim());
        if (key.Length != Ed25519PublicKeyParameters.KeySize || key.All(static value => value == 0))
            throw new CryptographicException(Text("M_PublicKeyInvalid"));
        return key;
    }

    private static bool IsNewerVersion(string candidate, string current)
    {
        return TryParseVersion(candidate, out var candidateVersion) && TryParseVersion(current, out var currentVersion)
            && candidateVersion > currentVersion;
    }

    private static bool TryParseVersion(string text, out Version version)
    {
        var normalized = text.Trim().TrimStart('v', 'V');
        var prereleaseIndex = normalized.IndexOf('-');
        if (prereleaseIndex >= 0)
            normalized = normalized[..prereleaseIndex];
        return Version.TryParse(normalized, out version!);
    }

    private bool IsCompatibleArtifact(UpdateArtifact artifact)
    {
        if (!string.Equals(artifact.Os, GetCurrentOs(), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(artifact.Arch, GetCurrentArchitecture(), StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.Equals(artifact.RuntimeKind, "framework-dependent", StringComparison.OrdinalIgnoreCase)
            && !HasRequiredDesktopRuntime())
            return false;

        var currentMarker = ReadCurrentPackageMarker();
        return currentMarker is null || string.Equals(artifact.Kind, currentMarker.PackageKind, StringComparison.OrdinalIgnoreCase);
    }

    private UpdateArtifact? SelectDefaultArtifact(IEnumerable<UpdateArtifact> artifacts)
    {
        var marker = ReadCurrentPackageMarker();
        if (marker is not null)
        {
            var exact = artifacts.FirstOrDefault(artifact => string.Equals(artifact.Kind, marker.PackageKind, StringComparison.OrdinalIgnoreCase)
                                                              && string.Equals(artifact.RuntimeKind, marker.RuntimeKind, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
                return exact;
        }

        return artifacts.FirstOrDefault(artifact => string.Equals(artifact.RuntimeKind, "self-contained", StringComparison.OrdinalIgnoreCase))
               ?? artifacts.FirstOrDefault();
    }

    private static UpdatePackageMarker? ReadCurrentPackageMarker()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "SecRandom.package.json");
        if (!File.Exists(path))
            return null;
        try
        {
            var marker = JsonSerializer.Deserialize<UpdatePackageMarker>(File.ReadAllText(path), JsonOptions);
            return marker is { SchemaVersion: 1, Product: Product } ? marker : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task DeployPortableAsync(string packagePath, UpdateArtifact artifact, CancellationToken cancellationToken)
    {
        if (Utils.PackageRoot == Path.GetFullPath(AppContext.BaseDirectory))
            throw new InvalidOperationException(Text("M_PortableLauncherMissing"));

        var transaction = Guid.NewGuid().ToString("N");
        var stagingRoot = Utils.GetDirectoryPath("updates", "staging", transaction);
        ExtractPortableZip(packagePath, stagingRoot);
        var appDirectory = Directory.EnumerateDirectories(stagingRoot, "app-*").SingleOrDefault();
        if (appDirectory is null)
            throw new InvalidDataException(Text("M_ZipAppDirectoryMissing"));

        var marker = JsonSerializer.Deserialize<UpdatePackageMarker>(File.ReadAllText(Path.Combine(appDirectory, "SecRandom.package.json")), JsonOptions)
                     ?? throw new InvalidDataException(Text("M_ZipMarkerMissing"));
        if (marker.SchemaVersion != 1 || !string.Equals(marker.Product, Product, StringComparison.Ordinal)
            || !string.Equals(marker.Rid, GetCurrentRid(), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(marker.PackageKind, "portable-zip", StringComparison.Ordinal)
            || !string.Equals(marker.RuntimeKind, artifact.RuntimeKind, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(Text("M_ZipMismatch"));

        await File.WriteAllTextAsync(Path.Combine(appDirectory, ".partial"), string.Empty, cancellationToken);
        var targetDirectory = Path.Combine(Utils.PackageRoot, Path.GetFileName(appDirectory));
        if (Directory.Exists(targetDirectory))
            throw new InvalidOperationException(Text("M_TargetVersionExists"));
        Directory.Move(appDirectory, targetDirectory);
        File.Delete(Path.Combine(targetDirectory, ".partial"));

        var launcherName = OperatingSystem.IsWindows() ? "SecRandomLauncher.exe" : "SecRandomLauncher";
        var stagedLauncher = Path.Combine(stagingRoot, launcherName);
        if (!File.Exists(stagedLauncher))
            throw new InvalidDataException(Text("M_ZipLauncherMissing"));
        var targetLauncher = Path.Combine(Utils.PackageRoot, launcherName);
        File.Move(stagedLauncher, targetLauncher + ".new", true);
        File.Move(targetLauncher + ".new", targetLauncher, true);

        foreach (var previous in Directory.EnumerateDirectories(Utils.PackageRoot, "app-*"))
            if (!string.Equals(previous, targetDirectory, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                File.Delete(Path.Combine(previous, ".current"));
        await File.WriteAllTextAsync(Path.Combine(targetDirectory, ".current"), string.Empty, cancellationToken);
    }

    private static void VerifyArtifact(byte[] bytes, UpdateArtifact artifact)
    {
        if (bytes.LongLength != artifact.ByteLength)
            throw new CryptographicException(Text("M_ArtifactLengthMismatch"));
        var hash = Convert.ToHexString(SHA512.HashData(bytes));
        if (!string.Equals(hash, artifact.Sha512, StringComparison.OrdinalIgnoreCase))
            throw new CryptographicException(Text("M_ArtifactHashMismatch"));
    }

    private static void ExtractPortableZip(string packagePath, string stagingRoot)
    {
        const int maxEntries = 20_000;
        const long maxTotalBytes = 4L * 1024 * 1024 * 1024;
        using var archive = ZipFile.OpenRead(packagePath);
        if (archive.Entries.Count > maxEntries)
            throw new InvalidDataException(Text("M_ZipEntryLimit"));

        long totalBytes = 0;
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
                continue;
            if (Path.IsPathRooted(entry.FullName) || entry.FullName.Contains("..", StringComparison.Ordinal))
                throw new InvalidDataException(Text("M_ZipInvalidPath"));
            totalBytes = checked(totalBytes + entry.Length);
            if (totalBytes > maxTotalBytes)
                throw new InvalidDataException(Text("M_ZipSizeLimit"));

            var targetPath = Path.GetFullPath(Path.Combine(stagingRoot, entry.FullName));
            if (!targetPath.StartsWith(Path.GetFullPath(stagingRoot) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
                throw new InvalidDataException(Text("M_ZipOutsidePath"));
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            entry.ExtractToFile(targetPath, overwrite: false);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static void StartSystemInstaller(string packagePath, UpdatePackageKind packageKind)
    {
        ProcessStartInfo startInfo = packageKind switch
        {
            UpdatePackageKind.WindowsExe => new ProcessStartInfo(packagePath)
            {
                UseShellExecute = true,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS"
            },
            UpdatePackageKind.LinuxDeb => new ProcessStartInfo("pkexec")
            {
                UseShellExecute = false,
                ArgumentList = { "apt", "install", "-y", packagePath }
            },
            UpdatePackageKind.MacosPkg => new ProcessStartInfo("open")
            {
                UseShellExecute = false,
                ArgumentList = { packagePath }
            },
            _ => throw new InvalidOperationException(Text("M_InstallerUnsupported"))
        };
        if (Process.Start(startInfo) is null)
            throw new InvalidOperationException(Text("M_InstallerStartFailed"));
    }

    private UpdateChannel GetChannel() => configHandler.Data.UpdateSettings.UpdateChannel;

    private UpdateSource GetSource() => configHandler.Data.UpdateSettings.UpdateSource;

    private static UpdatePackageKind ParsePackageKind(string value) => value.ToLowerInvariant() switch
    {
        "portable-zip" => UpdatePackageKind.PortableZip,
        "windows-exe" => UpdatePackageKind.WindowsExe,
        "linux-deb" => UpdatePackageKind.LinuxDeb,
        "macos-pkg" => UpdatePackageKind.MacosPkg,
        _ => throw new InvalidDataException(Text("M_UnknownPackageKind"))
    };

    private static string GetCurrentOs() => OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : "macos";
    private static string GetCurrentArchitecture() => RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant() switch { "x64" => "x64", "x86" => "x86", "arm64" => "arm64", var value => value };
    private static string GetCurrentRid() => OperatingSystem.IsWindows() ? $"win-{GetCurrentArchitecture()}" : OperatingSystem.IsLinux() ? $"linux-{GetCurrentArchitecture()}" : $"osx-{GetCurrentArchitecture()}";

    private static bool HasRequiredDesktopRuntime()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("dotnet", "--list-runtimes")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is null)
                return false;

            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(3000) || process.ExitCode != 0)
                return false;
            var runtime = OperatingSystem.IsWindows() ? "Microsoft.WindowsDesktop.App 10." : "Microsoft.NETCore.App 10.";
            return output.Split('\n').Any(line => line.StartsWith(runtime, StringComparison.Ordinal));
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string Text(string key) => UpdateResources.ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    private void OnStateChanged()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(HasAvailableArtifacts));
    }

    private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    private bool SetField<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName!);
        return true;
    }

    private sealed class MetadataDocument
    {
        [YamlDotNet.Serialization.YamlMember(Alias = "schema_version")]
        public int SchemaVersion { get; init; }

        [YamlDotNet.Serialization.YamlMember(Alias = "product")]
        public string Product { get; init; } = string.Empty;

        [YamlDotNet.Serialization.YamlMember(Alias = "channels")]
        public Dictionary<string, MetadataChannel>? Channels { get; init; }
    }

    private sealed class MetadataChannel
    {
        [YamlDotNet.Serialization.YamlMember(Alias = "tag")]
        public string Tag { get; init; } = string.Empty;
    }
}
