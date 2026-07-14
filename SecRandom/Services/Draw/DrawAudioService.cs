using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SecRandom.Services.Music;
using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Components;
using SoundFlow.Providers;
using SoundFlow.Structs;

namespace SecRandom.Services.Draw;

public sealed class DrawAudioService(MusicLibraryService musicLibrary, ILogger<DrawAudioService> logger) : IDisposable
{
    private readonly object _gate = new();
    private readonly IPlaybackBackend _backend = new SoundFlowPlaybackBackend();
    private PlaybackSession? _activeSession;
    private PlaybackSession? _fadingSession;
    private bool _isDisposed;

    public Task StartAnimationMusicAsync(string selection, int volume, int fadeIn, bool loop)
    {
        StartSession(PlaybackKind.Animation, selection, volume, fadeIn, 0, loop);
        return Task.CompletedTask;
    }

    public Task StopAnimationMusicAsync(int fadeOut, bool immediate = false)
    {
        PlaybackSession? animationSession;
        PlaybackSession? fadingSession;

        lock (_gate)
        {
            animationSession = _activeSession?.Kind == PlaybackKind.Animation ? _activeSession : null;
            if (animationSession is not null)
                _activeSession = null;

            fadingSession = _fadingSession;
            _fadingSession = !immediate && fadeOut > 0 ? animationSession : null;
        }

        DisposeSession(fadingSession);
        StopSession(animationSession, immediate ? 0 : fadeOut);
        return Task.CompletedTask;
    }

    public Task TransitionToResultMusicAsync(
        string selection,
        int volume,
        int fadeIn,
        int fadeOut,
        int animationFadeOut)
    {
        PlaybackSession? animationSession;
        PlaybackSession? fadingSession;

        lock (_gate)
        {
            animationSession = _activeSession?.Kind == PlaybackKind.Animation ? _activeSession : null;
            if (animationSession is not null)
                _activeSession = null;

            fadingSession = _fadingSession;
            _fadingSession = animationSession;
        }

        DisposeSession(fadingSession);

        if (animationSession is null)
            StartSession(PlaybackKind.Result, selection, volume, fadeIn, fadeOut, false);
        else
            _ = FadeThenStartResultAsync(animationSession, selection, volume, fadeIn, fadeOut, animationFadeOut);

        return Task.CompletedTask;
    }

    private async Task FadeThenStartResultAsync(
        PlaybackSession animationSession,
        string selection,
        int volume,
        int fadeIn,
        int fadeOut,
        int animationFadeOut)
    {
        var cancellationToken = animationSession.Cancellation.Token;
        try
        {
            await FadeVolumeAsync(animationSession, 0, animationFadeOut, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        lock (_gate)
        {
            if (!ReferenceEquals(_fadingSession, animationSession))
                return;

            _fadingSession = null;
        }

        DisposeSession(animationSession);
        StartSession(PlaybackKind.Result, selection, volume, fadeIn, fadeOut, false);
    }

    public Task<bool> PreviewAsync(string selection)
    {
        return Task.FromResult(StartSession(PlaybackKind.Preview, selection, 100, 100, 0, false));
    }

    public Task StopAsync(int fadeOut = 0)
    {
        PlaybackSession? activeSession;
        PlaybackSession? fadingSession;

        lock (_gate)
        {
            activeSession = _activeSession;
            _activeSession = null;
            fadingSession = _fadingSession;
            _fadingSession = fadeOut > 0 ? activeSession : null;
        }

        DisposeSession(fadingSession);
        StopSession(activeSession, fadeOut);
        return Task.CompletedTask;
    }

    // Kept for existing callers while draw sessions use explicit process/result methods.
    public Task PlayAsync(string selection, int volume, int fadeIn, int fadeOut,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.CompletedTask;

        StartSession(PlaybackKind.Result, selection, volume, fadeIn, fadeOut, false);
        return Task.CompletedTask;
    }

    private bool StartSession(PlaybackKind kind, string selection, int volume, int fadeIn, int fadeOut, bool loop)
    {
        var path = selection == MusicLibraryService.RandomTrackId
            ? musicLibrary.ResolveRandomPath()
            : musicLibrary.ResolvePath(selection);
        if (path is null)
            return false;

        PlaybackSession? previousSession;
        PlaybackSession? fadingSession;
        PlaybackSession? session = null;

        lock (_gate)
        {
            if (_isDisposed)
                return false;

            previousSession = _activeSession;
            _activeSession = null;
            fadingSession = _fadingSession;
            _fadingSession = null;

            try
            {
                var handle = _backend.Create(path, Math.Clamp(volume, 0, 100) / 100f, loop);
                session = new PlaybackSession(kind, handle);
                handle.PlaybackEnded += (_, _) => CompleteSession(session);
                _activeSession = session;
                handle.Play();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "抽取音乐播放失败：文件={FileName}。", Path.GetFileName(path));
            }
        }

        DisposeSession(previousSession);
        DisposeSession(fadingSession);
        if (session is null)
            return false;

        if (fadeIn > 0)
        {
            session.Handle.Volume = 0;
            _ = FadeVolumeAsync(session, Math.Clamp(volume, 0, 100) / 100f, fadeIn, session.Cancellation.Token);
        }

        if (kind == PlaybackKind.Result && fadeOut > 0 && session.Handle.DurationSeconds * 1000 > fadeOut)
            _ = FadeResultBeforeEndAsync(session, fadeOut, session.Cancellation.Token);

        return true;
    }

    private async Task FadeResultBeforeEndAsync(PlaybackSession session, int fadeOut, CancellationToken cancellationToken)
    {
        var wait = TimeSpan.FromSeconds(session.Handle.DurationSeconds) - TimeSpan.FromMilliseconds(fadeOut);
        if (wait > TimeSpan.Zero)
            await Task.Delay(wait, cancellationToken).ConfigureAwait(false);

        await FadeOutAndDisposeAsync(session, fadeOut, cancellationToken).ConfigureAwait(false);
    }

    private void StopSession(PlaybackSession? session, int fadeOut)
    {
        if (session is null)
            return;

        if (fadeOut <= 0)
        {
            DisposeSession(session);
            return;
        }

        _ = FadeOutAndDisposeAsync(session, fadeOut, session.Cancellation.Token);
    }

    private async Task FadeOutAndDisposeAsync(PlaybackSession session, int fadeOut, CancellationToken cancellationToken)
    {
        try
        {
            await FadeVolumeAsync(session, 0, fadeOut, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        lock (_gate)
        {
            if (ReferenceEquals(_fadingSession, session))
                _fadingSession = null;
        }

        DisposeSession(session);
    }

    private static async Task FadeVolumeAsync(
        PlaybackSession session,
        float targetVolume,
        int durationMilliseconds,
        CancellationToken cancellationToken)
    {
        if (durationMilliseconds <= 0)
        {
            session.Handle.Volume = targetVolume;
            return;
        }

        var initialVolume = session.Handle.Volume;
        var steps = Math.Clamp(durationMilliseconds / 50, 1, 20);
        for (var step = 1; step <= steps; step++)
        {
            await Task.Delay(Math.Max(1, durationMilliseconds / steps), cancellationToken).ConfigureAwait(false);
            session.Handle.Volume = initialVolume + (targetVolume - initialVolume) * step / steps;
        }
    }

    private void CompleteSession(PlaybackSession session)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_activeSession, session))
                _activeSession = null;
            if (ReferenceEquals(_fadingSession, session))
                _fadingSession = null;
        }

        DisposeSession(session);
    }

    private static void DisposeSession(PlaybackSession? session)
    {
        if (session is null || Interlocked.Exchange(ref session.IsDisposed, 1) != 0)
            return;

        session.Cancellation.Cancel();
        try
        {
            session.Handle.Stop();
            session.Handle.Dispose();
        }
        finally
        {
            session.Cancellation.Dispose();
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        StopAsync().GetAwaiter().GetResult();
        _backend.Dispose();
    }

    private enum PlaybackKind
    {
        Animation,
        Result,
        Preview
    }

    private sealed class PlaybackSession(PlaybackKind kind, IPlaybackHandle handle)
    {
        public PlaybackKind Kind { get; } = kind;
        public IPlaybackHandle Handle { get; } = handle;
        public CancellationTokenSource Cancellation { get; } = new();
        public int IsDisposed;
    }

    private interface IPlaybackBackend : IDisposable
    {
        IPlaybackHandle Create(string path, float volume, bool loop);
    }

    private interface IPlaybackHandle : IDisposable
    {
        event EventHandler<EventArgs> PlaybackEnded;
        float Volume { get; set; }
        float DurationSeconds { get; }
        void Play();
        void Stop();
    }

    private sealed class SoundFlowPlaybackBackend : IPlaybackBackend
    {
        private readonly AudioFormat _format = AudioFormat.DvdHq;
        private MiniAudioEngine? _engine;
        private AudioPlaybackDevice? _device;

        public IPlaybackHandle Create(string path, float volume, bool loop)
        {
            _engine ??= new MiniAudioEngine();
            _device ??= _engine.InitializePlaybackDevice(null, _format);
            if (!_device.IsRunning)
                _device.Start();

            var provider = new StreamDataProvider(
                _engine,
                _format,
                new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
            var player = new SoundPlayer(_engine, _format, provider)
            {
                Volume = volume,
                IsLooping = loop
            };
            _device.MasterMixer.AddComponent(player);
            return new SoundFlowPlaybackHandle(_device, player);
        }

        public void Dispose()
        {
            _device?.Stop();
            _device?.Dispose();
            _engine?.Dispose();
            _device = null;
            _engine = null;
        }
    }

    private sealed class SoundFlowPlaybackHandle(AudioPlaybackDevice device, SoundPlayer player) : IPlaybackHandle
    {
        public event EventHandler<EventArgs> PlaybackEnded
        {
            add => player.PlaybackEnded += value;
            remove => player.PlaybackEnded -= value;
        }

        public float Volume
        {
            get => player.Volume;
            set => player.Volume = value;
        }

        public float DurationSeconds => player.Duration;

        public void Play() => player.Play();

        public void Stop() => player.Stop();

        public void Dispose()
        {
            device.MasterMixer.RemoveComponent(player);
            player.Dispose();
        }
    }
}
