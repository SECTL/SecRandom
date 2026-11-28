using SecRandom.Services.RosterTransfer;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SecRandom.Core.Tests;

public sealed class RosterTransferServiceTests
{
    private const string OfflineQrApiKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Fact]
    public async Task QrExport_PreGeneratesMultiFrameSessionThatImportsInAnyDataFrameOrder()
    {
        var transfer = new RosterTransferService(OfflineQrApiKey);
        var document = new RosterTransferDocument(
            1,
            RosterTransferKind.Students,
            "class.secrandom-roster.json",
            Enumerable.Range(0, 96).Select(index => new RosterTransferRow(
                true,
                $"student-{index}-{Guid.NewGuid():N}",
                $"Student {index}",
                $"group-{index % 6}",
                $"section-{index % 4}",
                $"tag-{index % 9}")).ToArray());

        var export = await transfer.CreateExportSessionAsync(document, TestContext.Current.CancellationToken);

        Assert.True(export.DataFrameCount > 1);
        Assert.Equal(export.DataFrameCount + 1, export.Frames.Count);

        var import = transfer.CreateImportAccumulator();
        var frameOrder = export.Frames.Take(1).Concat(export.Frames.Skip(1).Reverse());
        foreach (var frame in frameOrder)
        {
            await using var stream = new MemoryStream(frame, writable: false);
            var text = await transfer.DecodeQrTextAsync(stream, TestContext.Current.CancellationToken);

            Assert.Equal(RosterQrFrameImportResult.Accepted, import.Add(text));
        }

        Assert.True(import.IsComplete);
        var imported = import.GetCompletedDocument();
        Assert.Equal(document.Version, imported.Version);
        Assert.Equal(document.Kind, imported.Kind);
        Assert.Equal(document.FileName, imported.FileName);
        Assert.Equal(document.Rows, imported.Rows);
    }

    [Fact]
    public async Task QrExport_UsesUniformlySizedDataFrames()
    {
        var transfer = new RosterTransferService(OfflineQrApiKey);
        var document = new RosterTransferDocument(
            1,
            RosterTransferKind.Students,
            "class.secrandom-roster.json",
            Enumerable.Range(0, 96).Select(index => new RosterTransferRow(
                true,
                $"student-{index}-{Guid.NewGuid():N}",
                $"Student {index}",
                $"group-{index % 6}",
                $"section-{index % 4}",
                $"tag-{index % 9}")).ToArray());

        var export = await transfer.CreateExportSessionAsync(document, TestContext.Current.CancellationToken);
        var dataFrameSizes = export.Frames.Skip(1).Select(ReadPngSize).Distinct().ToArray();

        Assert.True(export.DataFrameCount > 1);
        Assert.Single(dataFrameSizes);
    }

    [Fact]
    public void QrImport_AcceptsLegacy320ByteDataFrames()
    {
        const int legacyFramePayloadLength = 320;
        const string sessionId = "0123456789abcdef0123456789abcdef";
        var transfer = new RosterTransferService(OfflineQrApiKey);
        var document = new RosterTransferDocument(
            1,
            RosterTransferKind.Students,
            "class.secrandom-roster.json",
            Enumerable.Range(0, 96).Select(index => new RosterTransferRow(
                true,
                $"student-{index}-{Guid.NewGuid():N}",
                $"Student {index}",
                $"group-{index % 6}",
                $"section-{index % 4}",
                $"tag-{index % 9}")).ToArray());
        var payload = Compress(JsonSerializer.SerializeToUtf8Bytes(document, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var chunks = Enumerable.Range(0, (payload.Length + legacyFramePayloadLength - 1) / legacyFramePayloadLength)
            .Select(index => payload[(index * legacyFramePayloadLength)..Math.Min(payload.Length, (index + 1) * legacyFramePayloadLength)])
            .ToArray();
        var checksum = Convert.ToHexString(SHA256.HashData(payload));
        var import = transfer.CreateImportAccumulator();
        var fileName = Convert.ToBase64String(Encoding.UTF8.GetBytes(document.FileName))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var authenticationData = string.Join('\n', "secrandom-roster-offline-qr/v2", sessionId, fileName,
            payload.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            chunks.Length.ToString(System.Globalization.CultureInfo.InvariantCulture), checksum);
        var authenticationTag = Convert.ToBase64String(HMACSHA256.HashData(Encoding.ASCII.GetBytes(OfflineQrApiKey),
                Encoding.UTF8.GetBytes(authenticationData)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        Assert.Equal(RosterQrFrameImportResult.Accepted,
            import.Add($"SRQR2M|{sessionId}|{fileName}|{payload.Length}|{chunks.Length}|{checksum}|{authenticationTag}"));
        foreach (var index in Enumerable.Range(0, chunks.Length).Reverse())
        {
            Assert.Equal(RosterQrFrameImportResult.Accepted,
                import.Add($"SRQR2D|{sessionId}|{index}|{index * legacyFramePayloadLength}|{payload.Length}|{checksum}|{Convert.ToBase64String(chunks[index])}"));
        }

        var imported = import.GetCompletedDocument();
        Assert.Equal(document.Version, imported.Version);
        Assert.Equal(document.Kind, imported.Kind);
        Assert.Equal(document.FileName, imported.FileName);
        Assert.Equal(document.Rows, imported.Rows);
    }

    [Fact]
    public async Task RosterOfflineQr_RejectsTamperedAndWrongKeyManifests()
    {
        var transfer = new RosterTransferService(OfflineQrApiKey);
        var document = new RosterTransferDocument(1, RosterTransferKind.Students, "class.json",
            [new RosterTransferRow(true, "record-1", "Student 1")]);
        var export = await transfer.CreateExportSessionAsync(document, TestContext.Current.CancellationToken);
        await using var stream = new MemoryStream(export.Frames[0], writable: false);
        var manifest = await transfer.DecodeQrTextAsync(stream, TestContext.Current.CancellationToken)
            ?? throw new InvalidDataException("Generated QR manifest could not be decoded.");

        var wrongKeyImport = new RosterTransferService(new string('B', 43)).CreateImportAccumulator();
        Assert.Equal(RosterQrFrameImportResult.Rejected, wrongKeyImport.Add(manifest));

        var parts = manifest.Split('|');
        parts[^1] = parts[^1][..^1] + (parts[^1][^1] == 'A' ? 'B' : 'A');
        Assert.Equal(RosterQrFrameImportResult.Rejected, transfer.CreateImportAccumulator().Add(string.Join('|', parts)));

        Assert.Equal(RosterQrFrameImportResult.Rejected,
            transfer.CreateImportAccumulator().Add("SRQR1M|0123456789abcdef0123456789abcdef|Y2xhc3MuanNvbg|1|1|ABCDEF"));
    }

    [Fact]
    public async Task ExampleQr_ContainsTheSpecifiedPayload()
    {
        var transfer = new RosterTransferService(OfflineQrApiKey);
        await using var stream = new MemoryStream(transfer.CreateExampleQrPng(), writable: false);

        Assert.Equal("扫啥呢，示例二维码而已，好奇心太重了",
            await transfer.DecodeQrTextAsync(stream, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void SessionCode_NormalizesLowercaseAndSeparatorsForDisplayAndImport()
    {
        const string expected = "AB12CD34EF56";

        Assert.Equal(expected, RosterSyncTransferService.NormalizeSessionCode("ab-12 cd_34ef56"));
        Assert.Equal(expected, RosterSyncTransferService.FormatSessionCode("ab-12 cd_34ef56"));
    }

    [Fact]
    public async Task OfflineQrExport_RejectsRosterLargerThan300KiB()
    {
        var transfer = new RosterTransferService(OfflineQrApiKey);
        var document = new RosterTransferDocument(
            1,
            RosterTransferKind.Students,
            "too-large.secrandom-roster.json",
            Enumerable.Range(0, 12_000).Select(index => new RosterTransferRow(
                true,
                $"student-{index}-{Guid.NewGuid():N}",
                $"Student {Guid.NewGuid():N}",
                Guid.NewGuid().ToString("N"),
                Guid.NewGuid().ToString("N"),
                Guid.NewGuid().ToString("N"))).ToArray());

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => transfer.CreateExportSessionAsync(document, TestContext.Current.CancellationToken));

        Assert.Contains("300 KiB", exception.Message);
    }

    [Fact]
    public async Task OfflineQrExport_RejectsSettingsPackageLargerThan300KiB()
    {
        var transfer = new SettingsTransferQrService();
        var package = new SyncTransferPackage(SyncTransferContentType.Settings, "settings.json",
            RandomNumberGenerator.GetBytes(SyncTransferLimits.MaxOfflineQrPayloadBytes + 1));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => transfer.CreateExportSessionAsync(package, TestContext.Current.CancellationToken));

        Assert.Contains("300 KiB", exception.Message);
    }

    [Fact]
    public async Task SettingsOfflineQr_AuthenticatesManifestAndRoundTripsWithMatchingKey()
    {
        const string apiKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        var service = new SettingsTransferQrService(apiKey);
        var package = new SyncTransferPackage(SyncTransferContentType.Settings, "settings.json",
            Encoding.UTF8.GetBytes("{\"theme\":\"system\"}"));
        var export = await service.CreateExportSessionAsync(package, TestContext.Current.CancellationToken);
        var decoder = new RosterTransferService();
        var import = service.CreateImportAccumulator();
        var decodedFrames = new List<string>();

        foreach (var frame in export.Frames)
        {
            await using var stream = new MemoryStream(frame, writable: false);
            decodedFrames.Add(await decoder.DecodeQrTextAsync(stream, TestContext.Current.CancellationToken)
                ?? throw new InvalidDataException("Generated QR frame could not be decoded."));
        }

        var manifestParts = decodedFrames[0].Split('|');
        manifestParts[^1] = (manifestParts[^1][..^1]) + (manifestParts[^1][^1] == 'A' ? 'B' : 'A');
        Assert.Equal(SettingsTransferQrFrameImportResult.Rejected, import.Add(string.Join('|', manifestParts)));

        var wrongKeyImport = new SettingsTransferQrService(new string('B', 43)).CreateImportAccumulator();
        Assert.Equal(SettingsTransferQrFrameImportResult.Rejected, wrongKeyImport.Add(decodedFrames[0]));

        foreach (var frame in decodedFrames)
            Assert.Equal(SettingsTransferQrFrameImportResult.Accepted, import.Add(frame));

        var importedPackage = import.GetCompletedPackage();
        Assert.Equal(package.ContentType, importedPackage.ContentType);
        Assert.Equal(package.FileName, importedPackage.FileName);
        Assert.Equal(package.Content, importedPackage.Content);
    }

    [Fact]
    public void CloudTransfer_RejectsPayloadLargerThanOneMiB()
    {
        var exception = Assert.Throws<InvalidDataException>(() =>
            SyncTransferLimits.EnsurePayloadSize(SyncTransferLimits.MaxPayloadBytes + 1));

        Assert.Contains("1 MiB", exception.Message);
    }

    private static (int Width, int Height) ReadPngSize(byte[] png)
    {
        Assert.True(png.Length >= 24);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);
        return (
            (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19],
            (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23]);
    }

    private static byte[] Compress(byte[] payload)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true))
            brotli.Write(payload);
        return output.ToArray();
    }
}
