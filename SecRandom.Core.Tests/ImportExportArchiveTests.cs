using System.Reflection;
using SecRandom.Services.ImportExport;

namespace SecRandom.Core.Tests;

public class ImportExportArchiveTests
{
    [Theory]
    [InlineData("../settings.json")]
    [InlineData("C:/settings.json")]
    [InlineData("config/../security/credentials.v1.json")]
    [InlineData("list/CON.json")]
    [InlineData("list/invalid?.json")]
    public void ArchivePathNormalizer_RejectsUnsafePaths(string path)
    {
        var method = typeof(ImportExportService).GetMethod("NormalizePath", BindingFlags.NonPublic | BindingFlags.Static)!;

        var exception = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [path]));

        Assert.IsType<InvalidDataException>(exception.InnerException);
    }

    [Fact]
    public void ArchivePathNormalizer_NormalizesDirectorySeparators()
    {
        var method = typeof(ImportExportService).GetMethod("NormalizePath", BindingFlags.NonPublic | BindingFlags.Static)!;

        var normalized = (string)method.Invoke(null, ["list\\roll_call_list\\class.json"])!;

        Assert.Equal("list/roll_call_list/class.json", normalized);
    }

    [Theory]
    [InlineData("v3.0.0", true)]
    [InlineData("3.2.1", true)]
    [InlineData("v2.9.0", false)]
    [InlineData("v4.0.0", false)]
    [InlineData("", false)]
    public void V3ProducerVersionValidator_AcceptsOnlyV3(string producerVersion, bool expected)
    {
        var method = typeof(ImportExportService).GetMethod("IsSupportedV3ProducerVersion", BindingFlags.NonPublic | BindingFlags.Static)!;

        var result = (bool)method.Invoke(null, [producerVersion])!;

        Assert.Equal(expected, result);
    }
}
