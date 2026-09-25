using System.Security.Cryptography;
using SecRandom.Core.Services.Archive;

namespace SecRandom.Core.Tests;

public sealed class CloudBackupPackageTests
{
    private const string BackupId = "20260830-120000-abcd1234";

    [Fact]
    public void Slice_SplitsArchiveIntoOrderedPartsOfTheConfiguredSize()
    {
        var archive = CreateBytes(CloudBackupPackage.DefaultPartBytes * 2 + 17);

        var parts = CloudBackupPackage.Slice(archive, CloudBackupPackage.DefaultPartBytes);

        Assert.Equal(3, parts.Count);
        Assert.Equal(CloudBackupPackage.DefaultPartBytes, parts[0].Length);
        Assert.Equal(CloudBackupPackage.DefaultPartBytes, parts[1].Length);
        Assert.Equal(17, parts[2].Length);
        Assert.Equal(archive, parts.SelectMany(part => part).ToArray());
    }

    [Fact]
    public void BuildManifest_RecordsPartNamesLengthsAndHashes()
    {
        var archive = CreateBytes(CloudBackupPackage.DefaultPartBytes + 5);
        var parts = CloudBackupPackage.Slice(archive, CloudBackupPackage.DefaultPartBytes);

        var manifest = CloudBackupPackage.BuildManifest(BackupId, parts, CloudBackupPackage.DefaultPartBytes,
            ["config/settings.json", "list"], new DateTime(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc), "v3.0.0-beta.1", archive);

        Assert.Equal(CloudBackupPackage.Format, manifest.Format);
        Assert.Equal(CloudBackupPackage.SchemaVersion, manifest.SchemaVersion);
        Assert.Equal(BackupId, manifest.BackupId);
        Assert.Equal("v3.0.0-beta.1", manifest.ProducerVersion);
        Assert.Equal(CloudBackupPackage.BuildArchiveName(BackupId), manifest.ArchiveName);
        Assert.Equal(archive.LongLength, manifest.ArchiveBytes);
        Assert.Equal(CloudBackupPackage.Hash(archive), manifest.ArchiveSha256);
        Assert.Equal(["config/settings.json", "list"], manifest.Roots);
        Assert.Equal(2, manifest.Parts.Count);
        Assert.Equal(CloudBackupPackage.BuildPartName(BackupId, 1, 2), manifest.Parts[0].Name);
        Assert.Equal(CloudBackupPackage.BuildPartName(BackupId, 2, 2), manifest.Parts[1].Name);
        Assert.Equal(parts[0].LongLength, manifest.Parts[0].Length);
        Assert.Equal(CloudBackupPackage.Hash(parts[0]), manifest.Parts[0].Sha256);
        CloudBackupPackage.ValidateManifest(manifest);
    }

    [Fact]
    public void Manifest_RoundTripsThroughJson_AndMergesBackToTheOriginalArchive()
    {
        var archive = CreateBytes(CloudBackupPackage.DefaultPartBytes * 2 + 9);
        var parts = CloudBackupPackage.Slice(archive, CloudBackupPackage.DefaultPartBytes);
        var manifest = CloudBackupPackage.BuildManifest(BackupId, parts, CloudBackupPackage.DefaultPartBytes,
            ["list"], DateTime.UtcNow, "v3.0.0", archive);

        var restored = CloudBackupPackage.DeserializeManifest(CloudBackupPackage.SerializeManifest(manifest));

        Assert.Equal(manifest.BackupId, restored.BackupId);
        Assert.Equal(manifest.ArchiveBytes, restored.ArchiveBytes);
        Assert.Equal(manifest.ArchiveSha256, restored.ArchiveSha256);
        Assert.Equal(manifest.Parts.Select(part => part.Name), restored.Parts.Select(part => part.Name));
        Assert.Equal(manifest.Parts.Select(part => part.Sha256), restored.Parts.Select(part => part.Sha256));
        Assert.Equal(archive, CloudBackupPackage.MergeAndVerify(restored, parts));
    }

    [Fact]
    public void TryParseFileName_RecognizesPartsAndManifests_AndRejectsForeignNames()
    {
        Assert.True(CloudBackupPackage.TryParseFileName(
            CloudBackupPackage.BuildPartName(BackupId, 3, 7), out var partId, out var index, out var count, out var isManifest));
        Assert.Equal(BackupId, partId);
        Assert.Equal(3, index);
        Assert.Equal(7, count);
        Assert.False(isManifest);

        Assert.True(CloudBackupPackage.TryParseFileName(
            CloudBackupPackage.BuildManifestName(BackupId), out var manifestId, out _, out _, out var manifestFlag));
        Assert.Equal(BackupId, manifestId);
        Assert.True(manifestFlag);

        Assert.False(CloudBackupPackage.TryParseFileName("holiday-photo.png", out _, out _, out _, out _));
        Assert.False(CloudBackupPackage.TryParseFileName($"{CloudBackupPackage.FilePrefix}broken.srpart", out _, out _, out _, out _));
        Assert.False(CloudBackupPackage.TryParseFileName(null, out _, out _, out _, out _));
        // A crafted cloud file name must never feed a traversal id into a local staging path.
        Assert.False(CloudBackupPackage.TryParseFileName("../escape-manifest.json", out _, out _, out _, out _));
        Assert.False(CloudBackupPackage.TryParseFileName(
            $"{CloudBackupPackage.FilePrefix}../escape{CloudBackupPackage.ManifestSuffix}", out _, out _, out _, out _));
        Assert.False(CloudBackupPackage.IsSafeBackupId(".."));
    }

    [Fact]
    public void MergeAndVerify_RejectsMissingReorderedAndTamperedParts()
    {
        var archive = CreateBytes(CloudBackupPackage.DefaultPartBytes + 64);
        var parts = CloudBackupPackage.Slice(archive, CloudBackupPackage.DefaultPartBytes);
        var manifest = CloudBackupPackage.BuildManifest(BackupId, parts, CloudBackupPackage.DefaultPartBytes,
            ["list"], DateTime.UtcNow, "v3.0.0", archive);

        Assert.Throws<InvalidDataException>(() => CloudBackupPackage.MergeAndVerify(manifest, [.. parts.Take(1)]));

        var reordered = new List<byte[]> { parts[1], parts[0] };
        Assert.Throws<InvalidDataException>(() => CloudBackupPackage.MergeAndVerify(manifest, reordered));

        var tampered = parts.ToArray();
        tampered[0][0] ^= 0xFF;
        Assert.Throws<InvalidDataException>(() => CloudBackupPackage.MergeAndVerify(manifest, tampered));

        Assert.Throws<InvalidDataException>(() => CloudBackupPackage.MergeAndVerify(manifest, [[], parts[1]]));
    }

    [Fact]
    public void ValidateManifest_RejectsUnsupportedFormatAndInconsistentPartNames()
    {
        var archive = CreateBytes(CloudBackupPackage.DefaultPartBytes + 8);
        var parts = CloudBackupPackage.Slice(archive, CloudBackupPackage.DefaultPartBytes);
        var manifest = CloudBackupPackage.BuildManifest(BackupId, parts, CloudBackupPackage.DefaultPartBytes,
            ["list"], DateTime.UtcNow, "v3.0.0", archive);

        var wrongFormat = Clone(manifest);
        wrongFormat.Format = "something-else";
        Assert.Throws<InvalidDataException>(() => CloudBackupPackage.ValidateManifest(wrongFormat));

        var wrongSchema = Clone(manifest);
        wrongSchema.SchemaVersion = CloudBackupPackage.SchemaVersion + 1;
        Assert.Throws<InvalidDataException>(() => CloudBackupPackage.ValidateManifest(wrongSchema));

        var foreignPart = Clone(manifest);
        foreignPart.Parts[0].Name = "SecRandom-cloud-other-p01of02.srpart";
        Assert.Throws<InvalidDataException>(() => CloudBackupPackage.ValidateManifest(foreignPart));

        var wrongLength = Clone(manifest);
        wrongLength.ArchiveBytes += 1;
        Assert.Throws<InvalidDataException>(() => CloudBackupPackage.ValidateManifest(wrongLength));

        Assert.Throws<InvalidDataException>(() => CloudBackupPackage.ValidateManifest(null));
    }

    [Fact]
    public void ValidateManifest_RejectsTooManyParts()
    {
        var archive = CreateBytes(64);
        var parts = CloudBackupPackage.Slice(archive, CloudBackupPackage.DefaultPartBytes);
        var manifest = CloudBackupPackage.BuildManifest(BackupId, parts, CloudBackupPackage.DefaultPartBytes,
            ["list"], DateTime.UtcNow, "v3.0.0", archive);

        for (var index = 0; index < 64; index++)
        {
            manifest.Parts.Add(new CloudBackupPart
            {
                Index = manifest.Parts.Count + 1,
                Name = CloudBackupPackage.BuildPartName(BackupId, manifest.Parts.Count + 1, 65),
                Length = 0,
                Sha256 = CloudBackupPackage.Hash([])
            });
        }

        Assert.Throws<InvalidDataException>(() => CloudBackupPackage.ValidateManifest(manifest));
    }

    [Fact]
    public void EnsureUploadBodySize_KeepsTheDefaultPartInsideTheRequestBudget()
    {
        CloudBackupPackage.EnsureUploadBodySize(CloudBackupPackage.DefaultPartBytes);
        Assert.Throws<InvalidDataException>(() => CloudBackupPackage.EnsureUploadBodySize(2 * 1024 * 1024));
    }

    [Fact]
    public void CreateBackupId_IsUniqueAndSortable()
    {
        var timestamp = new DateTime(2026, 8, 30, 9, 8, 7, DateTimeKind.Utc);
        var first = CloudBackupPackage.CreateBackupId(timestamp);
        var second = CloudBackupPackage.CreateBackupId(timestamp);

        Assert.StartsWith("20260830-090807-", first);
        Assert.NotEqual(first, second);
        Assert.True(CloudBackupPackage.IsCloudBackupFileName(CloudBackupPackage.BuildManifestName(first)));
    }

    [Fact]
    public void CreateBackupId_WithADeviceTag_RoundTripsThroughTheFileNameAndStaysSafe()
    {
        var timestamp = new DateTime(2026, 8, 30, 9, 8, 7, DateTimeKind.Utc);

        var backupId = CloudBackupPackage.CreateBackupId(timestamp, "DESKTOP-ABC");

        Assert.True(CloudBackupPackage.IsSafeBackupId(backupId));
        Assert.True(CloudBackupPackage.TryGetDeviceTag(backupId, out var deviceTag));
        Assert.Equal("DESKTOP-ABC", deviceTag);

        // The tag rides along inside the part and manifest names, so the cloud listing is enough to
        // attribute a backup without downloading its manifest.
        var manifestName = CloudBackupPackage.BuildManifestName(backupId);
        Assert.True(CloudBackupPackage.TryParseFileName(manifestName, out var parsedId, out _, out _, out var isManifest));
        Assert.True(isManifest);
        Assert.Equal(backupId, parsedId);
        Assert.True(CloudBackupPackage.TryGetDeviceTag(parsedId, out var parsedTag));
        Assert.Equal("DESKTOP-ABC", parsedTag);

        var partName = CloudBackupPackage.BuildPartName(backupId, 1, 2);
        Assert.True(CloudBackupPackage.TryParseFileName(partName, out var partId, out var partIndex, out var partCount, out _));
        Assert.Equal(backupId, partId);
        Assert.Equal(1, partIndex);
        Assert.Equal(2, partCount);
    }

    [Fact]
    public void CreateBackupId_WithoutADeviceTag_CarriesNoTag()
    {
        var backupId = CloudBackupPackage.CreateBackupId(new DateTime(2026, 8, 30, 9, 8, 7, DateTimeKind.Utc));

        Assert.False(CloudBackupPackage.TryGetDeviceTag(backupId, out var deviceTag));
        Assert.Equal(string.Empty, deviceTag);
    }

    [Fact]
    public void BuildDeviceTag_KeepsSafeCharactersAndCapsTheLength()
    {
        Assert.Equal("DESKTOP-ABC", CloudBackupPackage.BuildDeviceTag(" DESKTOP-ABC "));
        // Unsafe characters are dropped rather than replaced, so a fully non-ASCII name still reaches
        // the digest fallback instead of turning into a row of dashes.
        Assert.Equal("homepc", CloudBackupPackage.BuildDeviceTag("home pc!"));

        var capped = CloudBackupPackage.BuildDeviceTag("abcdefghijklmnopqrstuvwxyz");
        Assert.Equal(CloudBackupPackage.MaxDeviceTagLength, capped.Length);
        Assert.Equal("abcdefghijklmnop", capped);
    }

    [Fact]
    public void BuildDeviceTag_FallsBackToAStableDigestWhenNothingIsAsciiSafe()
    {
        var first = CloudBackupPackage.BuildDeviceTag("教室电脑");
        var second = CloudBackupPackage.BuildDeviceTag("教室电脑");

        Assert.Equal(4, first.Length);
        Assert.Equal(first, second);
        Assert.True(CloudBackupPackage.IsSafeBackupId(CloudBackupPackage.CreateBackupId(DateTime.UtcNow, first)));
        Assert.Equal(string.Empty, CloudBackupPackage.BuildDeviceTag(""));
    }

    [Fact]
    public void TryGetDeviceTag_ReportsNoTagForForeignIds()
    {
        Assert.False(CloudBackupPackage.TryGetDeviceTag("20260830-120000-abcd1234", out _));
        // An id an older build uploaded, and one crafted through the cloud dashboard, are both foreign:
        // neither may be attributed to a device.
        Assert.False(CloudBackupPackage.TryGetDeviceTag("20260830-120000-abcd1234_", out _));
        Assert.False(CloudBackupPackage.TryGetDeviceTag("holiday_photos", out _));
        Assert.False(CloudBackupPackage.TryGetDeviceTag("20260830-120000-abcd123456", out _));
        Assert.False(CloudBackupPackage.TryGetDeviceTag(null, out _));
    }

    private static CloudBackupManifest Clone(CloudBackupManifest manifest) =>
        CloudBackupPackage.DeserializeManifest(CloudBackupPackage.SerializeManifest(manifest));

    private static byte[] CreateBytes(int length)
    {
        var bytes = new byte[length];
        RandomNumberGenerator.Fill(bytes);
        return bytes;
    }
}
