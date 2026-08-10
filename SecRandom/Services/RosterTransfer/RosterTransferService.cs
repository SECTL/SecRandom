using System.Collections.Generic;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using QRCoder;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using ZXing;
using ZXing.Common;

namespace SecRandom.Services.RosterTransfer;

/// <summary>
/// Creates a self-contained list-transfer document and splits it into independently decodable QR frames.
/// QR images are generated before playback begins so the UI timer only changes the displayed frame.
/// </summary>
public sealed class RosterTransferService
{
    private const string ManifestPrefix = "SRQR1M";
    private const string DataPrefix = "SRQR1D";
    // Keep offline QR modules large enough for a 256 px presentation surface.
    // Data frames are padded below so every frame uses the same QR matrix size.
    private const int FramePayloadLength = 128;
    private const int FrameIndexDigits = 4;
    private const int FrameOffsetDigits = 7;
    private const int MaximumFrameCount = 4096;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public Task<RosterQrExportSession> CreateExportSessionAsync(
        RosterTransferDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var compressedPayload = Compress(JsonSerializer.SerializeToUtf8Bytes(document, SerializerOptions));
            var chunks = Split(compressedPayload, FramePayloadLength);
            if (chunks.Count > MaximumFrameCount)
                throw new InvalidOperationException("名单过大，无法在合理数量的二维码中传输。");

            var sessionId = Guid.NewGuid().ToString("N");
            var checksum = Convert.ToHexString(SHA256.HashData(compressedPayload));
            var frames = new List<byte[]>(chunks.Count + 1);
            using var generator = new QRCodeGenerator();
            var manifest = string.Join('|', ManifestPrefix, sessionId,
                Base64UrlEncode(Encoding.UTF8.GetBytes(document.FileName)), compressedPayload.Length, chunks.Count, checksum);
            using (var manifestData = generator.CreateQrCode(manifest, QRCodeGenerator.ECCLevel.M))
                frames.Add(new PngByteQRCode(manifestData).GetGraphic(8));
            var dataContents = new List<string>(chunks.Count);
            for (var index = 0; index < chunks.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                dataContents.Add(string.Join('|', DataPrefix, sessionId,
                    index.ToString($"D{FrameIndexDigits}", CultureInfo.InvariantCulture),
                    (index * FramePayloadLength).ToString($"D{FrameOffsetDigits}", CultureInfo.InvariantCulture),
                    compressedPayload.Length, checksum, Convert.ToBase64String(PadFrame(chunks[index]))));
            }

            var dataFrameVersion = 0;
            foreach (var content in dataContents)
            {
                using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
                dataFrameVersion = Math.Max(dataFrameVersion, data.Version);
            }

            foreach (var content in dataContents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M,
                    requestedVersion: dataFrameVersion);
                frames.Add(new PngByteQRCode(data).GetGraphic(8));
            }

            return new RosterQrExportSession(sessionId, frames, chunks.Count, compressedPayload.Length, document.Rows.Count);
        }, cancellationToken);
    }

    public byte[] CreateExampleQrPng()
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode("扫啥呢，示例二维码而已，好奇心太重了", QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(data).GetGraphic(8);
    }

    public async Task<string?> DecodeQrTextAsync(Stream imageStream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageStream);
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var image = Image.Load<Rgba32>(imageStream);
            if (Math.Max(image.Width, image.Height) > 960)
                image.Mutate(context => context.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(960, 960)
                }));
            var pixels = new byte[image.Width * image.Height * 4];
            image.CopyPixelDataTo(pixels);
            var source = new RGBLuminanceSource(pixels, image.Width, image.Height,
                RGBLuminanceSource.BitmapFormat.RGBA32);
            var reader = new BarcodeReaderGeneric
            {
                AutoRotate = true,
                Options = new DecodingOptions
                {
                    TryHarder = true,
                    PossibleFormats = [BarcodeFormat.QR_CODE]
                }
            };
            return reader.Decode(source)?.Text;
        }, cancellationToken).ConfigureAwait(false);
    }

    public RosterQrImportAccumulator CreateImportAccumulator() => new();

    private static byte[] Compress(byte[] payload)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true))
            brotli.Write(payload);
        return output.ToArray();
    }

    private static byte[] Decompress(byte[] payload)
    {
        using var input = new MemoryStream(payload);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        brotli.CopyTo(output);
        return output.ToArray();
    }

    private static List<byte[]> Split(byte[] content, int length)
    {
        var chunks = new List<byte[]>(Math.Max(1, (content.Length + length - 1) / length));
        for (var offset = 0; offset < content.Length; offset += length)
            chunks.Add(content[offset..Math.Min(content.Length, offset + length)]);
        if (chunks.Count == 0)
            chunks.Add([]);
        return chunks;
    }

    private static byte[] PadFrame(byte[] chunk)
    {
        var padded = new byte[FramePayloadLength];
        Buffer.BlockCopy(chunk, 0, padded, 0, chunk.Length);
        return padded;
    }

    private static string Base64UrlEncode(byte[] value) => Convert.ToBase64String(value)
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        return Convert.FromBase64String(padded);
    }

    public sealed class RosterQrImportAccumulator
    {
        private readonly Dictionary<int, byte[]> _chunks = [];
        private string? _checksum;
        private string? _fileName;
        private string? _sessionId;
        private int _payloadLength;
        private int _totalFrames;
        private DateTimeOffset _startedAt;

        public int AcceptedFrames => _chunks.Count;
        public int DuplicateFrames { get; private set; }
        public int RejectedFrames { get; private set; }
        public int TotalFrames => _totalFrames;
        public int PayloadLength => _payloadLength;
        public int ReceivedBytes => _chunks.Values.Sum(chunk => chunk.Length);
        public string FileName => _fileName ?? string.Empty;
        public string SessionId => _sessionId ?? "-";
        public DateTimeOffset StartedAt => _startedAt;
        public bool IsComplete => _totalFrames > 0 && _chunks.Count == _totalFrames;

        public void Reset()
        {
            _chunks.Clear();
            _checksum = null;
            _fileName = null;
            _sessionId = null;
            _payloadLength = 0;
            _totalFrames = 0;
            _startedAt = default;
            DuplicateFrames = 0;
            RejectedFrames = 0;
        }

        public RosterQrFrameImportResult Add(string? qrText)
        {
            if (!TryParse(qrText, out var frame))
            {
                RejectedFrames++;
                return RosterQrFrameImportResult.Rejected;
            }

            if (frame.Kind == RosterQrFrameKind.Manifest)
            {
                if (_sessionId is not null && string.Equals(_sessionId, frame.SessionId, StringComparison.Ordinal))
                {
                    DuplicateFrames++;
                    return RosterQrFrameImportResult.Duplicate;
                }

                _chunks.Clear();
                _sessionId = frame.SessionId;
                _fileName = frame.FileName;
                _checksum = frame.Checksum;
                _payloadLength = frame.PayloadLength;
                _totalFrames = frame.TotalFrames;
                _startedAt = DateTimeOffset.UtcNow;
                return RosterQrFrameImportResult.Accepted;
            }

            if (_sessionId is null || !string.Equals(_sessionId, frame.SessionId, StringComparison.Ordinal) ||
                     !string.Equals(_checksum, frame.Checksum, StringComparison.Ordinal) ||
                     _payloadLength != frame.PayloadLength || frame.Index >= _totalFrames)
            {
                RejectedFrames++;
                return RosterQrFrameImportResult.Rejected;
            }

            if (!_chunks.TryAdd(frame.Index, frame.Chunk!))
            {
                DuplicateFrames++;
                return RosterQrFrameImportResult.Duplicate;
            }

            return RosterQrFrameImportResult.Accepted;
        }

        public RosterTransferDocument GetCompletedDocument()
        {
            if (!IsComplete || _checksum is null)
                throw new InvalidOperationException("二维码传输尚未完成。");

            var receivedPayload = Enumerable.Range(0, _totalFrames).Select(index =>
                _chunks.TryGetValue(index, out var chunk)
                    ? chunk
                    : throw new InvalidOperationException("二维码分包缺失。"))
                .SelectMany(chunk => chunk)
                .ToArray();
            if (receivedPayload.Length < _payloadLength)
                throw new InvalidDataException("二维码传输校验失败，请重新扫描。");

            var compressedPayload = receivedPayload[.._payloadLength];
            if (
                !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(_checksum), SHA256.HashData(compressedPayload)))
            {
                throw new InvalidDataException("二维码传输校验失败，请重新扫描。");
            }

            var document = JsonSerializer.Deserialize<RosterTransferDocument>(Decompress(compressedPayload), SerializerOptions);
            return document ?? throw new InvalidDataException("二维码内容无效。");
        }

        private static bool TryParse(string? value, out RosterQrFrame frame)
        {
            frame = default;
            var parts = value?.Split('|', 7, StringSplitOptions.None);
            if (parts is not { Length: 6 or 7 } || parts[1].Length != 32)
                return false;

            if (parts[0] == ManifestPrefix && parts.Length == 6 &&
                int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var manifestPayloadLength) &&
                int.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out var manifestTotal) &&
                manifestPayloadLength > 0 && manifestTotal is > 0 and <= MaximumFrameCount)
            {
                try
                {
                    var fileName = Encoding.UTF8.GetString(Base64UrlDecode(parts[2]));
                    if (string.IsNullOrWhiteSpace(fileName))
                        return false;
                    frame = new RosterQrFrame(RosterQrFrameKind.Manifest, parts[1], fileName, 0, manifestTotal,
                        manifestPayloadLength, parts[5], null);
                    return true;
                }
                catch (System.FormatException)
                {
                    return false;
                }
            }

            if (parts[0] != DataPrefix || parts.Length != 7 ||
                !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
                !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var offset) ||
                !int.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out var payloadLength) ||
                index < 0 || offset < 0 || offset >= payloadLength || payloadLength <= 0 || parts[5].Length != 64)
                return false;

            try
            {
                frame = new RosterQrFrame(RosterQrFrameKind.Data, parts[1], null, index, 0, payloadLength, parts[5],
                    Convert.FromBase64String(parts[6]));
                return true;
            }
            catch (System.FormatException)
            {
                return false;
            }
        }
    }

    private readonly record struct RosterQrFrame(
        RosterQrFrameKind Kind,
        string SessionId,
        string? FileName,
        int Index,
        int TotalFrames,
        int PayloadLength,
        string Checksum,
        byte[]? Chunk);

    private enum RosterQrFrameKind
    {
        Manifest,
        Data
    }
}

public enum RosterTransferKind
{
    Students,
    Prizes
}

public sealed record RosterTransferDocument(
    int Version,
    RosterTransferKind Kind,
    string FileName,
    IReadOnlyList<RosterTransferRow> Rows);

public sealed record RosterTransferRow(
    bool Exists,
    string Id,
    string Name,
    string? DetailOne = null,
    string? DetailTwo = null,
    string? Tags = null);

public sealed record RosterQrExportSession(
    string SessionId,
    IReadOnlyList<byte[]> Frames,
    int DataFrameCount,
    int PayloadBytes,
    int RecordCount);

public enum RosterQrFrameImportResult
{
    Accepted,
    Duplicate,
    Rejected
}
