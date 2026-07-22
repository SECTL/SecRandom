using Android.Media;
using Android.Speech.Tts;
using System.Runtime.Versioning;
using SecRandom.Mobile;

namespace SecRandom.Android;

/// <summary>
/// Android-only local media and speech implementation. The shared mobile library
/// calls this through IMobileMediaPlayer and never references Android APIs itself.
/// </summary>
[SupportedOSPlatform("android24.0")]
public sealed class AndroidMobileMediaPlayer : IMobileMediaPlayer, IDisposable
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _playbackGate = new(1, 1);
    private MediaPlayer? _mediaPlayer;
    private TextToSpeech? _speech;
    private TaskCompletionSource<bool>? _speechInitialization;
    private bool _speechReady;
    private bool _disposed;

    public bool IsSupported => true;

    public async Task PlayAsync(string path, int volume, bool loop, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        await _playbackGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            StopCurrentPlayer();
            var player = new MediaPlayer();
            try
            {
                player.SetDataSource(path);
                player.Looping = loop;
                var normalizedVolume = Math.Clamp(volume, 0, 100) / 100f;
                player.SetVolume(normalizedVolume, normalizedVolume);
                player.Prepare();
                cancellationToken.ThrowIfCancellationRequested();
                player.Start();
                lock (_gate)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    _mediaPlayer = player;
                }
            }
            catch
            {
                player.Dispose();
                throw;
            }
        }
        finally
        {
            _playbackGate.Release();
        }
    }

    public Task StopAsync()
    {
        return StopSerializedAsync();
    }

    private async Task StopSerializedAsync()
    {
        await _playbackGate.WaitAsync().ConfigureAwait(false);
        try
        {
            StopCurrentPlayer();
        }
        finally
        {
            _playbackGate.Release();
        }
    }

    public async Task SpeakAsync(string text, int volume, int rate, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(text))
            return;

        var speech = await GetSpeechAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        speech.SetSpeechRate(Math.Clamp(rate, 50, 200) / 100f);
        using var parameters = new global::Android.OS.Bundle();
        parameters.PutFloat(TextToSpeech.Engine.KeyParamVolume, Math.Clamp(volume, 0, 100) / 100f);
        speech.Speak(text, QueueMode.Flush, parameters, Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        TaskCompletionSource<bool>? pendingInitialization;
        MediaPlayer? player;
        lock (_gate)
        {
            _disposed = true;
            pendingInitialization = _speechInitialization;
            _speechInitialization = null;
            player = _mediaPlayer;
            _mediaPlayer = null;
            _speech?.Stop();
            _speech?.Shutdown();
            _speech?.Dispose();
            _speech = null;
            _speechReady = false;
        }
        pendingInitialization?.TrySetResult(false);
        DisposePlayer(player);
        GC.SuppressFinalize(this);
    }

    private async Task<TextToSpeech> GetSpeechAsync(CancellationToken cancellationToken)
    {
        Task<bool> initialization;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_speechReady && _speech is not null)
                return _speech;

            if (_speechInitialization is null)
            {
                _speechInitialization = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _speech = new TextToSpeech(global::Android.App.Application.Context, new InitializationListener(this));
            }
            initialization = _speechInitialization.Task;
        }

        if (!await initialization.WaitAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("Android text-to-speech initialization failed.");
        lock (_gate)
            return !_disposed && _speech is not null
                ? _speech
                : throw new ObjectDisposedException(nameof(AndroidMobileMediaPlayer));
    }

    private sealed class InitializationListener(AndroidMobileMediaPlayer owner) : Java.Lang.Object, TextToSpeech.IOnInitListener
    {
        public void OnInit(OperationResult status)
        {
            lock (owner._gate)
            {
                if (owner._disposed)
                {
                    owner._speech?.Dispose();
                    owner._speech = null;
                    owner._speechReady = false;
                    owner._speechInitialization?.TrySetResult(false);
                    owner._speechInitialization = null;
                    return;
                }
                if (status == OperationResult.Success)
                {
                    owner._speechReady = true;
                    owner._speechInitialization?.TrySetResult(true);
                }
                else
                {
                    owner._speech?.Dispose();
                    owner._speech = null;
                    owner._speechInitialization?.TrySetResult(false);
                    owner._speechInitialization = null;
                    owner._speechReady = false;
                }
            }
        }
    }

    private void StopCurrentPlayer()
    {
        MediaPlayer? player;
        lock (_gate)
        {
            player = _mediaPlayer;
            _mediaPlayer = null;
        }
        DisposePlayer(player);
    }

    private static void DisposePlayer(MediaPlayer? player)
    {
        if (player is null)
            return;
        try { player.Stop(); }
        catch (Java.Lang.IllegalStateException) { }
        player.Dispose();
    }
}
