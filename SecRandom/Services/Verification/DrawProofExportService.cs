using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Services.Config;
using SecRandom.Shared;
using SecRandom.Shared.Models.Verification;

namespace SecRandom.Services.Verification;

public sealed class DrawProofExportService(
    MainConfigHandler configHandler,
    ILogger<DrawProofExportService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) }
    };

    public string Save(DrawProof proof)
    {
        RemoveExpiredProofs(configHandler.Data.General.ProofRetention.RetentionDays);
        var timestamp = proof.CreatedAtUtc.UtcDateTime;
        var path = Utils.GetFilePath(
            "proofs",
            timestamp.ToString("yyyy-MM"),
            timestamp.ToString("yyyy-MM-dd"),
            $"proof_{timestamp:HH-mm-ss-fff}_{proof.ProofId:N}.srproof.json");
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(proof, JsonOptions));
        File.Move(temporaryPath, path, true);
        logger.LogInformation("已导出抽取证明：ProofId={ProofId}，模式={Mode}，路径={Path}。", proof.ProofId, proof.Mode, path);
        return path;
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
}
