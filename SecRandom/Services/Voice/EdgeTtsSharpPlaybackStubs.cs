using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Edge_tts_sharp.Utils;

// EdgeTtsSharp exposes NAudio helpers from its synthesis source file. SecRandom deliberately
// provides no implementation because all playback is owned by SpeechAudioPlayer and MiniAudio.
public static class Audio
{
    public static Task PlayToByteAsync(
        byte[] source,
        float volume = 1.0f,
        float speed = 0.0f,
        CancellationToken cancellationToken = default) =>
        Task.FromException(new NotSupportedException("Use SecRandom speech audio playback instead."));
}

public sealed class AudioPlayer
{
    public AudioPlayer(byte[] source, float volume = 1.0f) =>
        throw new NotSupportedException("Use SecRandom speech audio playback instead.");
}
