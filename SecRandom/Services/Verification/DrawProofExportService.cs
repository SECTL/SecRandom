using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Verification;
using SecRandom.Shared;
using SecRandom.Shared.Models.Verification;

namespace SecRandom.Services.Verification;

public sealed class DrawProofExportService(
    MainConfigHandler configHandler,
    ILogger<DrawProofExportService> logger)
{
    private const int MaximumFileNameLength = 240;
    private static readonly TimeZoneInfo ChinaStandardTime = GetChinaStandardTime();
    private static readonly HashSet<char> InvalidFileNameCharacters = "<>:\"/\\|?*"
        .Concat(Path.GetInvalidFileNameChars())
        .ToHashSet();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) }
    };

    public string Save(DrawProof proof, DrawProofExportContext context)
    {
        RemoveExpiredProofs(configHandler.Data.General.ProofRetention.RetentionDays);
        var timestamp = TimeZoneInfo.ConvertTime(proof.CreatedAtUtc, ChinaStandardTime);
        var path = Utils.GetFilePath(
            "proofs",
            timestamp.ToString("yyyy-MM"),
            timestamp.ToString("yyyy-MM-dd"),
            CreateFileName(proof, context));
        SaveAtPath(path, proof);
        RemoveProofsOverStorageLimit(configHandler.Data.General.ProofRetention.MaximumStorageBytes);
        logger.LogInformation("已导出抽取证明：ProofId={ProofId}，模式={Mode}，路径={Path}。", proof.ProofId, proof.Mode, path);
        return path;
    }

    public void SaveAtPath(string path, DrawProof proof)
    {
        var temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(proof, JsonOptions));
        File.Move(temporaryPath, path, true);
    }

    public IEnumerable<string> EnumerateProofPaths()
    {
        var root = Utils.GetDirectoryPath("proofs");
        return Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*.srproof.json", SearchOption.AllDirectories)
            : [];
    }

    public bool TryRead(string path, out DrawProof? proof)
    {
        try
        {
            proof = JsonSerializer.Deserialize<DrawProof>(File.ReadAllText(path), JsonOptions);
            return proof is not null;
        }
        catch (IOException)
        {
            proof = null;
            return false;
        }
        catch (JsonException)
        {
            proof = null;
            return false;
        }
    }

    public static string CreateFileName(DrawProof proof, DrawProofExportContext context)
    {
        var timestamp = TimeZoneInfo.ConvertTime(proof.CreatedAtUtc, ChinaStandardTime);
        var listName = SanitizeFilePart(context.ListName, "未命名名单");
        var filters = context.FilterLabels
            .Where(filter => !string.IsNullOrWhiteSpace(filter))
            .Where(filter => !filter.StartsWith("状态=", StringComparison.Ordinal))
            .Select(filter => SanitizeFilePart(filter, string.Empty))
            .Where(filter => !string.IsNullOrWhiteSpace(filter))
            .ToArray();
        if (VerificationWireCodec.TryGetAlgorithmLabel(proof.AlgorithmId, out var algorithmLabel))
            filters = [algorithmLabel, .. filters];
        var filterText = SanitizeFilePart(
            filters.Length == 0 ? "全部" : string.Join("、", filters),
            "全部");
        var fixedLength = timestamp.ToString("yyyyMMdd_HHmmss_fff").Length
            + proof.ProofId.ToString("N")[..8].Length
            + ".srproof.json".Length
            + 3;
        var availableLength = MaximumFileNameLength - fixedLength;
        listName = Truncate(listName, Math.Max(1, availableLength / 2));
        filterText = Truncate(filterText, Math.Max(1, availableLength - listName.Length));

        return $"{timestamp:yyyyMMdd_HHmmss_fff}_{listName}_{filterText}_{proof.ProofId.ToString("N")[..8]}.srproof.json";
    }

    private void RemoveExpiredProofs(int retentionDays)
    {
        if (retentionDays <= 0)
            return;

        var root = Utils.GetDirectoryPath("proofs");
        if (!Directory.Exists(root))
            return;

        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        foreach (var path in Directory.EnumerateFiles(root, "*.srproof.json", SearchOption.AllDirectories))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(path) < cutoff)
                    File.Delete(path);
            }
            catch (IOException exception)
            {
                logger.LogDebug(exception, "跳过正在使用的过期证明文件：{Path}。", path);
            }
            catch (UnauthorizedAccessException exception)
            {
                logger.LogDebug(exception, "无权删除过期证明文件：{Path}。", path);
            }
        }
    }

    private void RemoveProofsOverStorageLimit(long maximumStorageBytes)
    {
        if (maximumStorageBytes <= 0)
            return;

        var root = Utils.GetDirectoryPath("proofs");
        if (!Directory.Exists(root))
            return;

        var files = Directory.EnumerateFiles(root, "*.srproof.json", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .OrderBy(file => file.LastWriteTimeUtc)
            .ToList();
        var totalBytes = files.Sum(file => file.Length);
        foreach (var file in files)
        {
            if (totalBytes <= maximumStorageBytes)
                break;

            try
            {
                var length = file.Length;
                file.Delete();
                totalBytes -= length;
            }
            catch (IOException exception)
            {
                logger.LogDebug(exception, "跳过正在使用的超限证明文件：{Path}。", file.FullName);
            }
            catch (UnauthorizedAccessException exception)
            {
                logger.LogDebug(exception, "无权删除超限证明文件：{Path}。", file.FullName);
            }
        }
    }

    private static TimeZoneInfo GetChinaStandardTime()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        }
    }

    private static string SanitizeFilePart(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        StringBuilder builder = new();
        foreach (var character in value.Trim())
        {
            builder.Append(character switch
            {
                ':' or '：' or ',' or '，' or '、' => '_',
                _ when char.IsControl(character) || InvalidFileNameCharacters.Contains(character) => '_',
                _ => character
            });
        }

        var sanitized = builder.ToString().Trim(' ', '.');
        if (string.IsNullOrWhiteSpace(sanitized))
            return fallback;

        return Truncate(sanitized, 80);
    }

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];

}

public sealed record DrawProofExportContext(string ListName, IReadOnlyList<string> FilterLabels)
{
    public static DrawProofExportContext ForStudents(string listName, string group = "", string gender = "", string courseName = "")
    {
        List<string> filters = [];
        if (!string.IsNullOrWhiteSpace(group))
            filters.Add($"组别={group}");
        if (!string.IsNullOrWhiteSpace(gender))
            filters.Add($"性别={gender}");
        if (!string.IsNullOrWhiteSpace(courseName))
            filters.Add($"课程={courseName}");
        return new DrawProofExportContext(listName, filters);
    }

    public static DrawProofExportContext ForPrizes(string listName) =>
        new(listName, []);
}
