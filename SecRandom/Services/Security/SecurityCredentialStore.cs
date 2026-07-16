using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SecRandom.Shared;

namespace SecRandom.Services.Security;

internal sealed class SecurityCredentialStore(ICredentialKeyProtector keyProtector)
{
    private const int Version = 1;
    private readonly string _path = Utils.GetFilePath("config", "security", "credentials.v1.json");
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public bool CanStoreSecrets => keyProtector.IsAvailable;

    public SecurityCredentials Load()
    {
        if (!File.Exists(_path))
            return new SecurityCredentials();

        try
        {
            var envelope = JsonSerializer.Deserialize<SecurityCredentialEnvelope>(File.ReadAllText(_path), _jsonOptions);
            if (envelope is null || envelope.Version != Version || string.IsNullOrWhiteSpace(envelope.Payload))
                return new SecurityCredentials();

            if (!envelope.Protected)
                return new SecurityCredentials();

            var bytes = Convert.FromBase64String(envelope.Payload);
            bytes = keyProtector.Unprotect(bytes);

            return JsonSerializer.Deserialize<SecurityCredentials>(bytes, _jsonOptions) ?? new SecurityCredentials();
        }
        catch (CryptographicException)
        {
            return new SecurityCredentials();
        }
        catch (JsonException)
        {
            return new SecurityCredentials();
        }
        catch (FormatException)
        {
            return new SecurityCredentials();
        }
    }

    public void Save(SecurityCredentials credentials)
    {
        if (!CanStoreSecrets)
            throw new CryptographicException("The platform credential store is unavailable.");

        var payload = keyProtector.Protect(JsonSerializer.SerializeToUtf8Bytes(credentials, _jsonOptions));

        var envelope = new SecurityCredentialEnvelope(Version, true, Convert.ToBase64String(payload));
        var temporaryPath = _path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(envelope, _jsonOptions), Encoding.UTF8);
        File.Move(temporaryPath, _path, true);
    }
}

internal sealed record SecurityCredentialEnvelope(int Version, bool Protected, string Payload);

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
    public required string Salt { get; init; }
    public required string Hash { get; init; }
    public int Iterations { get; init; }
}

internal sealed class UsbBindingCredential
{
    public required string Id { get; init; }
    public required string RootPath { get; init; }
    public required string TokenHash { get; init; }
    public required string DisplayName { get; init; }
}
