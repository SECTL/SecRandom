using System;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using SecRandom.Core.Services.Archive;

namespace SecRandom.Services.ImportExport;

/// <summary>
///     Argon2id parameters a cloud backup key was derived with. They travel in the manifest so any
///     signed-in device can re-derive the same key from the same passphrase.
/// </summary>
public sealed record CloudBackupKdfParameters(int MemoryKiB, int Iterations, int Parallelism)
{
    public static CloudBackupKdfParameters Default { get; } = new(
        CloudBackupEncryption.DefaultMemoryKiB,
        CloudBackupEncryption.DefaultIterations,
        CloudBackupEncryption.DefaultParallelism);

    /// <summary>
    ///     Cheapest parameters the format still accepts. Tests use them so a key derivation stays fast;
    ///     production code always derives with <see cref="Default" />.
    /// </summary>
    public static CloudBackupKdfParameters Test { get; } = new(
        CloudBackupEncryption.MinimumMemoryKiB,
        CloudBackupEncryption.MinimumIterations,
        CloudBackupEncryption.MinimumParallelism);

    public static CloudBackupKdfParameters FromManifest(CloudBackupEncryption encryption)
    {
        ArgumentNullException.ThrowIfNull(encryption);
        return new CloudBackupKdfParameters(encryption.MemoryKiB, encryption.Iterations, encryption.Parallelism);
    }
}

/// <summary>Cloud backup encryption failure a caller may show to the user as-is.</summary>
public class CloudBackupEncryptionException(string message) : Exception(message);

/// <summary>
///     Raised when the account cloud backup is configured to encrypt but this device holds no key, so
///     an upload must not quietly fall back to plaintext.
/// </summary>
public sealed class CloudBackupEncryptionRequiredException()
    : CloudBackupEncryptionException("云端备份已开启加密，但还没有设置加密口令。");

/// <summary>
///     The passphrase did not unlock the backup: either it is not the one this backup was uploaded
///     with, or the downloaded payload no longer matches its GCM tag.
/// </summary>
public sealed class CloudBackupWrongPassphraseException()
    : CloudBackupEncryptionException("云端备份解密失败：加密口令不正确或备份数据已损坏。");

/// <summary>The outcome of sealing one archive: the bytes to upload plus the manifest block describing them.</summary>
public sealed record SealedCloudBackup(byte[] Payload, CloudBackupEncryption Encryption);

/// <summary>
///     Client-side encryption for account cloud backups. The whole v3 archive is sealed with
///     AES-256-GCM before it is sliced into parts, so the SECTL account cloud only ever stores a
///     ciphertext: the account, the platform, or anyone who replaces the cloud service with their own
///     endpoint cannot read a roster, a history file, or a settings payload. The key is derived with
///     Argon2id from a user passphrase and a per-account salt, and the derived key never leaves the
///     device except as the passphrase the user types again elsewhere.
/// </summary>
public static class CloudBackupCipher
{
    /// <summary>Shortest passphrase accepted, matching the security password rule.</summary>
    public const int PassphraseMinimumLength = 6;

    /// <summary>
    ///     Label mixed into the key when the verifier is computed. The verifier is public (it travels in
    ///     the manifest) and only lets a client check a passphrase without downloading the whole backup;
    ///     it never reveals the key.
    /// </summary>
    private const string VerifierLabel = "SecRandom/cloud-backup/verifier/v1";

    public static bool IsPassphraseValid(string? passphrase) =>
        !string.IsNullOrWhiteSpace(passphrase) && passphrase.Trim().Length >= PassphraseMinimumLength;

    /// <summary>
    ///     Creates the salt of a new encrypted backup. One salt per passphrase (rather than per upload)
    ///     is what makes two machines that share a passphrase derive the same key: the salt travels in
    ///     the manifest and the second machine adopts it instead of inventing its own.
    /// </summary>
    public static string CreateSalt() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(CloudBackupEncryption.MinimumSaltBytes));

    public static byte[] DeriveKey(string passphrase, string salt, CloudBackupKdfParameters parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passphrase);
        ArgumentNullException.ThrowIfNull(parameters);
        if (!IsPassphraseValid(passphrase))
            throw new CloudBackupEncryptionException($"云端备份加密口令至少需要 {PassphraseMinimumLength} 个字符。");

        byte[] saltBytes;
        try
        {
            saltBytes = Convert.FromBase64String(salt);
        }
        catch (FormatException exception)
        {
            throw new CloudBackupEncryptionException($"云端备份加密盐无效：{exception.Message}");
        }

        if (saltBytes.Length is < CloudBackupEncryption.MinimumSaltBytes or > CloudBackupEncryption.MaximumSaltBytes)
            throw new CloudBackupEncryptionException("云端备份加密盐长度无效。");

        var derived = new byte[CloudBackupEncryption.KeyBytes];
        var passwordBytes = Encoding.UTF8.GetBytes(passphrase);
        try
        {
            var builder = new Argon2Parameters.Builder(Argon2Parameters.Argon2id)
                .WithSalt(saltBytes)
                .WithMemoryAsKB(parameters.MemoryKiB)
                .WithIterations(parameters.Iterations)
                .WithParallelism(parameters.Parallelism)
                .Build();
            var generator = new Argon2BytesGenerator();
            generator.Init(builder);
            generator.GenerateBytes(passwordBytes, derived);
            return derived;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    /// <summary>Public check value of a derived key, stored in the manifest.</summary>
    public static string ComputeVerifier(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        using var hmac = new HMACSHA256(key);
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(VerifierLabel)));
    }

    /// <summary>
    ///     Checks a derived key against the manifest's verifier, so a wrong passphrase is rejected from
    ///     the small manifest alone instead of after downloading every part.
    /// </summary>
    public static bool VerifyKey(CloudBackupEncryption encryption, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(encryption);
        ArgumentNullException.ThrowIfNull(key);

        byte[] expected;
        try
        {
            expected = Convert.FromBase64String(encryption.Verifier);
        }
        catch (FormatException)
        {
            return false;
        }

        if (expected.Length != CloudBackupEncryption.VerifierBytes)
            return false;

        using var hmac = new HMACSHA256(key);
        var actual = hmac.ComputeHash(Encoding.UTF8.GetBytes(VerifierLabel));
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>Seals one archive and returns the ciphertext to slice plus its manifest block.</summary>
    public static SealedCloudBackup Seal(byte[] payload, byte[] key, string salt, CloudBackupKdfParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(parameters);
        if (key.Length != CloudBackupEncryption.KeyBytes)
            throw new ArgumentException("Cloud backup key must be 32 bytes.", nameof(key));

        var nonce = RandomNumberGenerator.GetBytes(CloudBackupEncryption.NonceBytes);
        var tag = new byte[CloudBackupEncryption.TagBytes];
        var ciphertext = new byte[payload.Length];
        using (var gcm = new AesGcm(key, CloudBackupEncryption.TagBytes))
        {
            gcm.Encrypt(nonce, payload, ciphertext, tag);
        }

        var encryption = new CloudBackupEncryption
        {
            Algorithm = CloudBackupEncryption.AesGcmAlgorithm,
            Kdf = CloudBackupEncryption.Argon2idKdf,
            Salt = salt,
            MemoryKiB = parameters.MemoryKiB,
            Iterations = parameters.Iterations,
            Parallelism = parameters.Parallelism,
            Nonce = Convert.ToBase64String(nonce),
            Tag = Convert.ToBase64String(tag),
            Verifier = ComputeVerifier(key)
        };
        return new SealedCloudBackup(ciphertext, encryption);
    }

    /// <summary>
    ///     Opens a verified ciphertext. The GCM tag authenticates the whole archive, so a cloud service
    ///     that alters, truncates, or swaps the payload cannot produce a valid plaintext even though it
    ///     controls the storage.
    /// </summary>
    public static byte[] Open(byte[] payload, byte[] key, CloudBackupEncryption encryption)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(encryption);
        if (key.Length != CloudBackupEncryption.KeyBytes)
            throw new ArgumentException("Cloud backup key must be 32 bytes.", nameof(key));

        byte[] nonce;
        byte[] tag;
        try
        {
            nonce = Convert.FromBase64String(encryption.Nonce);
            tag = Convert.FromBase64String(encryption.Tag);
        }
        catch (FormatException)
        {
            throw new CloudBackupWrongPassphraseException();
        }

        if (nonce.Length != CloudBackupEncryption.NonceBytes || tag.Length != CloudBackupEncryption.TagBytes)
            throw new CloudBackupWrongPassphraseException();

        var plaintext = new byte[payload.Length];
        try
        {
            using var gcm = new AesGcm(key, CloudBackupEncryption.TagBytes);
            gcm.Decrypt(nonce, payload, tag, plaintext);
            return plaintext;
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new CloudBackupWrongPassphraseException();
        }
    }
}
