#if IOS
using AVFoundation;
using Foundation;
using System.Runtime.Versioning;

namespace SecRandom.Mobile.iOS;

/// <summary>
/// iOS-only local media and speech implementation behind the neutral mobile seam.
/// </summary>
[SupportedOSPlatform("ios13.0")]
public sealed class IosMobileMediaPlayer : IMobileMediaPlayer, IDisposable
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _playbackGate = new(1, 1);
    private AVAudioPlayer? _audioPlayer;
    private AVSpeechSynthesizer? _speech;
    private bool _disposed;

    public bool IsSupported => true;

    public async Task PlayAsync(string path, int volume, bool loop, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        await _playbackGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        AVAudioPlayer? player = null;
        try
        {
            lock (_gate)
                ObjectDisposedException.ThrowIf(_disposed, this);
            StopCurrentPlayer();
            player = AVAudioPlayer.FromUrl(NSUrl.FromFilename(path), out var error);
            if (player is null || error is not null)
            {
                player?.Dispose();
                player = null;
                throw new InvalidOperationException("iOS could not open the selected media file.");
            }

            player.Volume = Math.Clamp(volume, 0, 100) / 100f;
            player.NumberOfLoops = loop ? -1 : 0;
            player.PrepareToPlay();
            cancellationToken.ThrowIfCancellationRequested();
            player.Play();
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _audioPlayer = player;
                player = null;
            }
        }
        finally
        {
            player?.Stop();
            player?.Dispose();
            _playbackGate.Release();
        }
    }

    public Task StopAsync()
    {
        return StopSerializedAsync();
    }

    public Task SpeakAsync(string text, int volume, int rate, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(text))
            return Task.CompletedTask;

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _speech ??= new AVSpeechSynthesizer();
            _speech.StopSpeaking(AVSpeechBoundary.Immediate);
            var utterance = new AVSpeechUtterance(text)
            {
                Volume = Math.Clamp(volume, 0, 100) / 100f,
                Rate = Math.Clamp(rate, 50, 200) / 100f * AVSpeechUtterance.DefaultSpeechRate
            };
            _speech.SpeakUtterance(utterance);
        }
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        AVAudioPlayer? player;
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            player = _audioPlayer;
            _audioPlayer = null;
            _speech?.StopSpeaking(AVSpeechBoundary.Immediate);
            _speech?.Dispose();
            _speech = null;
        }
        player?.Stop();
        player?.Dispose();
        GC.SuppressFinalize(this);
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

    private void StopCurrentPlayer()
    {
        AVAudioPlayer? player;
        lock (_gate)
        {
            player = _audioPlayer;
            _audioPlayer = null;
        }
        player?.Stop();
        player?.Dispose();
    }
}
#endif
