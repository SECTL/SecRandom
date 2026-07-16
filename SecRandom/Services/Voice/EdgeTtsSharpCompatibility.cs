using System;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Edge_tts_sharp.Model;

namespace Edge_tts_sharp
{
    // EdgeTtsSharp exposes this narrow transport seam. Keeping it local lets the
    // app use ClientWebSocket with normal certificate validation on every desktop OS.
    public sealed class Wss : IDisposable
    {
        private const string ChromiumVersion = "143.0.3650.75";
        private const string SecMsGecVersion = "1-" + ChromiumVersion;
        private const string UserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
            + "(KHTML, like Gecko) Chrome/143.0.0.0 Safari/537.36 Edg/143.0.0.0";
        private readonly ClientWebSocket _socket = new();
        private readonly HttpClient _httpClient;
        private readonly Uri _uri;
        private readonly CancellationToken _cancellationToken;
        private readonly CancellationTokenRegistration _cancellationRegistration;
        private int _isClosed;

        public Wss(string url, CancellationToken cancellationToken = default)
        {
            _uri = CreateRequestUri(url);
            _httpClient = CreateHttpClient();
            _cancellationToken = cancellationToken;
            _cancellationRegistration = cancellationToken.Register(() =>
            {
                try
                {
                    _socket.Abort();
                }
                catch (ObjectDisposedException)
                {
                }
            });
        }

        public event Action<Log>? OnLog;
        public event EventHandler<EdgeTtsMessageEventArgs>? OnMessage;
        public event EventHandler? OnColse;

        public bool Run()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var connectCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                _cancellationToken,
                timeout.Token);
            try
            {
                _socket.ConnectAsync(_uri, _httpClient, connectCancellation.Token).GetAwaiter().GetResult();
                _ = ReceiveAsync();
                return _socket.State == WebSocketState.Open;
            }
            catch
            {
                _socket.Dispose();
                _httpClient.Dispose();
                throw;
            }
        }

        public void Send(string message)
        {
            var payload = Encoding.UTF8.GetBytes(message);
            _socket.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, true, _cancellationToken)
                .GetAwaiter()
                .GetResult();
        }

        public void Close()
        {
            try
            {
                if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();
                }
            }
            catch (Exception exception) when (exception is ObjectDisposedException or WebSocketException or OperationCanceledException)
            {
            }
            finally
            {
                NotifyClosed();
            }
        }

        private async Task ReceiveAsync()
        {
            try
            {
                while (_socket.State is WebSocketState.Open or WebSocketState.CloseSent)
                {
                    var message = await ReceiveMessageAsync().ConfigureAwait(false);
                    if (message is null)
                        break;

                    OnMessage?.Invoke(this, message);
                    if (_socket.State == WebSocketState.Open &&
                        message.IsText && message.Data.Contains("Path:turn.end", StringComparison.OrdinalIgnoreCase))
                    {
                        await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, _cancellationToken)
                            .ConfigureAwait(false);
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                OnLog?.Invoke(new Log { level = level.error, msg = exception.Message });
            }
            finally
            {
                NotifyClosed();
                _socket.Dispose();
                _httpClient.Dispose();
            }
        }

        private async Task<EdgeTtsMessageEventArgs?> ReceiveMessageAsync()
        {
            var buffer = new byte[8192];
            using var stream = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), _cancellationToken)
                    .ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                    return null;

                stream.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            var payload = stream.ToArray();
            return new EdgeTtsMessageEventArgs(result.MessageType == WebSocketMessageType.Text, payload);
        }

        private void NotifyClosed()
        {
            if (Interlocked.Exchange(ref _isClosed, 1) == 0)
                OnColse?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            Close();
            _cancellationRegistration.Dispose();
            _socket.Dispose();
            _httpClient.Dispose();
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient(new SocketsHttpHandler { UseCookies = false });
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate, br, zstd");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Pragma", "no-cache");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Cache-Control", "no-cache");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "Cookie",
                $"muid={Convert.ToHexString(RandomNumberGenerator.GetBytes(16))};");
            return client;
        }

        private static Uri CreateRequestUri(string url)
        {
            const string versionParameter = "Sec-MS-GEC-Version=";
            var versionStart = url.IndexOf(versionParameter, StringComparison.OrdinalIgnoreCase);
            if (versionStart >= 0)
            {
                var valueStart = versionStart + versionParameter.Length;
                var valueEnd = url.IndexOf('&', valueStart);
                url = url[..valueStart] + SecMsGecVersion + (valueEnd < 0 ? string.Empty : url[valueEnd..]);
            }
            else
            {
                url += (url.Contains('?') ? "&" : "?") + versionParameter + SecMsGecVersion;
            }

            return new Uri($"{url}&ConnectionId={Guid.NewGuid():N}");
        }
    }

    public sealed class EdgeTtsMessageEventArgs(bool isText, byte[] rawData) : EventArgs
    {
        public bool IsText { get; } = isText;
        public bool IsBinary => !IsText;
        public byte[] RawData { get; } = rawData;
        public string Data { get; } = Encoding.UTF8.GetString(rawData);
    }
}

namespace WebSocketSharp
{
    // EdgeTtsSharp keeps an unused using directive for this legacy dependency.
    internal static class CompatibilityMarker
    {
    }
}
