using SecRandom.Shared.DataRoot;

namespace SecRandom.Core.Tests.DataRoot;

public sealed class DataRootAnchorStoreTests
{
    [Fact]
    public void Write_ThenRead_RoundTripsTheAnchor()
    {
        using var env = new DataRootTestEnvironment();
        var anchorPath = env.PathOf("anchor", DataRootPaths.AnchorFileName);

        DataRootAnchorStore.Write(anchorPath, new DataRootAnchor
        {
            Mode = DataRootModeExtensions.ToStorageValue(DataRootMode.Package),
            DataRoot = env.CreateDirectory("data"),
            PackageKind = "portable-zip"
        });

        var result = DataRootAnchorStore.Read(anchorPath);

        Assert.Equal(DataRootAnchorStatus.Valid, result.Status);
        Assert.NotNull(result.Anchor);
        Assert.Equal(DataRootMode.Package, DataRootModeExtensions.Parse(result.Anchor!.Mode));
        Assert.Equal(DataRootPaths.Normalize(env.PathOf("data")), result.Anchor.DataRoot);
        Assert.Equal("portable-zip", result.Anchor.PackageKind);
    }

    [Fact]
    public void Read_MissingAnchor_ReportsMissingWithoutThrowing()
    {
        using var env = new DataRootTestEnvironment();

        var result = DataRootAnchorStore.Read(env.PathOf("anchor", DataRootPaths.AnchorFileName));

        Assert.Equal(DataRootAnchorStatus.Missing, result.Status);
        Assert.Null(result.Anchor);
    }

    [Fact]
    public void Read_UnsupportedSchema_ReportsInvalid()
    {
        using var env = new DataRootTestEnvironment();
        var anchorPath = env.WriteFile("anchor/data-root.json",
            "{\"schemaVersion\":99,\"mode\":\"standard\",\"dataRoot\":\"/tmp/x\"}");

        var result = DataRootAnchorStore.Read(anchorPath);

        Assert.Equal(DataRootAnchorStatus.Invalid, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }
}