using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction.Services;

namespace SecRandom.Services.Voice;

public sealed class SystemSpeechProvider(ILogger<SystemSpeechProvider> logger) : ISpeechProvider
{
    public const int SystemEngine = 0;

    public int Engine => SystemEngine;

    public Task<IReadOnlyList<VoiceOption>> GetVoicesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<VoiceOption>>(OperatingSystem.IsWindows() ? GetWindowsVoices() : []);
    }

    public Task<SpeechAudio> SynthesizeAsync(SpeechSynthesisRequest request, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("System TTS is only available on Windows.");

#pragma warning disable CA1416
        return RunOnStaThread(() => SynthesizeWindows(request, cancellationToken));
#pragma warning restore CA1416
    }

    [SupportedOSPlatform("windows")]
    private static Task<T> RunOnStaThread<T>(Func<T> operation)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.TrySetResult(operation());
            }
            catch (OperationCanceledException exception)
            {
                completion.TrySetCanceled(exception.CancellationToken);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "SecRandom System Speech"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    [SupportedOSPlatform("windows")]
    private IReadOnlyList<VoiceOption> GetWindowsVoices()
    {
        var type = Type.GetTypeFromProgID("SAPI.SpVoice");
        if (type is null)
            return [];

        dynamic voice = Activator.CreateInstance(type)!;
        try
        {
            dynamic voices = voice.GetVoices();
            List<VoiceOption> result = [];
            for (var index = 0; index < voices.Count; index++)
            {
                dynamic item = voices.Item(index);
                var id = Convert.ToString(item.Id) ?? string.Empty;
                result.Add(new VoiceOption(id, Convert.ToString(item.GetDescription()) ?? id));
            }

            return result;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to enumerate system TTS voices.");
            return [];
        }
        finally
        {
            Marshal.FinalReleaseComObject(voice);
        }
    }

    [SupportedOSPlatform("windows")]
    private static SpeechAudio SynthesizeWindows(SpeechSynthesisRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var voiceType = Type.GetTypeFromProgID("SAPI.SpVoice")
            ?? throw new InvalidOperationException("SAPI.SpVoice is not available.");
        var streamType = Type.GetTypeFromProgID("SAPI.SpFileStream")
            ?? throw new InvalidOperationException("SAPI.SpFileStream is not available.");
        var temporaryPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.wav");
        dynamic voice = Activator.CreateInstance(voiceType)!;
        dynamic stream = Activator.CreateInstance(streamType)!;
        try
        {
            stream.Open(temporaryPath, 3, false);
            var selectedVoice = FindVoice(voice, request.VoiceId);
            if (selectedVoice is not null)
                voice.Voice = selectedVoice;

            voice.Rate = 0;
            voice.AudioOutputStream = stream;
            voice.Speak(request.Text, 0);
            stream.Close();
            cancellationToken.ThrowIfCancellationRequested();
            return new SpeechAudio(File.ReadAllBytes(temporaryPath), ".wav");
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
            Marshal.FinalReleaseComObject(stream);
            Marshal.FinalReleaseComObject(voice);
        }
    }

    [SupportedOSPlatform("windows")]
    private static dynamic? FindVoice(dynamic voice, string voiceId)
    {
        if (string.IsNullOrWhiteSpace(voiceId))
            return null;

        dynamic voices = voice.GetVoices();
        for (var index = 0; index < voices.Count; index++)
        {
            dynamic item = voices.Item(index);
            var id = Convert.ToString(item.Id) ?? string.Empty;
            var description = Convert.ToString(item.GetDescription()) ?? string.Empty;
            if (string.Equals(id, voiceId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(description, voiceId, StringComparison.OrdinalIgnoreCase))
                return item;
        }

        return null;
    }
}
