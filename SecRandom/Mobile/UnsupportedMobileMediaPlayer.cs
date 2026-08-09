namespace SecRandom.Mobile;

public sealed class UnsupportedMobileMediaPlayer : IMobileMediaPlayer
{
    public bool IsSupported => false;

    public Task PlayAsync(string path, int volume, bool loop, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task StopAsync() => Task.CompletedTask;

    public Task SpeakAsync(string text, int volume, int rate, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
