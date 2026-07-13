using System.Reflection;
using SecRandom.Services.ImportExport;

namespace SecRandom.Core.Tests;

public class ImportExportArchiveTests
{
    [Theory]
    [InlineData("../settings.json")]
    [InlineData("C:/settings.json")]
    [InlineData("config/../security/credentials.v1.json")]
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
}
