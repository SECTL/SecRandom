using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using SecRandom.Core.Models.Verification;
using SecRandom.Core.Services.Verification;
using SecRandom.Shared.Models.Verification;

public static class CryptoRandomAudit
{
    private const int SeedSamples = 256;
    private const int BoundedSamples = 700_000;
    private const int InventorySamples = 120_000;

    public static CryptoRandomAuditReport Run(string outputDirectory)
    {
        var seedSamples = Enumerable.Range(0, SeedSamples)
            .Select(_ => VerificationSeedDerivation.CreateCsprngSeed())
            .ToArray();
        var seedDigest = SHA256.HashData(seedSamples.SelectMany(sample => sample).ToArray());
        var byteCounts = seedSamples.SelectMany(sample => sample).GroupBy(value => value)
            .ToDictionary(group => group.Key, group => group.Count());
        var expectedByteCount = SeedSamples * 32d / 256d;
        var seedByteChiSquare = Enumerable.Range(0, 256)
            .Sum(value => Math.Pow(byteCounts.GetValueOrDefault((byte)value) - expectedByteCount, 2) / expectedByteCount);

        var streamSeed = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        var firstStream = new VerificationChaCha20Random(streamSeed);
        var secondStream = new VerificationChaCha20Random(streamSeed);
        var streamBytes = new byte[1_048_576];
        for (var offset = 0; offset < streamBytes.Length; offset += sizeof(uint))
            BinaryPrimitives.WriteUInt32LittleEndian(streamBytes.AsSpan(offset, sizeof(uint)), firstStream.NextUInt32());
        var streamVector = streamBytes.AsSpan(0, 68).ToArray();
        var repeatedVector = new byte[streamVector.Length];
        for (var offset = 0; offset < repeatedVector.Length; offset += sizeof(uint))
            BinaryPrimitives.WriteUInt32LittleEndian(repeatedVector.AsSpan(offset, sizeof(uint)), secondStream.NextUInt32());
        var streamPath = Path.Combine(outputDirectory, "verification-chacha20-stream.bin");
        File.WriteAllBytes(streamPath, streamBytes);

        var boundedRandom = new VerificationChaCha20Random(SHA256.HashData("SecRandom bounded audit"u8));
        var boundedCounts = new int[7];
        for (var index = 0; index < BoundedSamples; index++)
            boundedCounts[boundedRandom.NextBelow((ulong)boundedCounts.Length)]++;
        var boundedExpected = BoundedSamples / (double)boundedCounts.Length;
        var boundedChiSquare = boundedCounts.Sum(count => Math.Pow(count - boundedExpected, 2) / boundedExpected);

        var inventoryCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var kernel = new ManagedVerificationKernel();
        var inventorySeed = new byte[32];
        for (var sample = 0; sample < InventorySamples; sample++)
        {
            Array.Clear(inventorySeed);
            BinaryPrimitives.WriteInt32LittleEndian(inventorySeed, sample);
            var winners = kernel.Draw(CreateInventoryInput(), inventorySeed).Winners;
            var key = string.Join(",", winners.Select(winner => winner.RecordId.ToString("N")[..1]));
            inventoryCounts[key] = inventoryCounts.GetValueOrDefault(key) + 1;
        }
        var inventoryExpected = InventorySamples / 12d;
        var inventoryChiSquare = inventoryCounts.Values.Sum(count => Math.Pow(count - inventoryExpected, 2) / inventoryExpected);

        return new CryptoRandomAuditReport(
            SeedSamples,
            seedSamples.All(sample => sample.Length == 32),
            seedSamples.Select(Convert.ToHexString).Distinct(StringComparer.Ordinal).Count(),
            Convert.ToHexString(seedDigest),
            seedByteChiSquare,
            Convert.ToHexString(SHA256.HashData(streamBytes)),
            Convert.ToHexString(streamVector),
            streamVector.SequenceEqual(repeatedVector),
            streamPath,
            boundedCounts,
            boundedChiSquare,
            inventoryCounts,
            inventoryChiSquare);
    }

    private static VerificationDrawInput CreateInventoryInput()
    {
        return new VerificationDrawInput
        {
            Kind = VerificationDrawKind.Prize,
            SamplingMode = VerificationSamplingMode.InventoryPermutation,
            AlgorithmProfile = VerificationAlgorithmProfile.LotteryInventoryCount,
            Count = 2,
            Candidates =
            [
                new VerificationCandidate(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), 0, 1_000_000, false),
                new VerificationCandidate(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), 0, 1_000_000, false),
                new VerificationCandidate(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), 0, 1_000_000, false),
                new VerificationCandidate(Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"), 0, 1_000_000, false)
            ]
        };
    }

    public sealed record CryptoRandomAuditReport(
        int SeedSamples,
        bool AllSeedsHaveRequiredLength,
        int DistinctSeedCount,
        string SeedSampleDigest,
        double SeedByteChiSquare,
        string StreamSha256,
        string StreamPrefixHex,
        bool StreamIsReproducible,
        string StreamPath,
        IReadOnlyList<int> BoundedCounts,
        double BoundedChiSquare,
        IReadOnlyDictionary<string, int> InventoryCounts,
        double InventoryChiSquare)
    {
        public string ToHtml()
        {
            var streamFile = WebUtility.HtmlEncode(Path.GetFileName(StreamPath));
            var boundedPass = BoundedChiSquare < 40;
            var inventoryPass = InventoryCounts.Count == 12 && InventoryChiSquare < 40;
            var builder = new StringBuilder();
            builder.Append("""
<!doctype html>
<html lang="zh-CN"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>SecRandom 随机性回归审计</title><style>
body{font-family:system-ui,-apple-system,"Segoe UI",sans-serif;background:#f8fafc;color:#0f172a;margin:0;padding:24px}.wrap{max-width:1120px;margin:auto}.panel{background:#fff;border:1px solid #cbd5e1;border-radius:8px;padding:16px 18px;margin:0 0 16px}table{border-collapse:collapse;width:100%}th,td{border-bottom:1px solid #e2e8f0;padding:8px;text-align:left;font-size:14px}th{background:#f1f5f9}.good{color:#166534}.warn{color:#9a3412}.mono{font-family:ui-monospace,SFMono-Regular,Menlo,Consolas,monospace;overflow-wrap:anywhere}
</style></head><body><div class="wrap"><h1>SecRandom 随机性回归审计</h1><p>本报告检查实现边界和固定回归统计，不构成 Dieharder、TestU01 或任何 CSPRNG 认证。</p>
""");
            builder.Append("<div class=\"panel\"><h2>CSPRNG 种子边界</h2>");
            builder.Append($"<p class=\"{(AllSeedsHaveRequiredLength && DistinctSeedCount == SeedSamples ? "good" : "warn")}\">32 字节长度：{AllSeedsHaveRequiredLength}；样本数：{SeedSamples:n0}；去重后：{DistinctSeedCount:n0}。</p>");
            builder.Append($"<p>样本 SHA-256：<span class=\"mono\">{SeedSampleDigest}</span></p><p>字节频率卡方：{SeedByteChiSquare:n2}（描述性统计，不设认证阈值）。</p></div>");
            builder.Append("<div class=\"panel\"><h2>ChaCha20 确定性流</h2>");
            builder.Append($"<p class=\"{(StreamIsReproducible ? "good" : "warn")}\">固定种子跨块前缀重放：{(StreamIsReproducible ? "通过" : "失败")}。</p>");
            builder.Append($"<p>1 MiB 流 SHA-256：<span class=\"mono\">{StreamSha256}</span></p><p>前 68 字节：<span class=\"mono\">{StreamPrefixHex}</span></p><p>可供独立工具复检的原始流：<span class=\"mono\">{streamFile}</span>。</p></div>");
            builder.Append("<div class=\"panel\"><h2>有界拒绝采样</h2>");
            builder.Append($"<p class=\"{(boundedPass ? "good" : "warn")}\">7 个桶、{BoundedCounts.Sum():n0} 样本、卡方 {BoundedChiSquare:n2}，回归阈值 &lt; 40：{(boundedPass ? "通过" : "需复查")}。</p>");
            builder.Append("<table><thead><tr><th>桶</th><th>次数</th></tr></thead><tbody>");
            for (var index = 0; index < BoundedCounts.Count; index++)
                builder.Append($"<tr><td>{index}</td><td>{BoundedCounts[index]:n0}</td></tr>");
            builder.Append("</tbody></table></div>");
            builder.Append("<div class=\"panel\"><h2>库存局部置换</h2>");
            builder.Append($"<p class=\"{(inventoryPass ? "good" : "warn")}\">4 份库存取 2 份，共 {InventoryCounts.Count}/12 个有序结果，卡方 {InventoryChiSquare:n2}，回归阈值 &lt; 40：{(inventoryPass ? "通过" : "需复查")}。</p>");
            builder.Append("<table><thead><tr><th>有序结果</th><th>次数</th></tr></thead><tbody>");
            foreach (var pair in InventoryCounts.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                builder.Append($"<tr><td>{WebUtility.HtmlEncode(pair.Key)}</td><td>{pair.Value:n0}</td></tr>");
            builder.Append("</tbody></table></div></div></body></html>");
            return builder.ToString();
        }
    }
}
