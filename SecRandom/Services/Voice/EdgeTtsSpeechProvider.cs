using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Edge_tts_sharp;
using Edge_tts_sharp.Model;
using SecRandom.Core.Abstraction.Services;

namespace SecRandom.Services.Voice;

public sealed class EdgeTtsSpeechProvider : ISpeechProvider
{
    public const int EdgeEngine = 1;
    private static readonly Lazy<IReadOnlyList<eVoice>> Voices = new(() => EdgeTts.GetVoice());

    public int Engine => EdgeEngine;

    public Task<IReadOnlyList<VoiceOption>> GetVoicesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<VoiceOption> voices = Voices.Value
            .Where(voice => !string.IsNullOrWhiteSpace(voice.ShortName))
            .OrderBy(voice => !voice.ShortName.StartsWith("zh-CN", StringComparison.OrdinalIgnoreCase))
            .ThenBy(voice => voice.ShortName, StringComparer.OrdinalIgnoreCase)
            .Select(voice => new VoiceOption(
                voice.ShortName,
                string.IsNullOrWhiteSpace(voice.FriendlyName) ? voice.ShortName : voice.FriendlyName,
                $"{voice.Gender} | {voice.Locale}".Trim(' ', '|')))
            .ToList();
        return Task.FromResult(voices);
    }

    public async Task<SpeechAudio> SynthesizeAsync(SpeechSynthesisRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var voice = Voices.Value.FirstOrDefault(candidate =>
                        string.Equals(candidate.ShortName, request.VoiceId, StringComparison.OrdinalIgnoreCase))
                    ?? Voices.Value.FirstOrDefault(candidate =>
                        string.Equals(candidate.ShortName, "zh-CN-XiaoxiaoNeural", StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException("No Edge TTS voice is available.");
        var completion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        using var registration = linkedCancellation.Token.Register(() => completion.TrySetCanceled(linkedCancellation.Token));

        EdgeTts.Await = false;
        try
        {
            await Task.Run(() => EdgeTts.Invoke(
                new PlayOption
                {
                    Text = SecurityElement.Escape(request.Text) ?? string.Empty
                },
                voice,
                bytes => completion.TrySetResult(bytes.ToArray()),
                errorCallback: exception => completion.TrySetException(exception),
                cancellationToken: linkedCancellation.Token), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }

        return new SpeechAudio(await completion.Task.ConfigureAwait(false), ".mp3");
    }
}
