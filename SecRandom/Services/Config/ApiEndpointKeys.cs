using System.Reflection;

namespace SecRandom.Services.Config;

internal static class ApiEndpointKeys
{
    public static string? Sync => Get("SecRandomSyncApiKey", "SECRANDOM_SYNC_API_KEY");
    public static string? Verify => Get("SecRandomVerifyApiKey", "SECRANDOM_VERIFY_API_KEY");
    public static string? OfflineQr => Get("SecRandomOfflineQrApiKey", "SECRANDOM_OFFLINE_QR_API_KEY");

    private static string? Get(string metadataName, string environmentName)
    {
        var value = typeof(ApiEndpointKeys).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == metadataName)?.Value;
        return string.IsNullOrWhiteSpace(value) ? Environment.GetEnvironmentVariable(environmentName) : value;
    }
}
