using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using SecRandom.Services.Updates;

namespace SecRandom.Core.Tests;

public sealed class BoundedContentReaderTests
{
    [Fact]
    public async Task ReadAsync_StopsWhenUnknownLengthContentExceedsBudget()
    {
        using var content = new StreamContent(new CountingStream(new byte[] { 1, 2, 3, 4, 5 }));
        content.Headers.ContentLength = null;

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            BoundedContentReader.ReadAsync(content, 4, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetAsync_UsesHeadersReadAndRejectsOversizedBody()
    {
        var stream = new UnknownLengthStream(new byte[] { 1, 2, 3, 4, 5 });
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(stream)
        });
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<InvalidDataException>(() => BoundedContentReader.GetAsync(
            client, new Uri("https://example.test/data"), 4, TestContext.Current.CancellationToken));

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task ReadAsync_AcceptsContentExactlyAtBudget()
    {
        using var content = new ByteArrayContent(new byte[] { 1, 2, 3, 4 });

        var result = await BoundedContentReader.ReadAsync(content, 4, TestContext.Current.CancellationToken);

        Assert.Equal(new byte[] { 1, 2, 3, 4 }, result);
    }

    [Fact]
    public async Task ReadAsync_RejectsDeclaredOversizeBeforeOpeningStream()
    {
        var content = new NeverOpenContent(5);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            BoundedContentReader.ReadAsync(content, 4, TestContext.Current.CancellationToken));
        Assert.False(content.StreamOpened);
    }

    private sealed class CountingStream(byte[] bytes) : MemoryStream(bytes)
    {
    }

    private sealed class UnknownLengthStream(byte[] bytes) : MemoryStream(bytes)
    {
        public int BytesRead { get; private set; }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = base.ReadAsync(buffer, cancellationToken);
            return CountReadAsync(read);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = base.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }

        private async ValueTask<int> CountReadAsync(ValueTask<int> read)
        {
            var count = await read.ConfigureAwait(false);
            BytesRead += count;
            return count;
        }
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(response(request));
        }
    }

    private sealed class NeverOpenContent(long length) : HttpContent
    {
        public bool StreamOpened { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw new NotSupportedException();

        protected override bool TryComputeLength(out long contentLength)
        {
            contentLength = length;
            return true;
        }

        protected override Task<Stream> CreateContentReadStreamAsync() 
        {
            StreamOpened = true;
            throw new InvalidOperationException("The stream should not be opened.");
        }
    }
}
