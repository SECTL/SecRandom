namespace SecRandom.Mobile;

/// <summary>
/// Native mobile heads own media playback and speech so the neutral shared library
/// never references Android or iOS APIs.
/// </summary>
public interface IMobileMediaPlayer
{
    bool IsSupported { get; }

    Task PlayAsync(string path, int volume, bool loop, CancellationToken cancellationToken = default);

    Task StopAsync();

    Task SpeakAsync(string text, int volume, int rate, CancellationToken cancellationToken = default);
}
