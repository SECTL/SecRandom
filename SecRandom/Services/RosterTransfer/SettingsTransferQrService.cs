using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using QRCoder;
using SecRandom.Services.Configuration;

namespace SecRandom.Services.RosterTransfer;

/// <summary>
/// Local-only QR codec for settings and full-data exports. It uses its own frame prefix so the
/// roster QR protocol remains distinguishable.
/// </summary>
public sealed class SettingsTransferQrService
{
    private const string ManifestPrefix = "SRDQR2M";
    private const string DataPrefix = "SRDQR2D";
    private const int FramePayloadLength = 128;
    private const int MaximumFrameCount = 4096;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string? _apiKey;

    public SettingsTransferQrService(string? apiKey = null) => _apiKey = apiKey;

    public Task<SettingsTransferQrExportSession> CreateExportSessionAsync(SyncTransferPackage package,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (package.ContentType is not (SyncTransferContentType.Settings
                or SyncTransferContentType.AllData
                or SyncTransferContentType.TextLibrary))
            throw new InvalidDataException("Offline QR transfer content type is not supported.");
        SyncTransferLimits.EnsureOfflineQrPayloadSize(package.Content.LongLength, "source file");
        var apiKey = GetAuthenticationKey();

        return Task.Run(() =>
        {
            var serialized = JsonSerializer.SerializeToUtf8Bytes(new OfflineTransferEnvelope(1,
                RosterSyncTransferService.ContentTypeName(package.ContentType), package.FileName, package.Content), JsonOptions);
            var compressed = Compress(serialized);
            SyncTransferLimits.EnsureOfflineQrPayloadSize(compressed.LongLength, "offline QR package");
            var chunks = Split(compressed);
            if (chunks.Count > MaximumFrameCount)
                throw new InvalidDataException("Offline QR transfer would require too many frames.");

            var sessionId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            var checksum = Convert.ToHexString(SHA256.HashData(compressed));
            var contentType = Base64Url(Encoding.UTF8.GetBytes(RosterSyncTransferService.ContentTypeName(package.ContentType)));
            var fileName = Base64Url(Encoding.UTF8.GetBytes(package.FileName));
            var authenticationTag = CreateManifestAuthenticationTag(apiKey, sessionId, contentType, fileName,
                compressed.Length, chunks.Count, checksum);
            var manifest = string.Join('|', ManifestPrefix, sessionId,
                contentType, fileName, compressed.Length, chunks.Count, checksum, authenticationTag);

            using var generator = new QRCodeGenerator();
            var frameTexts = chunks.Select((chunk, index) => string.Join('|', DataPrefix, sessionId, index,
                index * FramePayloadLength, compressed.Length, checksum, Convert.ToBase64String(Pad(chunk)))).ToList();
            var version = 0;
            foreach (var text in frameTexts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
                version = Math.Max(version, data.Version);
            }

            var frames = new List<byte[]>(chunks.Count + 1);
            using (var manifestData = generator.CreateQrCode(manifest, QRCodeGenerator.ECCLevel.M, requestedVersion: version))
                frames.Add(new PngByteQRCode(manifestData).GetGraphic(8));
            foreach (var text in frameTexts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M, requestedVersion: version);
                frames.Add(new PngByteQRCode(data).GetGraphic(8));
            }
            return new SettingsTransferQrExportSession(frames);
        }, cancellationToken);
    }

    public SettingsTransferQrImportAccumulator CreateImportAccumulator() => new(GetAuthenticationKey());

    private byte[] GetAuthenticationKey()
    {
        var apiKey = _apiKey ?? ApiEndpointKeys.OfflineQr;
        if (apiKey is not { Length: >= 43 and <= 128 } || apiKey.Any(character =>
                character is not (>= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')))
            throw new InvalidOperationException("SECRANDOM_OFFLINE_QR_API_KEY must be configured as a 43-128 character base64url key.");

        return Encoding.ASCII.GetBytes(apiKey);
    }

    private static string CreateManifestAuthenticationTag(byte[] key, string sessionId, string contentType,
        string fileName, int payloadLength, int totalFrames, string checksum)
    {
        var data = string.Join('\n', "secrandom-offline-qr/v2", sessionId, contentType, fileName,
            payloadLength.ToString(System.Globalization.CultureInfo.InvariantCulture),
            totalFrames.ToString(System.Globalization.CultureInfo.InvariantCulture), checksum);
        return Base64Url(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(data)));
    }

    private static List<byte[]> Split(byte[] value)
    {
        var chunks = new List<byte[]>(Math.Max(1, (value.Length + FramePayloadLength - 1) / FramePayloadLength));
        for (var offset = 0; offset < value.Length; offset += FramePayloadLength)
            chunks.Add(value[offset..Math.Min(value.Length, offset + FramePayloadLength)]);
        if (chunks.Count == 0)
            chunks.Add([]);
        return chunks;
    }

    private static byte[] Pad(byte[] value)
    {
        var padded = new byte[FramePayloadLength];
        Buffer.BlockCopy(value, 0, padded, 0, value.Length);
        return padded;
    }

    private static byte[] Compress(byte[] value)
    {
        using var output = new MemoryStream();
        using (var stream = new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true))
            stream.Write(value);
        return output.ToArray();
    }

    private static byte[] Decompress(byte[] value)
    {
        using var input = new MemoryStream(value);
        using var stream = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (output.Length + read > SyncTransferLimits.MaxOfflineQrPayloadBytes * 2L)
                throw new InvalidDataException("Offline QR transfer expands beyond its safety limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] FromBase64Url(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/')
        .PadRight(value.Length + (4 - value.Length % 4) % 4, '='));

    public sealed class SettingsTransferQrImportAccumulator
    {
        private readonly byte[] _authenticationKey;
        private readonly Dictionary<int, byte[]> _chunks = [];
        private string? _sessionId;
        private string? _checksum;
        private int _payloadLength;
        private int _totalFrames;
        private int _duplicateFrames;
        private int _rejectedFrames;
        private DateTimeOffset _startedAt;

        internal SettingsTransferQrImportAccumulator(byte[] authenticationKey) => _authenticationKey = authenticationKey;

        public int AcceptedFrames => _chunks.Count;
        public int TotalFrames => _totalFrames;
        public int PayloadLength => _payloadLength;
        public int DuplicateFrames => _duplicateFrames;
        public int RejectedFrames => _rejectedFrames;
        public DateTimeOffset StartedAt => _startedAt;
        public string? SessionId => _sessionId;
        public bool IsComplete => _totalFrames > 0 && _chunks.Count == _totalFrames;
        public SettingsTransferQrFrameImportResult Add(string? qrText)
        {
            if (!TryParse(qrText, out var frame))
            {
                _rejectedFrames++;
                return SettingsTransferQrFrameImportResult.Rejected;
            }

            if (frame.IsManifest)
            {
                if (frame.ContentType is null || frame.FileName is null || frame.AuthenticationTag is null ||
                    !VerifyManifestAuthenticationTag(frame))
                {
                    _rejectedFrames++;
                    return SettingsTransferQrFrameImportResult.Rejected;
                }

                if (_sessionId == frame.SessionId)
                {
                    _duplicateFrames++;
                    return SettingsTransferQrFrameImportResult.Duplicate;
                }

                _chunks.Clear();
                _sessionId = frame.SessionId;
                _checksum = frame.Checksum;
                _payloadLength = frame.PayloadLength;
                _totalFrames = frame.TotalFrames;
                _duplicateFrames = 0;
                _rejectedFrames = 0;
                _startedAt = DateTimeOffset.UtcNow;
                return SettingsTransferQrFrameImportResult.Accepted;
            }

            if (_sessionId != frame.SessionId || _checksum != frame.Checksum || _payloadLength != frame.PayloadLength ||
                frame.Index < 0 || frame.Index >= _totalFrames || frame.Chunk is null)
            {
                _rejectedFrames++;
                return SettingsTransferQrFrameImportResult.Rejected;
            }

            if (_chunks.TryAdd(frame.Index, frame.Chunk))
                return SettingsTransferQrFrameImportResult.Accepted;

            _duplicateFrames++;
            return SettingsTransferQrFrameImportResult.Duplicate;
        }
        public SyncTransferPackage GetCompletedPackage()
        {
            if (!IsComplete || _checksum is null)
                throw new InvalidOperationException("Offline QR transfer is incomplete.");
            var padded = Enumerable.Range(0, _totalFrames).Select(index => _chunks.TryGetValue(index, out var value)
                ? value : throw new InvalidDataException("Offline QR transfer is missing a frame.")).SelectMany(value => value).ToArray();
            if (padded.Length < _payloadLength)
                throw new InvalidDataException("Offline QR transfer integrity check failed.");
            var compressed = padded[.._payloadLength];
            SyncTransferLimits.EnsureOfflineQrPayloadSize(compressed.LongLength);
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(_checksum), SHA256.HashData(compressed)))
                throw new InvalidDataException("Offline QR transfer integrity check failed.");
            var envelope = JsonSerializer.Deserialize<OfflineTransferEnvelope>(Decompress(compressed), JsonOptions)
                ?? throw new InvalidDataException("Offline QR transfer content is invalid.");
            if (envelope.Version != 1)
                throw new InvalidDataException("Offline QR transfer content is not supported.");
            SyncTransferLimits.EnsureOfflineQrPayloadSize(envelope.Content.LongLength, "offline QR source file");
            var contentType = envelope.ContentType switch
            {
                "settings" => SyncTransferContentType.Settings,
                "all-data" => SyncTransferContentType.AllData,
                "text-library" => SyncTransferContentType.TextLibrary,
                _ => throw new InvalidDataException("Offline QR transfer content type is not supported.")
            };
            return new SyncTransferPackage(contentType, envelope.FileName, envelope.Content);
        }

        private static bool TryParse(string? value, out Frame frame)
        {
            frame = default;
            var parts = value?.Split('|', StringSplitOptions.None);
            if (parts is null || parts.Length < 7 || parts[1].Length != 32)
                return false;
            if (parts[0] == ManifestPrefix && parts.Length == 8 && int.TryParse(parts[4], out var length) &&
                int.TryParse(parts[5], out var count) && length is > 0 and <= SyncTransferLimits.MaxOfflineQrPayloadBytes &&
                count is > 0 and <= MaximumFrameCount && parts[6].Length == 64 && IsBase64Url(parts[7], 43))
            {
                frame = new Frame(true, parts[1], 0, length, count, parts[6], null,
                    parts[2], parts[3], parts[7]);
                return true;
            }
            if (parts[0] != DataPrefix || parts.Length != 7 || !int.TryParse(parts[2], out var index) ||
                !int.TryParse(parts[3], out var offset) || !int.TryParse(parts[4], out var payloadLength) ||
                index < 0 || offset != index * FramePayloadLength || offset >= payloadLength ||
                payloadLength is <= 0 or > SyncTransferLimits.MaxOfflineQrPayloadBytes ||
                parts[5].Length != 64)
                return false;
            try
            {
                var chunk = Convert.FromBase64String(parts[6]);
                if (chunk.Length != FramePayloadLength)
                    return false;
                frame = new Frame(false, parts[1], index, payloadLength, 0, parts[5], chunk, null, null, null);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private bool VerifyManifestAuthenticationTag(Frame frame)
        {
            if (!TryFromBase64Url(frame.AuthenticationTag!, out var suppliedTag))
                return false;

            var expectedTag = HMACSHA256.HashData(_authenticationKey,
                Encoding.UTF8.GetBytes(string.Join('\n', "secrandom-offline-qr/v2", frame.SessionId,
                    frame.ContentType, frame.FileName,
                    frame.PayloadLength.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    frame.TotalFrames.ToString(System.Globalization.CultureInfo.InvariantCulture), frame.Checksum)));
            return CryptographicOperations.FixedTimeEquals(expectedTag, suppliedTag);
        }

        private static bool IsBase64Url(string value, int expectedLength) => value.Length == expectedLength && value.All(character =>
            character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_');

        private static bool TryFromBase64Url(string value, out byte[] bytes)
        {
            try
            {
                bytes = FromBase64Url(value);
                return true;
            }
            catch (FormatException)
            {
                bytes = [];
                return false;
            }
        }
    }

    private sealed record OfflineTransferEnvelope(int Version, string ContentType, string FileName, byte[] Content);
    private readonly record struct Frame(bool IsManifest, string SessionId, int Index, int PayloadLength, int TotalFrames,
        string Checksum, byte[]? Chunk, string? ContentType, string? FileName, string? AuthenticationTag);
}

public sealed record SettingsTransferQrExportSession(IReadOnlyList<byte[]> Frames);
public enum SettingsTransferQrFrameImportResult { Accepted, Duplicate, Rejected }
