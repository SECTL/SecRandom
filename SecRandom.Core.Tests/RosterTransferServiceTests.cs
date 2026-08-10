using SecRandom.Services.RosterTransfer;

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
}
