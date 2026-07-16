using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using SecRandom.Shared;

namespace SecRandom.Services.Security;

internal interface ICredentialKeyProtector
{
    bool IsAvailable { get; }
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] ciphertext);
}

internal sealed class CredentialKeyProtector : ICredentialKeyProtector
{
    private const string LinuxCommand = "secret-tool";
    private const string MacCommand = "security";
    private const string ServiceName = "SecRandom.SecurityCredentials";
    private readonly Lazy<byte[]?> _key;

    public CredentialKeyProtector()
    {
        _key = new Lazy<byte[]?>(LoadOrCreateKey);
    }

    public bool IsAvailable => _key.Value is not null;

    public byte[] Protect(byte[] plaintext)
    {
        var key = _key.Value ?? throw new CryptographicException("The platform credential store is unavailable.");
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, tag.Length);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);
        return [.. nonce, .. tag, .. ciphertext];
    }

    public byte[] Unprotect(byte[] envelope)
    {
        var key = _key.Value ?? throw new CryptographicException("The platform credential store is unavailable.");
        if (envelope.Length < 28)
            throw new CryptographicException("The credential envelope is invalid.");

        var nonce = envelope[..12];
        var tag = envelope[12..28];
        var ciphertext = envelope[28..];
        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(key, tag.Length);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }

    private static byte[]? LoadOrCreateKey()
    {
        if (OperatingSystem.IsWindows())
            return LoadOrCreateWindowsKey();
        if (OperatingSystem.IsMacOS())
            return LoadOrCreateMacKey();
        if (OperatingSystem.IsLinux())
            return LoadOrCreateLinuxKey();
        return null;
    }

    [SupportedOSPlatform("windows")]
    private static byte[]? LoadOrCreateWindowsKey()
    {
        var path = Path.Combine(Utils.DataRoot, "config", "security", "key.v1");
        try
        {
            if (File.Exists(path))
                return ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);

            var key = RandomNumberGenerator.GetBytes(32);
            var protectedKey = ProtectedData.Protect(key, null, DataProtectionScope.CurrentUser);
            var temporaryPath = path + ".tmp";
            File.WriteAllBytes(temporaryPath, protectedKey);
            File.Move(temporaryPath, path, true);
            return key;
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static byte[]? LoadOrCreateMacKey()
    {
        var key = Run(MacCommand, ["find-generic-password", "-a", Environment.UserName, "-s", ServiceName, "-w"]);
        if (key is null)
        {
            key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            if (Run(MacCommand, ["add-generic-password", "-a", Environment.UserName, "-s", ServiceName, "-w", key, "-U"]) is null)
                return null;
        }

        return TryDecodeKey(key);
    }

    private static byte[]? LoadOrCreateLinuxKey()
    {
        var key = Run(LinuxCommand, ["lookup", "service", "SecRandom", "purpose", "security-credentials"]);
        if (key is null)
        {
            key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            if (Run(LinuxCommand, ["store", "--label=SecRandom security credentials", "service", "SecRandom", "purpose", "security-credentials"], key) is null)
                return null;
        }

        return TryDecodeKey(key);
    }

    private static byte[]? TryDecodeKey(string key)
    {
        try
        {
            var bytes = Convert.FromBase64String(key.Trim());
            return bytes.Length == 32 ? bytes : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string? Run(string fileName, IReadOnlyList<string> arguments, string? standardInput = null)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    RedirectStandardInput = standardInput is not null,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            foreach (var argument in arguments)
                process.StartInfo.ArgumentList.Add(argument);
            process.Start();
            if (standardInput is not null)
                process.StandardInput.Write(standardInput);
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? output : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
