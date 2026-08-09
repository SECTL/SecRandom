using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using SecRandom.Shared;

namespace SecRandom.Services.Security;

/// <summary>
/// Stores portable security credentials. The user's security password derives
/// both the Argon2id verifier and the AES-GCM key, so no platform key store is
/// required to move this file between supported hosts.
/// </summary>
internal sealed class SecurityCredentialStore
{
    private const int FormatVersion = 2;
    private const int VerifierLength = 32;
    private const int EncryptionKeyLength = 32;
    private const int DerivedMaterialLength = VerifierLength + EncryptionKeyLength;
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private readonly string _path;
    private readonly CredentialKdfParameters _defaultKdfParameters;
    private readonly Action? _beforeWrite;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public SecurityCredentialStore()
        : this(Utils.GetFilePath("config", "security", "credentials.json"), CredentialKdfParameters.Default)
    {
    }

    internal SecurityCredentialStore(
        string path,
        CredentialKdfParameters? defaultKdfParameters = null,
        Action? beforeWrite = null)
    {
        _path = path;
        _defaultKdfParameters = defaultKdfParameters ?? CredentialKdfParameters.Default;
        _beforeWrite = beforeWrite;
    }

    public bool CanStoreSecrets => true;

    public SecurityCredentialMetadata LoadMetadata()
    {
        if (!File.Exists(_path))
            return SecurityCredentialMetadata.CreateEmpty();

        try
        {
            var envelope = JsonSerializer.Deserialize<SecurityCredentialEnvelope>(File.ReadAllText(_path), _jsonOptions);
            if (!IsValidEnvelope(envelope))
                return SecurityCredentialMetadata.CreateInvalid();

            return new SecurityCredentialMetadata(
                envelope!.Password!,
                envelope.HasTotp,
                envelope.UsbBindings,
                envelope.FailedAttempts,
                envelope.LockedUntilUtc,
                envelope.Nonce,
                envelope.Tag,
                envelope.Ciphertext,
                isReadable: true);
        }
        catch (IOException)
        {
            return SecurityCredentialMetadata.CreateInvalid();
        }
        catch (UnauthorizedAccessException)
        {
            return SecurityCredentialMetadata.CreateInvalid();
        }
        catch (JsonException)
        {
            return SecurityCredentialMetadata.CreateInvalid();
        }
        catch (FormatException)
        {
            return SecurityCredentialMetadata.CreateInvalid();
        }
    }

    public SecurityCredentialContext Create(string password)
    {
        var credential = CreatePasswordCredential(password, _defaultKdfParameters);
        var material = DeriveMaterial(credential, password);
        var metadata = new SecurityCredentialMetadata(
            credential,
            hasTotp: false,
            usbBindings: [],
            failedAttempts: 0,
            lockedUntilUtc: null,
            nonce: null,
            tag: null,
            ciphertext: null,
            isReadable: true);
        var context = new SecurityCredentialContext(new SecurityCredentials { Password = credential }, metadata, material.EncryptionKey);
        CryptographicOperations.ZeroMemory(material.Verifier);
        material = default;
        return context;
    }

    public CredentialUnlockResult TryUnlock(string password, out SecurityCredentialContext? context)
    {
        context = null;
        var metadata = LoadMetadata();
        if (!metadata.IsReadable)
            return CredentialUnlockResult.Unavailable;
        if (metadata.Password is null)
            return CredentialUnlockResult.NotConfigured;

        DerivedMaterial material;
        try
        {
            material = DeriveMaterial(metadata.Password, password);
        }
        catch (CryptographicException)
        {
            return CredentialUnlockResult.Unavailable;
        }

        try
        {
            if (!CryptographicOperations.FixedTimeEquals(material.Verifier, Convert.FromBase64String(metadata.Password.Verifier)))
                return CredentialUnlockResult.InvalidPassword;

            var credentials = DecryptCredentials(metadata, material.EncryptionKey);
            context = new SecurityCredentialContext(credentials, metadata, material.EncryptionKey);
            CryptographicOperations.ZeroMemory(material.Verifier);
            material = default;
            return CredentialUnlockResult.Succeeded;
        }
        catch (CryptographicException)
        {
            return CredentialUnlockResult.Unavailable;
        }
        catch (FormatException)
        {
            return CredentialUnlockResult.Unavailable;
        }
        catch (JsonException)
        {
            return CredentialUnlockResult.Unavailable;
        }
        finally
        {
            material.Dispose();
        }
    }

    public void Rekey(SecurityCredentialContext context, string password)
    {
        var credential = CreatePasswordCredential(password, _defaultKdfParameters);
        var material = DeriveMaterial(credential, password);
        context.ReplaceKey(material.EncryptionKey);
        CryptographicOperations.ZeroMemory(material.Verifier);
        material = default;
        context.Credentials.Password = credential;
        context.Metadata.Password = credential;
    }

    public void Save(SecurityCredentialContext context)
    {
        var credentials = context.Credentials;
        var password = credentials.Password ?? throw new CryptographicException("Security credentials have no password.");
        var metadata = context.Metadata;
        metadata.Password = password;
        metadata.HasTotp = !string.IsNullOrWhiteSpace(credentials.TotpSecret);
        metadata.UsbBindings = credentials.UsbBindings.Select(CloneBinding).ToList();
        metadata.FailedAttempts = credentials.FailedAttempts;
        metadata.LockedUntilUtc = credentials.LockedUntilUtc;

        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new SecurityCredentialSecrets(credentials.TotpSecret), _jsonOptions);
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var ciphertext = new byte[payload.Length];
        var tag = new byte[TagLength];
        var associatedData = CreateAssociatedData(metadata);
        try
        {
            using var aes = new AesGcm(context.EncryptionKey, TagLength);
            aes.Encrypt(nonce, payload, ciphertext, tag, associatedData);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }

        metadata.Nonce = Convert.ToBase64String(nonce);
        metadata.Tag = Convert.ToBase64String(tag);
        metadata.Ciphertext = Convert.ToBase64String(ciphertext);
        WriteEnvelope(CreateEnvelope(metadata));
    }

    public void SaveMetadata(SecurityCredentialMetadata metadata)
    {
        if (metadata.Password is null || string.IsNullOrWhiteSpace(metadata.Nonce) ||
            string.IsNullOrWhiteSpace(metadata.Tag) || string.IsNullOrWhiteSpace(metadata.Ciphertext))
            throw new CryptographicException("Security credential metadata is incomplete.");

        WriteEnvelope(CreateEnvelope(metadata));
    }

    public void Delete()
    {
        _beforeWrite?.Invoke();
        if (File.Exists(_path))
            File.Delete(_path);
    }

    private SecurityCredentials DecryptCredentials(SecurityCredentialMetadata metadata, byte[] encryptionKey)
    {
        if (metadata.Password is null || string.IsNullOrWhiteSpace(metadata.Nonce) ||
            string.IsNullOrWhiteSpace(metadata.Tag) || string.IsNullOrWhiteSpace(metadata.Ciphertext))
            throw new CryptographicException("Security credential metadata is incomplete.");

        var nonce = Convert.FromBase64String(metadata.Nonce);
        var tag = Convert.FromBase64String(metadata.Tag);
        var ciphertext = Convert.FromBase64String(metadata.Ciphertext);
        if (nonce.Length != NonceLength || tag.Length != TagLength)
            throw new CryptographicException("Security credential envelope is invalid.");

        var plaintext = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(encryptionKey, TagLength);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, CreateAssociatedData(metadata));
            var secrets = JsonSerializer.Deserialize<SecurityCredentialSecrets>(plaintext, _jsonOptions) ??
                          throw new CryptographicException("Security credential payload is invalid.");
            if (metadata.HasTotp != !string.IsNullOrWhiteSpace(secrets.TotpSecret))
                throw new CryptographicException("Security credential metadata does not match its payload.");

            return new SecurityCredentials
            {
                Password = metadata.Password,
                TotpSecret = secrets.TotpSecret,
                UsbBindings = metadata.UsbBindings.Select(CloneBinding).ToList(),
                FailedAttempts = metadata.FailedAttempts,
                LockedUntilUtc = metadata.LockedUntilUtc
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static bool IsValidEnvelope(SecurityCredentialEnvelope? envelope)
    {
        return envelope is { FormatVersion: FormatVersion, Password: not null, UsbBindings: not null } &&
               IsValidPasswordCredential(envelope.Password) &&
               envelope.UsbBindings.All(IsValidBinding) &&
               !string.IsNullOrWhiteSpace(envelope.Nonce) &&
               !string.IsNullOrWhiteSpace(envelope.Tag) &&
               !string.IsNullOrWhiteSpace(envelope.Ciphertext);
    }

    private static bool IsValidPasswordCredential(PasswordCredential credential)
    {
        return string.Equals(credential.Algorithm, PasswordCredential.AlgorithmId, StringComparison.Ordinal) &&
               credential.MemoryKiB >= CredentialKdfParameters.MinimumMemoryKiB &&
               credential.Iterations > 0 &&
               credential.Parallelism > 0 &&
               TryGetBytes(credential.Salt, expectedLength: 32) is not null &&
               TryGetBytes(credential.Verifier, expectedLength: VerifierLength) is not null;
    }

    private static bool IsValidBinding(UsbBindingCredential binding)
    {
        return !string.IsNullOrWhiteSpace(binding.Id) &&
               !string.IsNullOrWhiteSpace(binding.TokenHash) &&
               !string.IsNullOrWhiteSpace(binding.DisplayName);
    }

    private static SecurityCredentialEnvelope CreateEnvelope(SecurityCredentialMetadata metadata)
    {
        return new SecurityCredentialEnvelope
        {
            FormatVersion = FormatVersion,
            Password = metadata.Password ?? throw new CryptographicException("Security credential metadata has no password."),
            HasTotp = metadata.HasTotp,
            UsbBindings = metadata.UsbBindings.Select(CloneBinding).ToList(),
            FailedAttempts = metadata.FailedAttempts,
            LockedUntilUtc = metadata.LockedUntilUtc,
            Nonce = metadata.Nonce!,
            Tag = metadata.Tag!,
            Ciphertext = metadata.Ciphertext!
        };
    }

    private void WriteEnvelope(SecurityCredentialEnvelope envelope)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? throw new InvalidOperationException("The credential path has no directory."));
        _beforeWrite?.Invoke();
        var temporaryPath = _path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(envelope, _jsonOptions), Encoding.UTF8);
        File.Move(temporaryPath, _path, true);
    }

    private static byte[] CreateAssociatedData(SecurityCredentialMetadata metadata)
    {
        return JsonSerializer.SerializeToUtf8Bytes(new SecurityCredentialAuthenticationData(
            FormatVersion,
            metadata.Password,
            metadata.HasTotp,
            metadata.UsbBindings));
    }

    private static PasswordCredential CreatePasswordCredential(string password, CredentialKdfParameters parameters)
    {
        var salt = RandomNumberGenerator.GetBytes(32);
        var credential = new PasswordCredential
        {
            Algorithm = PasswordCredential.AlgorithmId,
            Salt = Convert.ToBase64String(salt),
            Verifier = string.Empty,
            MemoryKiB = parameters.MemoryKiB,
            Iterations = parameters.Iterations,
            Parallelism = parameters.Parallelism
        };
        var material = DeriveMaterial(credential, password);
        try
        {
            credential.Verifier = Convert.ToBase64String(material.Verifier);
            return credential;
        }
        finally
        {
            material.Dispose();
        }
    }

    private static DerivedMaterial DeriveMaterial(PasswordCredential credential, string password)
    {
        if (!IsValidKdfParameters(credential))
            throw new CryptographicException("Security password parameters are invalid.");

        var salt = TryGetBytes(credential.Salt, expectedLength: 32) ??
                   throw new CryptographicException("Security password salt is invalid.");
        var derived = new byte[DerivedMaterialLength];
        try
        {
            var parameters = new Argon2Parameters.Builder(Argon2Parameters.Argon2id)
                .WithSalt(salt)
                .WithMemoryAsKB(credential.MemoryKiB)
                .WithIterations(credential.Iterations)
                .WithParallelism(credential.Parallelism)
                .Build();
            var passwordBytes = Encoding.UTF8.GetBytes(password);
            try
            {
                var generator = new Argon2BytesGenerator();
                generator.Init(parameters);
                generator.GenerateBytes(passwordBytes, derived);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(passwordBytes);
            }
            return new DerivedMaterial(derived[..VerifierLength], derived[VerifierLength..]);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(derived);
        }
    }

    private static bool IsValidKdfParameters(PasswordCredential credential)
    {
        return string.Equals(credential.Algorithm, PasswordCredential.AlgorithmId, StringComparison.Ordinal) &&
               credential.MemoryKiB >= CredentialKdfParameters.MinimumMemoryKiB &&
               credential.Iterations > 0 &&
               credential.Parallelism > 0;
    }

    private static byte[]? TryGetBytes(string value, int expectedLength)
    {
        try
        {
            var bytes = Convert.FromBase64String(value);
            return bytes.Length == expectedLength ? bytes : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static UsbBindingCredential CloneBinding(UsbBindingCredential binding)
    {
        return new UsbBindingCredential
        {
            Id = binding.Id,
            DeviceId = binding.DeviceId,
            MarkerCleanupPending = binding.MarkerCleanupPending,
            TokenHash = binding.TokenHash,
            DisplayName = binding.DisplayName
        };
    }
}

internal enum CredentialUnlockResult
{
    Succeeded,
    NotConfigured,
    InvalidPassword,
    Unavailable
}

internal sealed class SecurityCredentialContext(SecurityCredentials credentials, SecurityCredentialMetadata metadata, byte[] encryptionKey) : IDisposable
{
    private byte[] _encryptionKey = encryptionKey;

    public SecurityCredentials Credentials { get; } = credentials;
    public SecurityCredentialMetadata Metadata { get; } = metadata;
    public byte[] EncryptionKey => _encryptionKey;

    public void ReplaceKey(byte[] encryptionKey)
    {
        CryptographicOperations.ZeroMemory(_encryptionKey);
        _encryptionKey = encryptionKey;
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_encryptionKey);
        _encryptionKey = [];
    }
}

internal sealed class SecurityCredentialMetadata(
    PasswordCredential? password,
    bool hasTotp,
    IEnumerable<UsbBindingCredential> usbBindings,
    int failedAttempts,
    DateTimeOffset? lockedUntilUtc,
    string? nonce,
    string? tag,
    string? ciphertext,
    bool isReadable)
{
    public static SecurityCredentialMetadata CreateEmpty() => new(null, false, [], 0, null, null, null, null, true);
    public static SecurityCredentialMetadata CreateInvalid() => new(null, false, [], 0, null, null, null, null, false);

    public PasswordCredential? Password { get; set; } = password;
    public bool HasTotp { get; set; } = hasTotp;
    public List<UsbBindingCredential> UsbBindings { get; set; } = usbBindings.Select(CloneBinding).ToList();
    public int FailedAttempts { get; set; } = failedAttempts;
    public DateTimeOffset? LockedUntilUtc { get; set; } = lockedUntilUtc;
    public string? Nonce { get; set; } = nonce;
    public string? Tag { get; set; } = tag;
    public string? Ciphertext { get; set; } = ciphertext;
    public bool IsReadable { get; } = isReadable;

    private static UsbBindingCredential CloneBinding(UsbBindingCredential binding)
    {
        return new UsbBindingCredential
        {
            Id = binding.Id,
            DeviceId = binding.DeviceId,
            MarkerCleanupPending = binding.MarkerCleanupPending,
            TokenHash = binding.TokenHash,
            DisplayName = binding.DisplayName
        };
    }
}

internal sealed record CredentialKdfParameters(int MemoryKiB, int Iterations, int Parallelism)
{
    public const int MinimumMemoryKiB = 8 * 1024;
    public static CredentialKdfParameters Default { get; } = new(64 * 1024, 3, 1);
    public static CredentialKdfParameters Test { get; } = new(MinimumMemoryKiB, 1, 1);
}

internal struct DerivedMaterial(byte[] verifier, byte[] encryptionKey) : IDisposable
{
    public byte[] Verifier { get; } = verifier;
    public byte[] EncryptionKey { get; } = encryptionKey;

    public void Dispose()
    {
        if (Verifier is not null)
            CryptographicOperations.ZeroMemory(Verifier);
        if (EncryptionKey is not null)
            CryptographicOperations.ZeroMemory(EncryptionKey);
    }
}

internal sealed class SecurityCredentialEnvelope
{
    public int FormatVersion { get; init; }
    public required PasswordCredential Password { get; init; }
    public bool HasTotp { get; init; }
    public List<UsbBindingCredential> UsbBindings { get; init; } = [];
    public int FailedAttempts { get; init; }
    public DateTimeOffset? LockedUntilUtc { get; init; }
    public required string Nonce { get; init; }
    public required string Tag { get; init; }
    public required string Ciphertext { get; init; }
}

internal sealed record SecurityCredentialAuthenticationData(
    int FormatVersion,
    PasswordCredential? Password,
    bool HasTotp,
    IReadOnlyList<UsbBindingCredential> UsbBindings);

internal sealed record SecurityCredentialSecrets(string? TotpSecret);

internal sealed class SecurityCredentials
{
    public PasswordCredential? Password { get; set; }
    public string? TotpSecret { get; set; }
    public List<UsbBindingCredential> UsbBindings { get; set; } = [];
    public int FailedAttempts { get; set; }
    public DateTimeOffset? LockedUntilUtc { get; set; }
}

internal sealed class PasswordCredential
{
    public const string AlgorithmId = "argon2id";

    public required string Algorithm { get; init; }
    public required string Salt { get; init; }
    public required string Verifier { get; set; }
    public int MemoryKiB { get; init; }
    public int Iterations { get; init; }
    public int Parallelism { get; init; }
}

internal sealed class UsbBindingCredential
{
    public required string Id { get; init; }
    public string? DeviceId { get; init; }
    public bool MarkerCleanupPending { get; set; }
    public required string TokenHash { get; init; }
    public required string DisplayName { get; init; }
}
