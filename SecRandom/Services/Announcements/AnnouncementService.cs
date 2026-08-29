using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SecRandom.Services.Announcements;

public sealed class AnnouncementService(
    IHttpClientFactory httpClientFactory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<AnnouncementItem>> GetAsync(CancellationToken cancellationToken = default)
    {
        HttpClient client = httpClientFactory.CreateClient("announcements");
        using var request = new HttpRequestMessage(HttpMethod.Get, "announcements");

        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // Keep compatibility with deployments that expose the gateway route under /api.
            using var fallback = new HttpRequestMessage(HttpMethod.Get, "api/announcements");
            using HttpResponseMessage fallbackResponse = await client.SendAsync(fallback, cancellationToken);
            fallbackResponse.EnsureSuccessStatusCode();
            return await ReadItemsAsync(fallbackResponse, cancellationToken);
        }

        response.EnsureSuccessStatusCode();
        return await ReadItemsAsync(response, cancellationToken);
    }

    private static async Task<IReadOnlyList<AnnouncementItem>> ReadItemsAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using JsonDocument document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        JsonElement root = document.RootElement;
        JsonElement items = root.ValueKind == JsonValueKind.Array
            ? root
            : FindCollection(root);

        if (items.ValueKind != JsonValueKind.Array)
            return [];

        var result = new List<AnnouncementItem>();
        foreach (JsonElement item in items.EnumerateArray())
        {
            AnnouncementItem? announcement = item.Deserialize<AnnouncementItem>(JsonOptions);
            if (announcement is null || string.IsNullOrWhiteSpace(announcement.Title))
                continue;

            result.Add(announcement with
            {
                Id = announcement.Id ?? GetPropertyOrNull(item, "$id"),
                PlatformId = announcement.PlatformId ?? GetPropertyOrNull(item, "platformId")
            });
        }

        return result
            .OrderByDescending(item => item.IsPinned)
            .ThenByDescending(item => item.Date, StringComparer.Ordinal)
            .ToArray();
    }

    private static JsonElement FindCollection(JsonElement root)
    {
        foreach (string name in new[] { "announcements", "items", "data", "documents" })
        {
            if (root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Array)
                return value;
        }

        return default;
    }

    private static string? GetPropertyOrNull(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}

public sealed record AnnouncementItem(
    [property: JsonPropertyName("$id")] string? Id,
    string Title,
    string Content,
    string Category,
    string Date,
    [property: JsonPropertyName("platform_id")] string? PlatformId,
    [property: JsonPropertyName("is_pinned")] bool IsPinned,
    [property: JsonPropertyName("author_id")] string? AuthorId);
