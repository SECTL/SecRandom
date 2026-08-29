using System.Text.Json;
using SecRandom.Services.Auth;

namespace SecRandom.Core.Tests;

public sealed class SectlUserTests
{
    [Fact]
    public void ParsesTheUserDataEnvelopeReturnedBySectl()
    {
        const string json = "{\"data\":{\"user_id\":\"user-1\",\"email\":\"user@example.com\",\"user_name\":\"Test User\",\"avatar_file_id\":\"avatar-1\"}}";

        var user = SectlUser.TryParse(JsonSerializer.Deserialize<JsonElement>(json));

        Assert.NotNull(user);
        Assert.Equal("user-1", user!.ResolvedUserId);
        Assert.Equal("user@example.com", user.ResolvedEmail);
        Assert.Equal("Test User", user.ResolvedUserName);
        Assert.Contains("/storage/buckets/69cce3720009a343f892/files/avatar-1/view", user.ResolvedAvatarUrl);
    }

    [Fact]
    public void ParsesAnAppwriteUserDataRow()
    {
        const string json = "{\"user_id\":\"user-2\",\"email\":\"row@example.com\",\"user_name\":\"Row User\",\"avatar_file_id\":\"avatar-2\",\"$id\":\"row-1\"}";

        var user = SectlUser.TryParse(JsonSerializer.Deserialize<JsonElement>(json));

        Assert.NotNull(user);
        Assert.Equal("user-2", user!.ResolvedUserId);
        Assert.Equal("row@example.com", user.ResolvedEmail);
        Assert.Equal("Row User", user.ResolvedUserName);
        Assert.EndsWith("/files/avatar-2/view?project=69bd6e700005458848db", user.ResolvedAvatarUrl);
    }
}
