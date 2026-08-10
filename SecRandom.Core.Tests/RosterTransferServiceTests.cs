using SecRandom.Services.RosterTransfer;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SecRandom.Core.Tests;

public sealed class RosterTransferServiceTests
{
    [Fact]
    public async Task QrExport_PreGeneratesMultiFrameSessionThatImportsInAnyDataFrameOrder()
    {
        var transfer = new RosterTransferService();
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
        var transfer = new RosterTransferService();
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
        var transfer = new RosterTransferService();
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

        Assert.Equal(RosterQrFrameImportResult.Accepted,
            import.Add($"SRQR1M|{sessionId}|{fileName}|{payload.Length}|{chunks.Length}|{checksum}"));
        foreach (var index in Enumerable.Range(0, chunks.Length).Reverse())
        {
            Assert.Equal(RosterQrFrameImportResult.Accepted,
                import.Add($"SRQR1D|{sessionId}|{index}|{index * legacyFramePayloadLength}|{payload.Length}|{checksum}|{Convert.ToBase64String(chunks[index])}"));
        }

        var imported = import.GetCompletedDocument();
        Assert.Equal(document.Version, imported.Version);
        Assert.Equal(document.Kind, imported.Kind);
        Assert.Equal(document.FileName, imported.FileName);
        Assert.Equal(document.Rows, imported.Rows);
    }

    [Fact]
    public async Task ExampleQr_ContainsTheSpecifiedPayload()
    {
        var transfer = new RosterTransferService();
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
        var transfer = new RosterTransferService();
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
