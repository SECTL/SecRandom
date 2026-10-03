using SecRandom.Services.Auth;

namespace SecRandom.Core.Tests;

/// <summary>
///     Builds a <see cref="SectlTokenStore" /> that owns a private temporary directory, so tests that
///     exercise token rotation never read or write the real <c>data/config/sectl-auth.json</c> and
///     never race each other through one shared file.
/// </summary>
internal static class TestTokenStore
{
    public static SectlTokenStore Create() => new(Path.Combine(
        Path.GetTempPath(), "secrandom-tests", Guid.NewGuid().ToString("N"), "sectl-auth.json"));
}
