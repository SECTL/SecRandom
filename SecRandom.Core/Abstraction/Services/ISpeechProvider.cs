namespace SecRandom.Core.Abstraction.Services;

/// <summary>
/// Enumerates voices and synthesizes audio for one speech engine.
/// Audio playback stays behind <see cref="ISpeechAudioPlayer"/>.
/// </summary>
public interface ISpeechProvider
{
    int Engine { get; }

    Task<IReadOnlyList<VoiceOption>> GetVoicesAsync(CancellationToken cancellationToken = default);

    Task<SpeechAudio> SynthesizeAsync(SpeechSynthesisRequest request, CancellationToken cancellationToken = default);
}

public interface ISpeechAudioPlayer
{
    Task PlayAsync(
        string path,
        int volume,
        int playbackSpeed,
        CancellationToken cancellationToken = default);
}

public sealed record VoiceOption(string Id, string DisplayName, string Description = "");

public sealed record SpeechSynthesisRequest(string Text, string VoiceId);

public sealed record SpeechAudio(byte[] Content, string FileExtension);
