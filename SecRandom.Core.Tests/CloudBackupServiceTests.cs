using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Models;
using SecRandom.Core.Services.Archive;
using SecRandom.Core.Services.Config;
using SecRandom.Services.Auth;
using SecRandom.Services.Config;
using SecRandom.Services.ImportExport;
using SecRandom.Shared;

namespace SecRandom.Core.Tests;

public sealed class CloudBackupServiceTests : IDisposable
{
    private const string ClientId = SectlAuthService.ClientId;

    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "SecRandom", "cloud-backup-tests", Guid.NewGuid().ToString("N"));

    public CloudBackupServiceTests()
    {
        ResetDataRootForTests();
        ConfigureDataRootForTests(_dataRoot);
    }

    [Fact]
    public async Task UploadAsync_UploadsPartsBeforeTheManifestAndReportsProgress()
    {
        var uploadedNames = new List<string>();
        var archive = CreateArchiveBytes(CloudBackupPackage.DefaultPartBytes + 11);
        var handler = new RecordingHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/upload", StringComparison.Ordinal))
            {
                uploadedNames.Add(ReadUploadedName(request));
                return Json("{\"success\":true,\"file_id\":\"file-1\",\"filename\":\"part.srpart\",\"size\":1}");
            }

            throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
        });
        var service = CreateService(handler, archive);
        var progress = new List<CloudBackupProgress>();

        var descriptor = await service.UploadAsync(new ProgressRecorder(progress), TestContext.Current.CancellationToken);

        Assert.Equal(3, uploadedNames.Count);
        Assert.EndsWith($"{CloudBackupPackage.ManifestSuffix}", uploadedNames[3 - 1]);
        Assert.Contains("-p01of02", uploadedNames[0]);
        Assert.Contains("-p02of02", uploadedNames[1]);
        Assert.True(descriptor.IsComplete);
        Assert.Equal(2, descriptor.PartCount);
        Assert.Equal(archive.LongLength, descriptor.TotalBytes);
        Assert.Equal([1, 2, 3], progress.Select(item => item.Completed));
        Assert.Equal(CloudBackupProgress.DoneStage, progress[^1].Stage);
        Assert.False(File.Exists(Path.Combine(_dataRoot, "cache", "cloud-backup", $"{descriptor.BackupId}.zip")));
    }

    [Fact]
    public async Task UploadAsync_WhenAPartUploadFails_RollsBackTheUploadedParts()
    {
        var deleted = new List<string>();
        var attempts = 0;
        var archive = CreateArchiveBytes(CloudBackupPackage.DefaultPartBytes * 2 + 11);
        var handler = new RecordingHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/upload", StringComparison.Ordinal))
            {
                attempts++;
                if (attempts == 3)
                    return Json("{\"error\":\"storage_exceeded\",\"error_description\":\"Storage quota exceeded\"}", HttpStatusCode.RequestEntityTooLarge);
                return Json($"{{\"success\":true,\"file_id\":\"file-{attempts}\",\"filename\":\"part.srpart\",\"size\":1}}");
            }

            if (request.Method == HttpMethod.Delete)
            {
                deleted.Add(request.RequestUri.AbsolutePath);
                return Json("{\"success\":true}");
            }

            throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
        });
        var service = CreateService(handler, archive);

        var exception = await Assert.ThrowsAsync<SectlCloudStorageException>(() =>
            service.UploadAsync(cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("storage_exceeded", exception.Code);
        Assert.Equal(["/api/cloud/files/file-1", "/api/cloud/files/file-2"], deleted);
    }

    [Fact]
    public async Task ListAsync_GroupsPartsWithTheirManifestAndFlagsIncompleteBackups()
    {
        var handler = new RecordingHandler(_ => Json(BuildListResponse(
            ("f-manifest", $"{CloudBackupPackage.FilePrefix}20260830-120000-aaaa0001{CloudBackupPackage.ManifestSuffix}", 20),
            ("f-part-1", CloudBackupPackage.BuildPartName("20260830-120000-aaaa0001", 1, 2), 100),
            ("f-part-2", CloudBackupPackage.BuildPartName("20260830-120000-aaaa0001", 2, 2), 50),
            ("f-orphan", CloudBackupPackage.BuildPartName("20260830-130000-bbbb0002", 1, 2), 70),
            ("f-foreign", "holiday-photo.png", 999))));
        var service = CreateService(handler);

        var backups = await service.ListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, backups.Count);
        var complete = backups.Single(item => item.BackupId == "20260830-120000-aaaa0001");
        Assert.True(complete.IsComplete);
        Assert.True(complete.CanRestore);
        Assert.Equal(150, complete.TotalBytes);
        Assert.Equal(2, complete.PartCount);
        Assert.Equal("f-manifest", complete.ManifestFileId);

        var incomplete = backups.Single(item => item.BackupId == "20260830-130000-bbbb0002");
        Assert.False(incomplete.IsComplete);
        Assert.False(incomplete.CanRestore);
        Assert.Equal(70, incomplete.TotalBytes);
    }

    [Fact]
    public async Task DownloadAsync_RejectsAnIncompleteBackupWithoutAnyRequest()
    {
        var requests = 0;
        var handler = new RecordingHandler(_ =>
        {
            requests++;
            return Json("{}");
        });
        var service = CreateService(handler);
        var descriptor = new CloudBackupDescriptor("20260830-120000-aaaa0001", "name", DateTimeOffset.UtcNow, 10, 1, false, null);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.DownloadAsync(descriptor, TestContext.Current.CancellationToken));
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task DownloadAsync_VerifiesEachPartAndWritesTheRebuiltArchive()
    {
        var archive = CreateArchiveBytes(CloudBackupPackage.DefaultPartBytes + 37);
        var parts = CloudBackupPackage.Slice(archive, CloudBackupPackage.DefaultPartBytes);
        var backupId = "20260830-120000-aaaa0001";
        var manifest = CloudBackupPackage.BuildManifest(backupId, parts, CloudBackupPackage.DefaultPartBytes,
            DataArchiveService.CloudBackupRoots, DateTimeOffset.UtcNow.UtcDateTime, "v3.0.0", archive);
        var manifestName = CloudBackupPackage.BuildManifestName(backupId);
        var manifestBytes = CloudBackupPackage.SerializeManifest(manifest);
        var handler = new RecordingHandler(request => Download(request, new Dictionary<string, byte[]>
        {
            ["f-manifest"] = manifestBytes,
            ["f-part-1"] = parts[0],
            ["f-part-2"] = parts[1]
        }));
        var service = CreateService(handler);
        handler.Files = BuildListResponse(
            ("f-manifest", manifestName, manifestBytes.Length),
            ("f-part-1", manifest.Parts[0].Name, parts[0].Length),
            ("f-part-2", manifest.Parts[1].Name, parts[1].Length));
        var descriptor = new CloudBackupDescriptor(backupId, manifestName, DateTimeOffset.UtcNow,
            archive.LongLength, 2, true, "f-manifest");

        var path = await service.DownloadAsync(descriptor, TestContext.Current.CancellationToken);

        Assert.Equal(archive, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DownloadAsync_RejectsATamperedPartBeforeWritingALocalArchive()
    {
        var archive = CreateArchiveBytes(CloudBackupPackage.DefaultPartBytes + 37);
        var parts = CloudBackupPackage.Slice(archive, CloudBackupPackage.DefaultPartBytes);
        var backupId = "20260830-120000-aaaa0001";
        var manifest = CloudBackupPackage.BuildManifest(backupId, parts, CloudBackupPackage.DefaultPartBytes,
            DataArchiveService.CloudBackupRoots, DateTimeOffset.UtcNow.UtcDateTime, "v3.0.0", archive);
        var tampered = (byte[])parts[1].Clone();
        tampered[0] ^= 0xFF;
        var handler = new RecordingHandler(request => Download(request, new Dictionary<string, byte[]>
        {
            ["f-manifest"] = CloudBackupPackage.SerializeManifest(manifest),
            ["f-part-1"] = parts[0],
            ["f-part-2"] = tampered
        }));
        var service = CreateService(handler);
        handler.Files = BuildListResponse(
            ("f-manifest", CloudBackupPackage.BuildManifestName(backupId), 100),
            ("f-part-1", manifest.Parts[0].Name, parts[0].Length),
            ("f-part-2", manifest.Parts[1].Name, parts[1].Length));
        var descriptor = new CloudBackupDescriptor(backupId, "name", DateTimeOffset.UtcNow,
            archive.LongLength, 2, true, "f-manifest");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.DownloadAsync(descriptor, TestContext.Current.CancellationToken));

        Assert.False(File.Exists(Path.Combine(_dataRoot, "cache", "cloud-backup", $"{backupId}.zip")));
    }

    [Fact]
    public async Task DeleteAsync_DeletesPartsBeforeTheManifest()
    {
        var operations = new List<string>();
        var backupId = "20260830-120000-aaaa0001";
        var handler = new RecordingHandler(request =>
        {
            if (request.Method == HttpMethod.Delete)
            {
                operations.Add(request.RequestUri!.AbsolutePath);
                return Json("{\"success\":true}");
            }

            return Json(BuildListResponse(
                ("f-manifest", CloudBackupPackage.BuildManifestName(backupId), 10),
                ("f-part-1", CloudBackupPackage.BuildPartName(backupId, 1, 2), 10),
                ("f-part-2", CloudBackupPackage.BuildPartName(backupId, 2, 2), 10)));
        });
        var service = CreateService(handler);
        var descriptor = new CloudBackupDescriptor(backupId, "name", DateTimeOffset.UtcNow, 20, 2, true, "f-manifest");

        await service.DeleteAsync(descriptor, TestContext.Current.CancellationToken);

        Assert.Equal([
            "/api/cloud/files/f-part-1", "/api/cloud/files/f-part-2", "/api/cloud/files/f-manifest"
        ], operations);
    }

    [Fact]
    public async Task DeleteAsync_WhenAPartDeleteFails_KeepsTheManifestSoTheBackupStaysVisible()
    {
        var operations = new List<string>();
        var backupId = "20260830-120000-aaaa0001";
        var handler = new RecordingHandler(request =>
        {
            if (request.Method == HttpMethod.Delete)
            {
                operations.Add(request.RequestUri!.AbsolutePath);
                return request.RequestUri.AbsolutePath.EndsWith("f-part-1", StringComparison.Ordinal)
                    ? Json("{\"error\":\"internal_error\",\"error_description\":\"boom\"}", HttpStatusCode.InternalServerError)
                    : Json("{\"success\":true}");
            }

            return Json(BuildListResponse(
                ("f-manifest", CloudBackupPackage.BuildManifestName(backupId), 10),
                ("f-part-1", CloudBackupPackage.BuildPartName(backupId, 1, 2), 10),
                ("f-part-2", CloudBackupPackage.BuildPartName(backupId, 2, 2), 10)));
        });
        var service = CreateService(handler);
        var descriptor = new CloudBackupDescriptor(backupId, "name", DateTimeOffset.UtcNow, 20, 2, true, "f-manifest");

        var exception = await Assert.ThrowsAsync<SectlCloudStorageException>(() =>
            service.DeleteAsync(descriptor, TestContext.Current.CancellationToken));

        Assert.Equal("delete_failed", exception.Code);
        Assert.DoesNotContain("/api/cloud/files/f-manifest", operations);
    }

    [Fact]
    public async Task PurgeIncompleteAsync_RemovesOnlyBackupsWithoutTheirManifest()
    {
        var deleted = new List<string>();
        var completeId = "20260830-120000-aaaa0001";
        var incompleteId = "20260830-130000-bbbb0002";
        var handler = new RecordingHandler(request =>
        {
            if (request.Method == HttpMethod.Delete)
            {
                deleted.Add(request.RequestUri!.AbsolutePath);
                return Json("{\"success\":true}");
            }

            return Json(BuildListResponse(
                ("f-manifest", CloudBackupPackage.BuildManifestName(completeId), 10),
                ("f-part-1", CloudBackupPackage.BuildPartName(completeId, 1, 1), 10),
                ("f-orphan", CloudBackupPackage.BuildPartName(incompleteId, 1, 2), 10)));
        });
        var service = CreateService(handler);

        var removed = await service.PurgeIncompleteAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, removed);
        Assert.Equal(["/api/cloud/files/f-orphan"], deleted);
    }

    [Fact]
    public async Task UploadAutomaticAsync_WhenTheQuotaIsExhausted_DeletesTheOldestBackupAndRetries()
    {
        var deleted = new List<string>();
        var files = new List<(string FileId, string FileName, long Size)>();
        files.AddRange(BuildBackupFiles("20260801-120000-aaaa0001"));
        files.AddRange(BuildBackupFiles("20260820-120000-bbbb0002"));
        var uploads = 0;
        var handler = new RecordingHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/upload", StringComparison.Ordinal))
            {
                uploads++;
                return uploads == 1
                    ? Json("{\"error\":\"storage_exceeded\",\"error_description\":\"Storage quota exceeded\"}",
                        HttpStatusCode.RequestEntityTooLarge)
                    : Json($"{{\"success\":true,\"file_id\":\"new-{uploads}\",\"filename\":\"part.srpart\",\"size\":1}}");
            }

            if (request.Method == HttpMethod.Delete)
            {
                deleted.Add(path);
                files.RemoveAll(file => path.EndsWith(file.FileId, StringComparison.Ordinal));
                return Json("{\"success\":true}");
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/api/cloud/files", StringComparison.Ordinal))
                return Json(BuildListResponse(files.ToArray()));

            throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
        });
        var service = CreateService(handler, CreateArchiveBytes(CloudBackupPackage.DefaultPartBytes));

        var descriptor = await service.UploadAutomaticAsync(0, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(descriptor.IsComplete);
        Assert.Equal(3, uploads);
        Assert.Equal([
            "/api/cloud/files/f-20260801-120000-aaaa0001-p1",
            "/api/cloud/files/f-20260801-120000-aaaa0001-m"
        ], deleted);
        Assert.Equal(2, files.Count);
        Assert.All(files, file => Assert.Contains("20260820-120000-bbbb0002", file.FileId));
    }

    [Fact]
    public async Task UploadAutomaticAsync_WhenTheQuotaIsExhaustedAndNothingIsLeft_SurfacesTheQuotaError()
    {
        var handler = new RecordingHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/upload", StringComparison.Ordinal))
                return Json("{\"error\":\"storage_exceeded\",\"error_description\":\"Storage quota exceeded\"}",
                    HttpStatusCode.RequestEntityTooLarge);
            if (request.Method == HttpMethod.Get)
                return Json(BuildListResponse());
            throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
        });
        var service = CreateService(handler, CreateArchiveBytes(CloudBackupPackage.DefaultPartBytes));

        var exception = await Assert.ThrowsAsync<SectlCloudStorageException>(() =>
            service.UploadAutomaticAsync(0, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("storage_exceeded", exception.Code);
    }

    [Fact]
    public async Task UploadAutomaticAsync_WhenBackupsExceedTheRetentionLimit_DeletesTheOldestOnes()
    {
        var deleted = new List<string>();
        var files = new List<(string FileId, string FileName, long Size)>();
        files.AddRange(BuildBackupFiles("20260801-120000-aaaa0001"));
        files.AddRange(BuildBackupFiles("20260810-120000-bbbb0002"));
        files.AddRange(BuildBackupFiles("20260820-120000-cccc0003"));
        var handler = new RecordingHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/upload", StringComparison.Ordinal))
                return Json("{\"success\":true,\"file_id\":\"new-1\",\"filename\":\"part.srpart\",\"size\":1}");
            if (request.Method == HttpMethod.Delete)
            {
                deleted.Add(path);
                files.RemoveAll(file => path.EndsWith(file.FileId, StringComparison.Ordinal));
                return Json("{\"success\":true}");
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/api/cloud/files", StringComparison.Ordinal))
                return Json(BuildListResponse(files.ToArray()));

            throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
        });
        var service = CreateService(handler, CreateArchiveBytes(CloudBackupPackage.DefaultPartBytes));

        await service.UploadAutomaticAsync(1, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([
            "/api/cloud/files/f-20260810-120000-bbbb0002-p1",
            "/api/cloud/files/f-20260810-120000-bbbb0002-m",
            "/api/cloud/files/f-20260801-120000-aaaa0001-p1",
            "/api/cloud/files/f-20260801-120000-aaaa0001-m"
        ], deleted);
        Assert.Equal(2, files.Count);
        Assert.All(files, file => Assert.Contains("20260820-120000-cccc0003", file.FileId));
    }

    [Fact]
    public async Task UploadAutomaticAsync_WhenItSucceeds_AnnouncesTheUpload()
    {
        var announced = 0;
        var handler = new RecordingHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/upload", StringComparison.Ordinal))
                return Json("{\"success\":true,\"file_id\":\"new-1\",\"filename\":\"part.srpart\",\"size\":1}");
            if (request.Method == HttpMethod.Get && path.EndsWith("/api/cloud/files", StringComparison.Ordinal))
                return Json(BuildListResponse());
            throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
        });
        var service = CreateService(handler, CreateArchiveBytes(CloudBackupPackage.DefaultPartBytes));
        service.AutomaticBackupUploaded += (_, _) => announced++;

        await service.UploadAutomaticAsync(0, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, announced);
    }

    public void Dispose()
    {
        ResetDataRootForTests();
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    private CloudBackupService CreateService(RecordingHandler handler, byte[]? archive = null)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://appwrite.sectl.cn/") };
        var factory = new StubHttpClientFactory(httpClient);
        var configHandler = new MainConfigHandler(
            NullLogger<MainConfigHandler>.Instance,
            new TestConfigService(new MainConfigModel()));
        var deviceUuidStore = new DeviceUuidStore(configHandler, NullLogger<DeviceUuidStore>.Instance);
        var authService = new SectlAuthService(factory, deviceUuidStore);
        SetToken(authService, new SectlToken("access-token", "refresh-token", "user-1", 3600));
        var cloudClient = new SectlCloudStorageClient(authService, factory, NullLogger<SectlCloudStorageClient>.Instance);
        return new CloudBackupService(authService, cloudClient, new FakeImportExportService(archive ?? []),
            NullLogger<CloudBackupService>.Instance);
    }

    private static HttpResponseMessage Download(HttpRequestMessage request, IReadOnlyDictionary<string, byte[]> content)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path.EndsWith("/download", StringComparison.Ordinal))
        {
            var fileId = path.Split('/')[4];
            return Json($"{{\"download_url\":\"https://storage.example/{fileId}\"}}");
        }

        var identifier = request.RequestUri.AbsolutePath.TrimStart('/');
        return content.TryGetValue(identifier, out var bytes)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
            : new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static string ReadUploadedName(HttpRequestMessage request)
    {
        var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        return JsonNode.Parse(body)!["file"]!["name"]!.GetValue<string>();
    }

    private static string BuildListResponse(params (string FileId, string FileName, long Size)[] files)
    {
        var payload = new JsonObject
        {
            ["files"] = new JsonArray(files.Select(file => new JsonObject
            {
                ["file_id"] = file.FileId,
                ["filename"] = file.FileName,
                ["size"] = file.Size,
                ["created_at"] = "2026-08-30T12:00:00Z"
            }).ToArray<JsonNode?>()),
            ["has_more"] = false
        };
        return payload.ToJsonString();
    }

    /// <summary>One complete cloud backup as the listing reports it: a single part plus its manifest.</summary>
    private static IEnumerable<(string FileId, string FileName, long Size)> BuildBackupFiles(string backupId) =>
    [
        ($"f-{backupId}-p1", CloudBackupPackage.BuildPartName(backupId, 1, 1), 10),
        ($"f-{backupId}-m", CloudBackupPackage.BuildManifestName(backupId), 10)
    ];

    private static void SetToken(SectlAuthService service, SectlToken token)
    {
        var field = typeof(SectlAuthService).GetField("_token", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(service, token);
    }

    private static byte[] CreateArchiveBytes(int length)
    {
        var bytes = new byte[length];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return bytes;
    }

    private static void ConfigureDataRootForTests(string dataRoot)
    {
        GetUtilsMethod("ConfigureDataRoot").Invoke(null, [dataRoot]);
    }

    private static void ResetDataRootForTests()
    {
        GetUtilsMethod("ResetDataRootForTests").Invoke(null, null);
    }

    private static MethodInfo GetUtilsMethod(string name)
    {
        return typeof(Utils).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
               ?? throw new InvalidOperationException($"Utils.{name} was not found.");
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class ProgressRecorder(List<CloudBackupProgress> progress) : IProgress<CloudBackupProgress>
    {
        public void Report(CloudBackupProgress value) => progress.Add(value);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        public string? Files { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Files is not null
                && request.Method == HttpMethod.Get
                && request.RequestUri!.AbsolutePath.EndsWith("/api/cloud/files", StringComparison.Ordinal))
                return Task.FromResult(Json(Files));

            return Task.FromResult(send(request));
        }
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class FakeImportExportService(byte[] archive) : IImportExportService
    {
        public IReadOnlyList<string> GetCloudBackupRoots() => DataArchiveService.CloudBackupRoots;

        public Task<string> ExportCloudBackupAsync(string destinationPath, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.WriteAllBytes(destinationPath, archive);
            return Task.FromResult(destinationPath);
        }

        public Task<string> ExportDiagnosticAsync(string destinationPath, bool includeExtendedData = false,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<string> ExportSettingsAsync(string destinationPath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string> ExportAllDataAsync(string destinationPath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ImportInspection> InspectSettingsAsync(string sourcePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ImportInspection> InspectAllDataAsync(string sourcePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ImportInspection> InspectCloudBackupAsync(string sourcePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ImportResult> ImportSettingsAsync(string sourcePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ImportResult> ImportAllDataAsync(string sourcePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ImportResult> ImportCloudBackupAsync(string sourcePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public string CreateManualBackup(IReadOnlyCollection<string> roots) => throw new NotSupportedException();

        public string CreateAutomaticBackup(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ImportResult> RestoreBackupAsync(string sourcePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class TestConfigService(MainConfigModel config) : ConfigServiceBase
    {
        public override bool IsConfigExists<T>(T fallback) => true;
        public override T LoadConfig<T>(T fallback) => config is T typed ? typed : fallback;
        public override void SaveConfig<T>(T value) { }
        public override void DeleteConfig<T>(T value) { }
    }
}
