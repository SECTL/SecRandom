using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiniExcelLibs;
using QRCoder;

namespace SecRandom.Services.RosterTransfer;

public enum RosterCloudTransferMode
{
    QuickQr,
    OfflineQr,
    SessionCode
}

public sealed record RosterCloudTransferInfo(
    RosterCloudTransferMode Mode,
    string? TransferId,
    string? UploadToken,
    string? PairingUrl,
    string? SessionCode,
    DateTimeOffset ExpiresAt,
    long PayloadBytes,
    int RecordCount);

public sealed record RosterSyncImportResult(RosterTransferDocument Document, string FileName);

/// <summary>
/// Encrypts device-transfer archives locally and uploads only the encrypted archive to SecRandom Sync.
/// </summary>
public sealed class RosterSyncTransferService(HttpClient httpClient)
{
    public const string PublicUrl = "https://secrandom-sync.sectl.cn";
    private const int SessionCodeLength = 12;
    private const int Pbkdf2Iterations = 600_000;
    private const string SessionAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<RosterCloudTransferInfo> CreateAsync(
        RosterTransferDocument document,
        IReadOnlyList<Dictionary<string, object?>> fileRows,
        RosterCloudTransferMode mode,
        SyncTransferContentType contentType = SyncTransferContentType.Roster,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(fileRows);
        if (mode == RosterCloudTransferMode.OfflineQr)
            throw new ArgumentException("Offline QR transfer is local to the desktop client", nameof(mode));

        if (contentType != SyncTransferContentType.Roster)
            throw new ArgumentOutOfRangeException(nameof(contentType), "Roster exports must use the roster content type.");

        var archive = await Task.Run(() => CreateRosterArchive(document, fileRows), cancellationToken);
        return await CreateEncryptedTransferAsync(archive, mode, contentType, document.Rows.Count, cancellationToken);
    }

    /// <summary>
    /// Creates a transfer for a settings or full-data file. The nested package manifest keeps
    /// the content type explicit without changing the legacy roster archive format.
    /// </summary>
    public async Task<RosterCloudTransferInfo> CreateFileAsync(
        SyncTransferPackage package,
        RosterCloudTransferMode mode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (package.ContentType == SyncTransferContentType.Roster)
            throw new ArgumentOutOfRangeException(nameof(package), "Roster transfers use the legacy roster export path.");
        if (mode == RosterCloudTransferMode.OfflineQr)
            throw new ArgumentException("Offline QR transfer is local to the desktop client", nameof(mode));

        SyncTransferLimits.EnsurePayloadSize(package.Content.LongLength, "source file");
        var archive = await Task.Run(() => CreateFileArchive(package), cancellationToken);
        return await CreateEncryptedTransferAsync(archive, mode, package.ContentType, 0, cancellationToken);
    }

    private async Task<RosterCloudTransferInfo> CreateEncryptedTransferAsync(
        byte[] archive,
        RosterCloudTransferMode mode,
        SyncTransferContentType contentType,
        int recordCount,
        CancellationToken cancellationToken)
    {
        SyncTransferLimits.EnsurePayloadSize(archive.LongLength, "transfer archive");
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var keyMaterial = mode == RosterCloudTransferMode.QuickQr
            ? RandomNumberGenerator.GetBytes(32)
            : Encoding.UTF8.GetBytes(CreateSessionCode());
        var normalizedSessionCode = mode == RosterCloudTransferMode.SessionCode
            ? Encoding.UTF8.GetString(keyMaterial)
            : null;
        var key = mode == RosterCloudTransferMode.QuickQr
            ? keyMaterial
            : Rfc2898DeriveBytes.Pbkdf2(keyMaterial, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, 32);
        var ciphertext = new byte[archive.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(key, tag.Length))
            aes.Encrypt(nonce, archive, ciphertext, tag);

        var envelope = new RosterSyncEnvelope(
            "secrandom-sync-envelope/v1",
            "aes-256-gcm",
            mode == RosterCloudTransferMode.QuickQr ? "raw" : "pbkdf2-sha256",
            mode == RosterCloudTransferMode.QuickQr ? null : Pbkdf2Iterations,
            Base64Url(salt),
            Base64Url(nonce),
            Base64Url(tag),
            Base64Url(ciphertext));
        var payload = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
        SyncTransferLimits.EnsurePayloadSize(payload.LongLength, "encrypted transfer");
        var sessionCodeHash = normalizedSessionCode is null ? null : Sha256Hex(normalizedSessionCode);
        var createRequest = new RosterSyncCreateRequest(
            mode == RosterCloudTransferMode.QuickQr ? "quick" : "session",
            payload.LongLength,
            Convert.ToHexString(SHA256.HashData(payload)),
            sessionCodeHash,
            ContentTypeName(contentType));
        using var createResponse = await httpClient.PostAsJsonAsync("v1/transfers", createRequest, JsonOptions, cancellationToken);
        await EnsureSuccessAsync(createResponse, cancellationToken);
        var created = await createResponse.Content.ReadFromJsonAsync<RosterSyncCreateResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Sync service returned an empty transfer response");

        try
        {
            using var payloadContent = new ByteArrayContent(payload);
            payloadContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            payloadContent.Headers.ContentLength = payload.LongLength;
            using var uploadRequest = new HttpRequestMessage(HttpMethod.Put, $"v1/transfers/{Uri.EscapeDataString(created.Id)}/payload")
            {
                Content = payloadContent
            };
            uploadRequest.Headers.Add("X-SecRandom-Upload-Token", created.UploadToken);
            using var uploadResponse = await httpClient.SendAsync(uploadRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            await EnsureSuccessAsync(uploadResponse, cancellationToken);
        }
        catch
        {
            var incompleteTransfer = new RosterCloudTransferInfo(mode, created.Id, created.UploadToken, null, null,
                created.ExpiresAt, payload.LongLength, recordCount);
            try
            {
                await RevokeAsync(incompleteTransfer, CancellationToken.None);
            }
            catch
            {
                // The service retains its short expiry as the last-resort cleanup path.
            }
            throw;
        }

        var pairingUrl = mode == RosterCloudTransferMode.QuickQr
            ? $"{PublicUrl}/#t={Uri.EscapeDataString(created.Id)}&k={Uri.EscapeDataString(Base64Url(keyMaterial))}"
            : null;
        return new RosterCloudTransferInfo(mode, created.Id, created.UploadToken, pairingUrl, normalizedSessionCode,
            created.ExpiresAt, payload.LongLength, recordCount);
    }

    public async Task RevokeAsync(RosterCloudTransferInfo transfer, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(transfer.TransferId) || string.IsNullOrWhiteSpace(transfer.UploadToken))
            return;
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"v1/transfers/{Uri.EscapeDataString(transfer.TransferId)}");
        request.Headers.Add("X-SecRandom-Upload-Token", transfer.UploadToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
            await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<RosterSyncImportResult> ImportQuickAsync(string pairingUrl, CancellationToken cancellationToken = default)
    {
        if (!TryParseQuickPairing(pairingUrl, out var transferId, out var key))
            throw new InvalidDataException("The QR code is not a SecRandom quick-transfer pairing code.");

        return DecryptRosterArchive(await DownloadEnvelopeAsync(transferId, cancellationToken), key);
    }

    public async Task<RosterSyncImportResult> ImportSessionAsync(string sessionCode, CancellationToken cancellationToken = default)
    {
        var normalizedCode = NormalizeSessionCode(sessionCode);
        if (normalizedCode.Length != SessionCodeLength)
            throw new InvalidDataException("The session code must contain 12 letters or digits.");

        using var response = await httpClient.PostAsJsonAsync("v1/sessions/resolve",
            new RosterSyncResolveSessionRequest(Sha256Hex(normalizedCode)), JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return DecryptRosterArchive(await ReadEnvelopeAsync(response.Content, cancellationToken),
            Encoding.UTF8.GetBytes(normalizedCode));
    }

    public async Task<SyncTransferImportResult> ImportQuickPackageAsync(string pairingUrl,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseQuickPairing(pairingUrl, out var transferId, out var key))
            throw new InvalidDataException("The QR code is not a SecRandom quick-transfer pairing code.");

        return DecryptFileArchive(await DownloadEnvelopeAsync(transferId, cancellationToken), key);
    }

    public async Task<SyncTransferImportResult> ImportSessionPackageAsync(string sessionCode,
        CancellationToken cancellationToken = default)
    {
        var normalizedCode = NormalizeSessionCode(sessionCode);
        if (normalizedCode.Length != SessionCodeLength)
            throw new InvalidDataException("The session code must contain 12 letters or digits.");

        using var response = await httpClient.PostAsJsonAsync("v1/sessions/resolve",
            new RosterSyncResolveSessionRequest(Sha256Hex(normalizedCode)), JsonOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return DecryptFileArchive(await ReadEnvelopeAsync(response.Content, cancellationToken),
            Encoding.UTF8.GetBytes(normalizedCode));
    }

    public static byte[] CreatePairingQrPng(string pairingUrl)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(pairingUrl, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(data).GetGraphic(8);
    }

    public static string NormalizeSessionCode(string? code) => new(code?.Where(char.IsLetterOrDigit)
        .Select(char.ToUpperInvariant).ToArray() ?? []);

    public static string FormatSessionCode(string code) => NormalizeSessionCode(code);

    public static string ContentTypeName(SyncTransferContentType contentType) => contentType switch
    {
        SyncTransferContentType.Roster => "roster",
        SyncTransferContentType.Settings => "settings",
        SyncTransferContentType.AllData => "all-data",
        _ => throw new ArgumentOutOfRangeException(nameof(contentType))
    };

    private static string CreateSessionCode()
    {
        Span<byte> random = stackalloc byte[SessionCodeLength];
        RandomNumberGenerator.Fill(random);
        Span<char> code = stackalloc char[SessionCodeLength];
        for (var index = 0; index < code.Length; index++)
            code[index] = SessionAlphabet[random[index] % SessionAlphabet.Length];
        return new string(code);
    }

    private static byte[] CreateRosterArchive(RosterTransferDocument document, IReadOnlyList<Dictionary<string, object?>> fileRows)
    {
        var baseName = Path.GetFileNameWithoutExtension(document.FileName);
        const string rosterSuffix = ".secrandom-roster";
        if (baseName.EndsWith(rosterSuffix, StringComparison.OrdinalIgnoreCase))
            baseName = baseName[..^rosterSuffix.Length];
        baseName = SanitizeFileName(string.IsNullOrWhiteSpace(baseName) ? "roster" : baseName);
        var xlsxPath = Path.Combine(Path.GetTempPath(), $"secrandom-sync-{Guid.NewGuid():N}.xlsx");
        try
        {
            MiniExcel.SaveAs(xlsxPath, fileRows);
            var xlsx = File.ReadAllBytes(xlsxPath);
            var csv = Encoding.UTF8.GetBytes(CreateCsv(fileRows));
            SyncTransferLimits.EnsurePayloadSize(xlsx.LongLength, "roster spreadsheet");
            SyncTransferLimits.EnsurePayloadSize(csv.LongLength, "roster CSV");
            using var output = new MemoryStream();
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                var xlsxEntry = archive.CreateEntry($"{baseName}.xlsx", CompressionLevel.Fastest);
                using (var stream = xlsxEntry.Open()) stream.Write(xlsx);
                var csvEntry = archive.CreateEntry($"{baseName}.csv", CompressionLevel.Fastest);
                using (var stream = csvEntry.Open()) stream.Write(csv);
                var documentEntry = archive.CreateEntry("secrandom-roster-transfer.json", CompressionLevel.Fastest);
                using (var stream = documentEntry.Open())
                    JsonSerializer.Serialize(stream, document, JsonOptions);
            }
            var result = output.ToArray();
            SyncTransferLimits.EnsurePayloadSize(result.LongLength, "roster transfer archive");
            return result;
        }
        finally
        {
            if (File.Exists(xlsxPath)) File.Delete(xlsxPath);
        }
    }

    private static string CreateCsv(IReadOnlyList<Dictionary<string, object?>> rows)
    {
        var headers = rows.FirstOrDefault()?.Keys.ToArray() ?? [];
        var lines = new List<string> { string.Join(',', headers.Select(EscapeCsv)) };
        lines.AddRange(rows.Select(row => string.Join(',', headers.Select(header => EscapeCsv(row.GetValueOrDefault(header)?.ToString() ?? string.Empty)))));
        return string.Join(Environment.NewLine, lines);
    }

    private static string EscapeCsv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

    private static byte[] CreateFileArchive(SyncTransferPackage package)
    {
        var fileName = SanitizeFileName(Path.GetFileName(package.FileName));
        if (string.IsNullOrWhiteSpace(fileName))
            throw new InvalidDataException("The transfer file name is invalid.");

        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var descriptor = new SyncTransferFileDescriptor(1, ContentTypeName(package.ContentType), fileName);
            var manifest = archive.CreateEntry("secrandom-data-transfer.json", CompressionLevel.Fastest);
            using (var stream = manifest.Open())
                JsonSerializer.Serialize(stream, descriptor, JsonOptions);

            var payload = archive.CreateEntry($"payload/{fileName}", CompressionLevel.Fastest);
            using var payloadStream = payload.Open();
            payloadStream.Write(package.Content);
        }

        var result = output.ToArray();
        SyncTransferLimits.EnsurePayloadSize(result.LongLength, "file transfer archive");
        return result;
    }

    private static RosterSyncImportResult DecryptRosterArchive(RosterSyncEnvelope envelope, byte[] keyMaterial)
    {
        var plaintext = DecryptArchivePayload(envelope, keyMaterial);
        using var input = new MemoryStream(plaintext, writable: false);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        var documentEntry = archive.GetEntry("secrandom-roster-transfer.json")
            ?? throw new InvalidDataException("The transfer archive does not contain roster data.");
        using var documentStream = documentEntry.Open();
        var document = JsonSerializer.Deserialize<RosterTransferDocument>(documentStream, JsonOptions)
            ?? throw new InvalidDataException("The transfer roster data is invalid.");
        if (document.Version != 1)
            throw new InvalidDataException("The transfer roster version is not supported.");
        return new RosterSyncImportResult(document, document.FileName);
    }

    private static SyncTransferImportResult DecryptFileArchive(RosterSyncEnvelope envelope, byte[] keyMaterial)
    {
        var plaintext = DecryptArchivePayload(envelope, keyMaterial);
        using var input = new MemoryStream(plaintext, writable: false);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        var entries = archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)).ToArray();
        if (entries.Length != 2)
            throw new InvalidDataException("The transfer archive contains unexpected entries.");

        var descriptorEntry = archive.GetEntry("secrandom-data-transfer.json")
            ?? throw new InvalidDataException("The transfer archive does not contain a data manifest.");
        using var descriptorStream = descriptorEntry.Open();
        var descriptor = JsonSerializer.Deserialize<SyncTransferFileDescriptor>(descriptorStream, JsonOptions)
            ?? throw new InvalidDataException("The transfer data manifest is invalid.");
        if (descriptor.Version != 1 || !TryParseContentType(descriptor.ContentType, out var contentType) ||
            contentType == SyncTransferContentType.Roster)
            throw new InvalidDataException("The transfer data type is not supported.");

        var fileName = SanitizeFileName(Path.GetFileName(descriptor.FileName));
        if (!string.Equals(fileName, descriptor.FileName, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(fileName))
            throw new InvalidDataException("The transfer file name is invalid.");
        var payloadEntry = archive.GetEntry($"payload/{fileName}")
            ?? throw new InvalidDataException("The transfer archive does not contain its file payload.");
        SyncTransferLimits.EnsurePayloadSize(payloadEntry.Length, "file transfer payload");
        using var payload = payloadEntry.Open();
        using var content = new MemoryStream();
        CopyWithLimit(payload, content, SyncTransferLimits.MaxPayloadBytes, "file transfer payload");
        return new SyncTransferImportResult(new SyncTransferPackage(contentType, fileName, content.ToArray()));
    }

    private static byte[] DecryptArchivePayload(RosterSyncEnvelope envelope, byte[] keyMaterial)
    {
        if (envelope.Format != "secrandom-sync-envelope/v1" || envelope.Encryption != "aes-256-gcm")
            throw new InvalidDataException("The transfer payload format is not supported.");

        var salt = FromBase64Url(envelope.Salt);
        var key = envelope.KeyDerivation switch
        {
            "raw" when keyMaterial.Length == 32 => keyMaterial,
            "pbkdf2-sha256" when envelope.Iterations is > 0 => Rfc2898DeriveBytes.Pbkdf2(
                keyMaterial, salt, envelope.Iterations.Value, HashAlgorithmName.SHA256, 32),
            _ => throw new InvalidDataException("The transfer key is not valid for this payload.")
        };
        var ciphertext = FromBase64Url(envelope.Ciphertext);
        SyncTransferLimits.EnsurePayloadSize(ciphertext.LongLength, "encrypted transfer");
        var plaintext = new byte[ciphertext.Length];
        using (var aes = new AesGcm(key, 16))
            aes.Decrypt(FromBase64Url(envelope.Nonce), ciphertext, FromBase64Url(envelope.Tag), plaintext);
        SyncTransferLimits.EnsurePayloadSize(plaintext.LongLength, "decrypted transfer");
        return plaintext;
    }

    private static bool TryParseQuickPairing(string value, out string transferId, out byte[] key)
    {
        transferId = string.Empty;
        key = [];
        if (!Uri.TryCreate(value, UriKind.Absolute, out var pairing) || string.IsNullOrWhiteSpace(pairing.Fragment))
            return false;
        var values = System.Web.HttpUtility.ParseQueryString(pairing.Fragment.TrimStart('#'));
        transferId = values["t"] ?? string.Empty;
        try
        {
            key = FromBase64Url(values["k"] ?? string.Empty);
            return transferId.Length == 24 && key.Length == 32;
        }
        catch (FormatException)
        {
            transferId = string.Empty;
            key = [];
            return false;
        }
    }

    private async Task<RosterSyncEnvelope> DownloadEnvelopeAsync(string transferId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync($"v1/transfers/{Uri.EscapeDataString(transferId)}",
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await ReadEnvelopeAsync(response.Content, cancellationToken);
    }

    private static async Task<RosterSyncEnvelope> ReadEnvelopeAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is { } length)
            SyncTransferLimits.EnsurePayloadSize(length, "sync response");

        var bytes = await ReadWithLimitAsync(content, SyncTransferLimits.MaxPayloadBytes, "sync response", cancellationToken);
        return JsonSerializer.Deserialize<RosterSyncEnvelope>(bytes, JsonOptions)
               ?? throw new InvalidDataException("Sync service returned an empty transfer payload.");
    }

    private static async Task<byte[]> ReadWithLimitAsync(HttpContent content, long maximumLength, string payloadName,
        CancellationToken cancellationToken)
    {
        await using var input = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (output.Length > maximumLength - read)
                throw new InvalidDataException($"The {payloadName} exceeds the 1 MiB transfer limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static void CopyWithLimit(Stream input, Stream output, long maximumLength, string payloadName)
    {
        var buffer = new byte[81920];
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (output.Length > maximumLength - read)
                throw new InvalidDataException($"The {payloadName} exceeds the 1 MiB transfer limit.");
            output.Write(buffer, 0, read);
        }
    }

    private static bool TryParseContentType(string value, out SyncTransferContentType contentType)
    {
        contentType = value switch
        {
            "roster" => SyncTransferContentType.Roster,
            "settings" => SyncTransferContentType.Settings,
            "all-data" => SyncTransferContentType.AllData,
            _ => default
        };
        return value is "roster" or "settings" or "all-data";
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
        return value.Trim().TrimEnd('.') is { Length: > 0 } safe ? safe : "roster";
    }
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] FromBase64Url(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/')
        .PadRight(value.Length + (4 - value.Length % 4) % 4, '='));
    private static string Sha256Hex(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException($"Sync service returned {(int)response.StatusCode}: {body}");
    }

    private sealed record RosterSyncEnvelope(string Format, string Encryption, string KeyDerivation, int? Iterations, string Salt, string Nonce, string Tag, string Ciphertext);

    private sealed record RosterSyncCreateRequest(string Mode, long PayloadLength, string PayloadSha256,
        string? SessionCodeHash, string? ContentType);
    private sealed record RosterSyncCreateResponse(string Id, string UploadToken, DateTimeOffset ExpiresAt);
    private sealed record RosterSyncResolveSessionRequest(string SessionCodeHash);
    private sealed record SyncTransferFileDescriptor(
        [property: JsonPropertyName("version")] int Version,
        [property: JsonPropertyName("content_type")] string ContentType,
        [property: JsonPropertyName("file_name")] string FileName);
}
