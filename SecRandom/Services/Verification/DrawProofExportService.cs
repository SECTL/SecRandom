using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SecRandom.Shared;
using SecRandom.Shared.Models.Verification;

namespace SecRandom.Services.Verification;

public sealed class DrawProofExportService(ILogger<DrawProofExportService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) }
    };

    public string Save(DrawProof proof)
    {
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

}
