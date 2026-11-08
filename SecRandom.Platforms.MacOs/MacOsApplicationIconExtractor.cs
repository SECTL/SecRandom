using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using SecRandom.Platforms.Abstractions;

namespace SecRandom.Platforms.MacOs;

/// <summary>
///     macOS 应用图标提取：<c>.app</c> 包 → <c>Info.plist</c> 的 <c>CFBundleIconFile</c> →
///     <c>Contents/Resources/*.icns</c> 中内嵌的 PNG。
/// </summary>
/// <remarks>
///     直接接受 <c>.icns</c> 文件；二进制 plist 通过系统 <c>plutil</c> 在有界超时内转成 XML 再解析。
/// </remarks>
public sealed partial class MacOsApplicationIconExtractor : IPlatformApplicationIconExtractor
{
    private const int MaxPlistBytes = 1024 * 1024;
    private const int PlistToolTimeoutMilliseconds = 5000;

    public PlatformIconExtractionResult Extract(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return PlatformIconExtractionResult.Failure(PlatformIconExtractionError.Failed);

        if (string.Equals(Path.GetExtension(path), ".icns", StringComparison.OrdinalIgnoreCase))
            return File.Exists(path)
                ? ReadIcns(path)
                : PlatformIconExtractionResult.Failure(PlatformIconExtractionError.Failed);

        var bundle = FindApplicationBundle(path);
        if (bundle is null)
            return PlatformIconExtractionResult.Failure(PlatformIconExtractionError.NotAnApplication);

        var resources = Path.Combine(bundle, "Contents", "Resources");
        var plistPath = Path.Combine(bundle, "Contents", "Info.plist");
        if (!File.Exists(plistPath))
            return FindAnyIcns(resources);

        var plist = ReadPlistAsXml(plistPath);
        if (plist is null)
            return PlatformIconExtractionResult.Failure(PlatformIconExtractionError.Failed);

        var iconFile = ReadStringValue(plist, "CFBundleIconFile") ??
                       ReadStringValue(plist, "CFBundleIconName");
        if (string.IsNullOrWhiteSpace(iconFile))
            return FindAnyIcns(resources);

        iconFile = iconFile.Trim();
        if (!iconFile.EndsWith(".icns", StringComparison.OrdinalIgnoreCase))
            iconFile += ".icns";

        var icnsPath = Path.Combine(resources, iconFile);
        if (File.Exists(icnsPath))
            return ReadIcns(icnsPath);

        return FindAnyIcns(resources);
    }

    private static string? FindApplicationBundle(string path)
    {
        string? current;
        if (Directory.Exists(path))
        {
            current = path;
        }
        else if (File.Exists(path))
        {
            current = Path.GetDirectoryName(path);
        }
        else
        {
            return null;
        }

        while (!string.IsNullOrWhiteSpace(current))
        {
            if (string.Equals(Path.GetExtension(current), ".app", StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(current))
                return current;

            var parent = Path.GetDirectoryName(current);
            if (string.Equals(parent, current, StringComparison.Ordinal))
                break;
            current = parent;
        }

        return null;
    }

    private static PlatformIconExtractionResult ReadIcns(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= 0 || info.Length > IcnsPngReader.MaxIconBytes)
                return PlatformIconExtractionResult.Failure(PlatformIconExtractionError.NoIconFound);

            var bytes = File.ReadAllBytes(path);
            var png = IcnsPngReader.FindLargestPng(bytes);
            return png is null
                ? PlatformIconExtractionResult.Failure(PlatformIconExtractionError.UnsupportedFormat)
                : PlatformIconExtractionResult.Success(
                    new PlatformIconImage(0, 0, png, PlatformIconPixelFormat.Png));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return PlatformIconExtractionResult.Failure(PlatformIconExtractionError.Failed);
        }
    }

    private static PlatformIconExtractionResult FindAnyIcns(string resourcesDirectory)
    {
        if (!Directory.Exists(resourcesDirectory))
            return PlatformIconExtractionResult.Failure(PlatformIconExtractionError.NoIconFound);

        try
        {
            var candidate = Directory.EnumerateFiles(resourcesDirectory, "*.icns", SearchOption.TopDirectoryOnly)
                .OrderByDescending(FileSize)
                .FirstOrDefault();
            return candidate is null
                ? PlatformIconExtractionResult.Failure(PlatformIconExtractionError.NoIconFound)
                : ReadIcns(candidate);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return PlatformIconExtractionResult.Failure(PlatformIconExtractionError.Failed);
        }
    }

    private static long FileSize(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static string? ReadPlistAsXml(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= 0 || info.Length > MaxPlistBytes)
                return null;

            var bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 6 && bytes[0] == (byte)'b' && bytes[1] == (byte)'p' &&
                bytes[2] == (byte)'l' && bytes[3] == (byte)'i' && bytes[4] == (byte)'s' &&
                bytes[5] == (byte)'t')
            {
                return ConvertBinaryPlistWithPlutil(path);
            }

            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? ConvertBinaryPlistWithPlutil(string path)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "/usr/bin/plutil",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-convert");
            startInfo.ArgumentList.Add("xml1");
            startInfo.ArgumentList.Add("-o");
            startInfo.ArgumentList.Add("-");
            startInfo.ArgumentList.Add(path);

            using var process = Process.Start(startInfo);
            if (process is null)
                return null;

            var output = process.StandardOutput.ReadToEnd();
            _ = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(PlistToolTimeoutMilliseconds))
            {
                try
                {
                    process.Kill(true);
                }
                catch (InvalidOperationException)
                {
                    // 进程已经自行退出。
                }

                return null;
            }

            return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output) ? output : null;
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static string? ReadStringValue(string plist, string key)
    {
        var match = PlistStringRegex().Match(plist);
        while (match.Success)
        {
            if (string.Equals(match.Groups["key"].Value, key, StringComparison.Ordinal))
                return match.Groups["value"].Value;
            match = match.NextMatch();
        }

        return null;
    }

    [GeneratedRegex(
        "<key>\\s*(?<key>[^<]+?)\\s*</key>\\s*<string>(?<value>[^<]*)</string>",
        RegexOptions.CultureInvariant)]
    private static partial Regex PlistStringRegex();
}
