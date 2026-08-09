using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia.Metadata;
using Avalonia.Platform;
#pragma warning disable CS0618

namespace SecRandom;

public sealed class OverlayAssetLoader(
    IAssetLoader fallback,
    Assembly localAssembly,
    string assemblyName,
    string avaresPrefix,
    string physicalRoot,
    ISet<string> embeddedOnlyPaths)
    : IAssetLoader
{
    private readonly string _avaresPrefix = NormalizeAvaresPrefix(avaresPrefix);
    private readonly string _physicalRoot = Path.GetFullPath(physicalRoot);

    public void SetDefaultAssembly(Assembly assembly)
    {
        fallback.SetDefaultAssembly(assembly);
    }

    public bool Exists(Uri uri, Uri? baseUri = null)
    {
        return TryMapToFile(uri, baseUri, out _) || fallback.Exists(uri, baseUri);
    }

    public Stream Open(Uri uri, Uri? baseUri = null)
    {
        if (TryMapToFile(uri, baseUri, out var filePath))
            return File.OpenRead(filePath);

        return fallback.Open(uri, baseUri);
    }

    public (Stream stream, Assembly assembly) OpenAndGetAssembly(Uri uri, Uri? baseUri = null)
    {
        if (TryMapToFile(uri, baseUri, out var filePath))
            return (File.OpenRead(filePath), localAssembly);

        return fallback.OpenAndGetAssembly(uri, baseUri);
    }

    public Assembly? GetAssembly(Uri uri, Uri? baseUri = null)
    {
        if (TryMapToFile(uri, baseUri, out _))
            return localAssembly;

        return fallback.GetAssembly(uri, baseUri);
    }

    public IEnumerable<Uri> GetAssets(Uri uri, Uri? baseUri)
    {
        var absolute = EnsureAbsolute(uri, baseUri);

        if (!IsHandledAvaresUri(absolute))
            return fallback.GetAssets(uri, baseUri);

        var physicalAssets = GetPhysicalAssets(absolute);
        return physicalAssets
            .Concat(fallback.GetAssets(uri, baseUri))
            .DistinctBy(static asset => asset.ToString());
    }

    public void InvalidateAssemblyCache(string name)
    {
        fallback.InvalidateAssemblyCache(name);
    }

    public void InvalidateAssemblyCache()
    {
        fallback.InvalidateAssemblyCache();
    }

    private IEnumerable<Uri> GetPhysicalAssets(Uri absolute)
    {
        var virtualPath = Uri.UnescapeDataString(absolute.AbsolutePath);
        var relativePrefix = virtualPath[_avaresPrefix.Length..].TrimStart('/');
        var physicalDirectory = SafeCombine(_physicalRoot, relativePrefix);

        if (!Directory.Exists(physicalDirectory))
            return [];

        return Directory.EnumerateFiles(physicalDirectory, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(_physicalRoot, file)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/'))
            .Where(relativePath => !embeddedOnlyPaths.Contains(relativePath))
            .Select(relativePath => new Uri($"avares://{assemblyName}{_avaresPrefix}{relativePath}"));
    }

    private bool TryMapToFile(Uri uri, Uri? baseUri, out string filePath)
    {
        filePath = "";
        var absolute = EnsureAbsolute(uri, baseUri);

        if (!IsHandledAvaresUri(absolute))
            return false;

        var virtualPath = Uri.UnescapeDataString(absolute.AbsolutePath);
        var relativePath = virtualPath[_avaresPrefix.Length..]
            .TrimStart('/')
            .Replace('/', Path.DirectorySeparatorChar);

        if (embeddedOnlyPaths.Contains(relativePath.Replace(Path.DirectorySeparatorChar, '/')))
            return false;

        var fullPath = SafeCombine(_physicalRoot, relativePath);
        if (!File.Exists(fullPath))
            return false;

        filePath = fullPath;
        return true;
    }

    private bool IsHandledAvaresUri(Uri uri)
    {
        return uri.IsAbsoluteUri
               && uri.Scheme.Equals("avares", StringComparison.OrdinalIgnoreCase)
               && uri.Authority.Equals(assemblyName, StringComparison.Ordinal)
               && Uri.UnescapeDataString(uri.AbsolutePath).StartsWith(_avaresPrefix, StringComparison.Ordinal);
    }

    private static Uri EnsureAbsolute(Uri uri, Uri? baseUri)
    {
        if (uri.IsAbsoluteUri)
            return uri;

        if (baseUri is null)
            throw new InvalidOperationException($"Relative asset URI '{uri}' requires base URI.");

        return new Uri(baseUri, uri);
    }

    private string SafeCombine(string root, string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        var rootWithSeparator = _physicalRoot.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fullPath, _physicalRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Asset path escapes root directory: {relativePath}");
        }

        return fullPath;
    }

    private static string NormalizeAvaresPrefix(string prefix)
    {
        prefix = prefix.Replace('\\', '/');

        if (!prefix.StartsWith('/'))
            prefix = "/" + prefix;

        return prefix.EndsWith('/') ? prefix : prefix + "/";
    }
}
