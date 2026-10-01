using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SecRandom.Services.Updates;

internal static class BoundedContentReader
{
    public static async Task<byte[]> GetAsync(HttpClient client, Uri uri, int maximumBytes, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (client.Timeout != Timeout.InfiniteTimeSpan)
            timeout.CancelAfter(client.Timeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await ReadAsync(response.Content, maximumBytes, timeout.Token).ConfigureAwait(false);
    }

    public static async Task<byte[]> ReadAsync(HttpContent content, int maximumBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);

        var contentLength = content.Headers.ContentLength;
        if (contentLength.HasValue && contentLength.Value > maximumBytes)
            throw new InvalidDataException("The response exceeds the configured size limit.");

        await using var input = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream(Math.Min(maximumBytes, 81920));
        var buffer = new byte[81920];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, maximumBytes - (int)output.Length + 1)), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
                return output.ToArray();
            if (read > maximumBytes - output.Length)
                throw new InvalidDataException("The response exceeds the configured size limit.");
            output.Write(buffer, 0, read);
        }
    }
}
