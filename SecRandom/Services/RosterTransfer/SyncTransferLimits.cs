namespace SecRandom.Services.RosterTransfer;

/// <summary>
/// Shared limits for encrypted device-sync payloads. The Sync API enforces the same 1 MiB cloud limit.
/// </summary>
public static class SyncTransferLimits
{
    public const long MaxPayloadBytes = 1L * 1024 * 1024;
    public const int MaxOfflineQrPayloadBytes = 300 * 1024;

    public static void EnsurePayloadSize(long length, string parameterName = "payload")
    {
        if (length < 0 || length > MaxPayloadBytes)
            throw new InvalidDataException($"The {parameterName} exceeds the 1 MiB transfer limit.");
    }

    public static void EnsureOfflineQrPayloadSize(long length, string parameterName = "offline QR payload")
    {
        if (length < 0 || length > MaxOfflineQrPayloadBytes)
            throw new InvalidDataException($"The {parameterName} exceeds the 300 KiB offline QR transfer limit.");
    }
}

public enum SyncTransferContentType
{
    Roster,
    Settings,
    AllData
}

public sealed record SyncTransferPackage(SyncTransferContentType ContentType, string FileName, byte[] Content);
public sealed record SyncTransferImportResult(SyncTransferPackage Package);
