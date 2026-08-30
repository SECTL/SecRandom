using System.Text.Json;
using SecRandom.Services.Auth;

namespace SecRandom.Core.Tests;

public sealed class SectlUserTests
{
    [Fact]
    public void ParsesTheUserDataEnvelopeReturnedBySectl()
    {
        const string json = "{\"user_id\":\"user-1\",\"email\":\"user@example.com\",\"name\":\"Test User\",\"avatar_url\":\"avatar-1\"}";

        var user = SectlUser.TryParse(JsonSerializer.Deserialize<JsonElement>(json));

        Assert.NotNull(user);
        Assert.Equal("user-1", user!.ResolvedUserId);
        Assert.Equal("user@example.com", user.ResolvedEmail);
        Assert.Equal("Test User", user.ResolvedUserName);
        Assert.Equal("avatar-1", user.ResolvedAvatarUrl);
    }
}
