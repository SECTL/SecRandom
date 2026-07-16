using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SecRandom.Core.Abstraction.Services;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Components;
using SoundFlow.Providers;
using SoundFlow.Structs;

namespace SecRandom.Services.Voice;

public sealed class SpeechAudioPlayer : ISpeechAudioPlayer
{
    public async Task PlayAsync(
        string path,
        int volume,
        int playbackSpeed,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var engine = new MiniAudioEngine();
        using var device = engine.InitializePlaybackDevice(null, AudioFormat.DvdHq);
        var format = AudioFormat.DvdHq;
        using var source = new StreamDataProvider(
            engine,
            format,
            new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
        var speed = Math.Clamp(playbackSpeed, 1, 200) / 100f;
        using var player = new SoundPlayer(engine, format, source)
        {
            Volume = Math.Clamp(volume, 0, 100) / 100f,
            PlaybackSpeed = speed
        };
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        player.PlaybackEnded += (_, _) => completion.TrySetResult();
        using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));

        device.MasterMixer.AddComponent(player);
        device.Start();
        player.Play();
        try
        {
            await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            player.Stop();
            device.MasterMixer.RemoveComponent(player);
        }
    }
}
