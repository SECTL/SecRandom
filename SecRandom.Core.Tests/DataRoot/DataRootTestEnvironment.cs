using SecRandom.Shared.DataRoot;

namespace SecRandom.Core.Tests.DataRoot;

/// <summary>Disposable temp tree shared by the data-root tests; every path stays inside <see cref="Root" />.</summary>
internal sealed class DataRootTestEnvironment : IDisposable
{
    public string Root { get; } =
        Path.Combine(Path.GetTempPath(), "SecRandom", "data-root-tests", Guid.NewGuid().ToString("N"));

    public string PathOf(params string[] parts)
    {
        return Path.Combine([Root, .. parts]);
    }

    public string CreateDirectory(params string[] parts)
    {
        var path = PathOf(parts);
        Directory.CreateDirectory(path);
        return path;
    }

    public string WriteFile(string relativePath, string content)
    {
        var path = PathOf(relativePath.Split('/', '\\'));
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(path, content);
        return path;
    }

    public string ReadFile(string relativePath)
    {
        return File.ReadAllText(PathOf(relativePath.Split('/', '\\')));
    }

    public string WritePayloadMarker(string payloadDirectory, string packageKind = "portable-zip")
    {
        Directory.CreateDirectory(payloadDirectory);
        var path = Path.Combine(payloadDirectory, DataRootPaths.PayloadMarkerFileName);
        File.WriteAllText(
            path,
            "{\"schemaVersion\":1,\"product\":\"SecRandom\",\"rid\":\"win-x64\",\"packageKind\":\""
            + packageKind + "\",\"runtimeKind\":\"self-contained\"}");
        return path;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover temp tree must never fail a test run.
        }
    }
}