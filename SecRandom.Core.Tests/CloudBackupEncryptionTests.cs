using System.Reflection;
using System.Security.Cryptography;
using SecRandom.Core.Models.SubConfigs.General;
using SecRandom.Core.Services.Archive;
using SecRandom.Services.ImportExport;
using SecRandom.Shared;

namespace SecRandom.Core.Tests;

/// <summary>
///     Covers the client-side cloud backup encryption: the Argon2id/AES-256-GCM primitives, the
///     encrypted manifest and file-name format, and the key cache under <c>data/config/security</c>.
/// </summary>
public sealed class CloudBackupEncryptionTests : IDisposable
{
    private const string BackupId = "20260830-120000-abcd1234";
    private const string Passphrase = "correct horse battery";

    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "SecRandom",
        "cloud-encryption-tests", Guid.NewGuid().ToString("N"));

    public CloudBackupEncryptionTests()
    {
        ResetDataRootForTests();
        ConfigureDataRootForTests(_dataRoot);
    }

    [Fact]
    public void Encryption_IsOffByDefault()
    {
        // A user who never opens the cloud backup page keeps uploading plaintext.
        Assert.False(new BackupConfig().CloudEncryptionEnabled);
    }

    [Fact]
    public void Passphrase_RulesMatchTheSecurityPasswordRule()
    {
        Assert.False(CloudBackupCipher.IsPassphraseValid(null));
        Assert.False(CloudBackupCipher.IsPassphraseValid("   "));
        Assert.False(CloudBackupCipher.IsPassphraseValid("12345"));
        Assert.True(CloudBackupCipher.IsPassphraseValid("123456"));
        Assert.True(CloudBackupCipher.IsPassphraseValid(Passphrase));
    }

    [Fact]
    public void DeriveKey_IsDeterministicForTheSamePassphraseAndSalt()
    {
        var salt = CloudBackupCipher.CreateSalt();

        var first = CloudBackupCipher.DeriveKey(Passphrase, salt, CloudBackupKdfParameters.Test);
        var second = CloudBackupCipher.DeriveKey(Passphrase, salt, CloudBackupKdfParameters.Test);

        Assert.Equal(CloudBackupEncryption.KeyBytes, first.Length);
        Assert.Equal(first, second);
    }

    [Fact]
    public void DeriveKey_DependsOnBothTheSaltAndThePassphrase()
    {
        var salt = CloudBackupCipher.CreateSalt();
        var otherSalt = CloudBackupCipher.CreateSalt();

        var baseline = CloudBackupCipher.DeriveKey(Passphrase, salt, CloudBackupKdfParameters.Test);

        // A different salt is exactly what a second machine would produce on its own, so this is the
        // property that makes "adopt the account's salt" load-bearing for multi-device restores.
        Assert.NotEqual(baseline, CloudBackupCipher.DeriveKey(Passphrase, otherSalt, CloudBackupKdfParameters.Test));
        Assert.NotEqual(baseline, CloudBackupCipher.DeriveKey($"{Passphrase}!", salt, CloudBackupKdfParameters.Test));
    }

    [Fact]
    public void SealAndOpen_RoundTripTheArchive()
    {
        var archive = CreateBytes(4096);
        var salt = Salt();
        var key = CloudBackupCipher.DeriveKey(Passphrase, salt, CloudBackupKdfParameters.Test);
        var sealedBackup = CloudBackupCipher.Seal(archive, key, salt, CloudBackupKdfParameters.Test);

        Assert.Equal(archive.Length, sealedBackup.Payload.Length);
        Assert.NotEqual(archive, sealedBackup.Payload);
        Assert.Equal(archive, CloudBackupCipher.Open(sealedBackup.Payload, key, sealedBackup.Encryption));
        Assert.True(CloudBackupCipher.VerifyKey(sealedBackup.Encryption, key));
    }

    [Fact]
    public void Open_WithAnotherKey_Fails()
    {
        var archive = CreateBytes(512);
        var salt = Salt();
        var key = CloudBackupCipher.DeriveKey(Passphrase, salt, CloudBackupKdfParameters.Test);
        var sealedBackup = CloudBackupCipher.Seal(archive, key, salt, CloudBackupKdfParameters.Test);
        var wrongKey = CloudBackupCipher.DeriveKey("another passphrase", salt, CloudBackupKdfParameters.Test);

        Assert.False(CloudBackupCipher.VerifyKey(sealedBackup.Encryption, wrongKey));
        Assert.Throws<CloudBackupWrongPassphraseException>(() =>
            CloudBackupCipher.Open(sealedBackup.Payload, wrongKey, sealedBackup.Encryption));
    }

    [Fact]
    public void Open_WhenTheStoredPayloadWasChanged_Fails()
    {
        var archive = CreateBytes(512);
        var salt = Salt();
        var key = CloudBackupCipher.DeriveKey(Passphrase, salt, CloudBackupKdfParameters.Test);
        var sealedBackup = CloudBackupCipher.Seal(archive, key, salt, CloudBackupKdfParameters.Test);

        // A cloud service that controls the storage can replace a part; the GCM tag is what stops the
        // changed archive from being accepted.
        sealedBackup.Payload[10] ^= 0xFF;

        Assert.Throws<CloudBackupWrongPassphraseException>(() =>
            CloudBackupCipher.Open(sealedBackup.Payload, key, sealedBackup.Encryption));
    }

    [Fact]
    public void TryParseFileName_ReportsTheEncryptionMarker()
    {
        Assert.True(CloudBackupPackage.TryParseFileName(
            CloudBackupPackage.BuildManifestName(BackupId, encrypted: true), out var manifestId, out _, out _,
            out var isManifest, out var manifestEncrypted));
        Assert.Equal(BackupId, manifestId);
        Assert.True(isManifest);
        Assert.True(manifestEncrypted);

        Assert.True(CloudBackupPackage.TryParseFileName(
            CloudBackupPackage.BuildPartName(BackupId, 2, 3, encrypted: true), out var partId, out var index,
            out var count, out var partIsManifest, out var partEncrypted));
        Assert.Equal(BackupId, partId);
        Assert.Equal(2, index);
        Assert.Equal(3, count);
        Assert.False(partIsManifest);
        Assert.True(partEncrypted);

        // Names written before encryption existed keep parsing, and simply report no marker.
        Assert.True(CloudBackupPackage.TryParseFileName(
            CloudBackupPackage.BuildManifestName(BackupId), out var plainId, out _, out _, out _, out var plainEncrypted));
        Assert.Equal(BackupId, plainId);
        Assert.False(plainEncrypted);
    }

    [Fact]
    public void EncryptedManifest_UsesSchemaTwoAndMergesBackToTheArchive()
    {
        var archive = CreateBytes(3000);
        var (payload, encryption, key) = Seal(archive);
        var parts = CloudBackupPackage.Slice(payload, 1024);
        var manifest = CloudBackupPackage.BuildManifest(BackupId, parts, 1024, ["list"], DateTime.UtcNow, "v3.0.0",
            payload, encryption);

        Assert.Equal(CloudBackupPackage.EncryptedSchemaVersion, manifest.SchemaVersion);
        Assert.NotNull(manifest.Encryption);
        Assert.Equal(CloudBackupPackage.Hash(payload), manifest.ArchiveSha256);
        Assert.All(manifest.Parts, part => Assert.Contains(CloudBackupPackage.EncryptionMarker, part.Name));

        // The manifest travels as JSON in the cloud, so the encryption block has to survive that trip.
        var roundTripped = CloudBackupPackage.DeserializeManifest(CloudBackupPackage.SerializeManifest(manifest));
        CloudBackupPackage.ValidateManifest(roundTripped);
        Assert.Equal(encryption.Salt, roundTripped.Encryption!.Salt);
        Assert.Equal(encryption.Verifier, roundTripped.Encryption.Verifier);

        var merged = CloudBackupPackage.MergeAndVerify(roundTripped, parts);
        Assert.Equal(archive, CloudBackupCipher.Open(merged, key, roundTripped.Encryption));
    }

    [Fact]
    public void ValidateManifest_RejectsAnEncryptionBlockWithoutTheEncryptedSchema()
    {
        var manifest = BuildEncryptedManifest(out _, out _, out _);
        manifest.SchemaVersion = CloudBackupPackage.SchemaVersion;

        Assert.Throws<InvalidDataException>(() => CloudBackupPackage.ValidateManifest(manifest));
    }

    [Fact]
    public void ValidateManifest_RejectsTheEncryptedSchemaWithoutAnEncryptionBlock()
    {
        var manifest = BuildEncryptedManifest(out _, out _, out _);
        manifest.Encryption = null;

        Assert.Throws<InvalidDataException>(() => CloudBackupPackage.ValidateManifest(manifest));
    }

    [Fact]
    public void ValidateManifest_RejectsAPartNameThatDisagreesWithTheEncryptionState()
    {
        var manifest = BuildEncryptedManifest(out _, out _, out _);
        manifest.Parts[0].Name = CloudBackupPackage.BuildPartName(BackupId, 1, manifest.Parts.Count);

        Assert.Throws<InvalidDataException>(() => CloudBackupPackage.ValidateManifest(manifest));
    }

    [Fact]
    public void ValidateManifest_RejectsOutOfRangeOrMalformedEncryptionParameters()
    {
        var tooLittleMemory = BuildEncryptedManifest(out _, out _, out _);
        tooLittleMemory.Encryption!.MemoryKiB = CloudBackupEncryption.MinimumMemoryKiB - 1;
        Assert.Throws<InvalidDataException>(() => CloudBackupPackage.ValidateManifest(tooLittleMemory));

        var tooManyIterations = BuildEncryptedManifest(out _, out _, out _);
        tooManyIterations.Encryption!.Iterations = CloudBackupEncryption.MaximumIterations + 1;
        Assert.Throws<InvalidDataException>(() => CloudBackupPackage.ValidateManifest(tooManyIterations));

        // A manifest is downloaded from the account cloud, so a hostile one must not be able to make a
        // client spend an arbitrary amount of memory on key derivation.
        var absurdMemory = BuildEncryptedManifest(out _, out _, out _);
        absurdMemory.Encryption!.MemoryKiB = int.MaxValue;
        Assert.Throws<InvalidDataException>(() => CloudBackupPackage.ValidateManifest(absurdMemory));

        var shortNonce = BuildEncryptedManifest(out _, out _, out _);
        shortNonce.Encryption!.Nonce = Convert.ToBase64String([1, 2, 3]);
        Assert.Throws<InvalidDataException>(() => CloudBackupPackage.ValidateManifest(shortNonce));

        var unknownKdf = BuildEncryptedManifest(out _, out _, out _);
        unknownKdf.Encryption!.Kdf = "pbkdf2";
        Assert.Throws<InvalidDataException>(() => CloudBackupPackage.ValidateManifest(unknownKdf));

        var missingVerifier = BuildEncryptedManifest(out _, out _, out _);
        missingVerifier.Encryption!.Verifier = string.Empty;
        Assert.Throws<InvalidDataException>(() => CloudBackupPackage.ValidateManifest(missingVerifier));
    }

    [Fact]
    public void KeyStore_CachesDerivedKeysPerSalt()
    {
        var store = new CloudBackupKeyStore();
        Assert.False(store.HasKey);
        Assert.Null(store.GetActiveEntry());

        var entry = store.CreateKey(Passphrase, parameters: CloudBackupKdfParameters.Test);

        Assert.True(store.HasKey);
        Assert.Equal(entry.Salt, store.GetActiveEntry()!.Salt);
        Assert.Equal(CloudBackupCipher.DeriveKey(Passphrase, entry.Salt, CloudBackupKdfParameters.Test), entry.Key);

        // The restore path looks the key up by the salt a manifest carries, so that lookup has to hit.
        Assert.NotNull(store.FindBySalt(entry.Salt));
        Assert.Null(store.FindBySalt(CloudBackupCipher.CreateSalt()));
    }

    [Fact]
    public void KeyStore_KeepsTheKeyAcrossInstancesAndForgetsItOnClear()
    {
        var created = new CloudBackupKeyStore().CreateKey(Passphrase, parameters: CloudBackupKdfParameters.Test);

        // The cache is what keeps automatic backups unattended: a later run re-reads it from disk.
        var reopened = new CloudBackupKeyStore();
        Assert.True(reopened.HasKey);
        var cached = reopened.GetActiveEntry();
        Assert.NotNull(cached);
        Assert.Equal(created.Salt, cached!.Salt);
        Assert.Equal(created.Key, cached.Key);
        Assert.Equal(CloudBackupKdfParameters.Test, cached.Parameters);

        reopened.Clear();
        Assert.False(new CloudBackupKeyStore().HasKey);
    }

    [Fact]
    public void KeyStore_PrefersTheNewestKeyForUploadsAndStillFindsTheOlderOne()
    {
        var store = new CloudBackupKeyStore();
        var older = store.CreateKey(Passphrase, parameters: CloudBackupKdfParameters.Test);
        var newer = store.CreateKey("another passphrase", parameters: CloudBackupKdfParameters.Test);

        Assert.Equal(newer.Salt, store.GetActiveEntry()!.Salt);
        // Changing the passphrase must not strand the backups encrypted with the previous one.
        Assert.Equal(older.Key, store.FindBySalt(older.Salt)!.Key);
    }

    public void Dispose()
    {
        ResetDataRootForTests();
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    private static string Salt() => CloudBackupCipher.CreateSalt();

    private static (byte[] Payload, CloudBackupEncryption Encryption, byte[] Key) Seal(byte[] archive)
    {
        var salt = Salt();
        var key = CloudBackupCipher.DeriveKey(Passphrase, salt, CloudBackupKdfParameters.Test);
        var sealedBackup = CloudBackupCipher.Seal(archive, key, salt, CloudBackupKdfParameters.Test);
        return (sealedBackup.Payload, sealedBackup.Encryption, key);
    }

    private static CloudBackupManifest BuildEncryptedManifest(out byte[] payload, out byte[] key, out byte[] archive)
    {
        archive = CreateBytes(600);
        var sealedBackup = Seal(archive);
        payload = sealedBackup.Payload;
        key = sealedBackup.Key;
        var parts = CloudBackupPackage.Slice(payload, CloudBackupPackage.DefaultPartBytes);
        return CloudBackupPackage.BuildManifest(BackupId, parts, CloudBackupPackage.DefaultPartBytes, ["list"],
            DateTime.UtcNow, "v3.0.0", payload, sealedBackup.Encryption);
    }

    private static byte[] CreateBytes(int length)
    {
        var bytes = new byte[length];
        RandomNumberGenerator.Fill(bytes);
        return bytes;
    }

    private static void ConfigureDataRootForTests(string dataRoot) =>
        GetUtilsMethod("ConfigureDataRoot").Invoke(null, [dataRoot]);

    private static void ResetDataRootForTests() => GetUtilsMethod("ResetDataRootForTests").Invoke(null, null);

    private static MethodInfo GetUtilsMethod(string name) =>
        typeof(Utils).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"Utils.{name} was not found.");
}
