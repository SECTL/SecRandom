using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using QRCoder;

namespace SecRandom.Services.RosterTransfer;

/// <summary>
/// Local-only QR codec for settings and full-data exports. It uses its own frame prefix so the
/// legacy roster QR protocol remains readable without ambiguity.
/// </summary>
public sealed class SettingsTransferQrService
{
    private const string ManifestPrefix = "SRDQR1M";
    private const string DataPrefix = "SRDQR1D";
    private const int FramePayloadLength = 128;
    private const int MaximumFrameCount = 4096;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<SettingsTransferQrExportSession> CreateExportSessionAsync(SyncTransferPackage package,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (package.ContentType is not (SyncTransferContentType.Settings or SyncTransferContentType.AllData))
            throw new InvalidDataException("Offline QR transfer content type is not supported.");
        SyncTransferLimits.EnsureOfflineQrPayloadSize(package.Content.LongLength, "source file");

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
            var manifest = string.Join('|', ManifestPrefix, sessionId,
                Base64Url(Encoding.UTF8.GetBytes(RosterSyncTransferService.ContentTypeName(package.ContentType))),
                Base64Url(Encoding.UTF8.GetBytes(package.FileName)), compressed.Length, chunks.Count, checksum);

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

    public SettingsTransferQrImportAccumulator CreateImportAccumulator() => new();

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
        private readonly Dictionary<int, byte[]> _chunks = [];
        private string? _sessionId;
        private string? _checksum;
        private int _payloadLength;
        private int _totalFrames;
        private int _duplicateFrames;
        private int _rejectedFrames;
        private DateTimeOffset _startedAt;

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
            if (parts[0] == ManifestPrefix && parts.Length == 7 && int.TryParse(parts[4], out var length) &&
                int.TryParse(parts[5], out var count) && length is > 0 and <= SyncTransferLimits.MaxOfflineQrPayloadBytes &&
                count is > 0 and <= MaximumFrameCount && parts[6].Length == 64)
            {
                frame = new Frame(true, parts[1], 0, length, count, parts[6], null);
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
                frame = new Frame(false, parts[1], index, payloadLength, 0, parts[5], chunk);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }

    private sealed record OfflineTransferEnvelope(int Version, string ContentType, string FileName, byte[] Content);
    private readonly record struct Frame(bool IsManifest, string SessionId, int Index, int PayloadLength, int TotalFrames, string Checksum, byte[]? Chunk);
}

public sealed record SettingsTransferQrExportSession(IReadOnlyList<byte[]> Frames);
public enum SettingsTransferQrFrameImportResult { Accepted, Duplicate, Rejected }
