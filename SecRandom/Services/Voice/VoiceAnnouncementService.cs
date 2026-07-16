using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SecRandom.Core;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Models.AttachedSettings;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;
using SecRandom.Shared;
using SecRandom.Shared.Extensions;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Services.Voice;

public sealed class VoiceAnnouncementService(
    MainConfigHandler configHandler,
    IEnumerable<ISpeechProvider> speechProviders,
    ISpeechAudioPlayer audioPlayer,
    ILogger<VoiceAnnouncementService> logger) : IVoiceAnnouncementService
{
    private const int VoiceNamePrefixLength = 48;
    private const int TextPrefixLength = 96;
    private readonly IReadOnlyDictionary<int, ISpeechProvider> _speechProviders = speechProviders
        .GroupBy(provider => provider.Engine)
        .ToDictionary(group => group.Key, group => group.First());
    private readonly SemaphoreSlim _speakGate = new(1, 1);

    public Task<IReadOnlyList<VoiceOption>> GetVoicesAsync(
        int engine,
        CancellationToken cancellationToken = default)
    {
        return _speechProviders.TryGetValue(engine, out var provider)
            ? provider.GetVoicesAsync(cancellationToken)
            : Task.FromResult<IReadOnlyList<VoiceOption>>([]);
    }

    public Task SpeakAsync(string text, bool waitForCompletion = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text) || !configHandler.Data.VoiceSettings.VoiceEnable)
            return Task.CompletedTask;

        var task = SpeakCoreAsync(text, cancellationToken);
        if (waitForCompletion || configHandler.Data.VoiceSettings.VoiceWaitComplete)
            return ObserveAsync(task);

        _ = ObserveAsync(task);
        return Task.CompletedTask;
    }

    public Task PreviewAsync(string text, CancellationToken cancellationToken = default)
    {
        return string.IsNullOrWhiteSpace(text)
            ? Task.CompletedTask
            : SpeakCoreAsync(text, cancellationToken);
    }

    public Task SpeakStudentsAsync(
        IEnumerable<Student> students,
        bool waitForCompletion = false,
        CancellationToken cancellationToken = default)
    {
        var settings = configHandler.Data.VoiceSettings;
        var text = string.Join("，", students
            .Select(student => BuildAnnouncementText(settings, GetSpecificSettings(student), student.Id, student.Name))
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        return SpeakAsync(text, waitForCompletion, cancellationToken);
    }

    public Task SpeakPrizesAsync(
        IEnumerable<Prize> prizes,
        bool waitForCompletion = false,
        CancellationToken cancellationToken = default)
    {
        var settings = configHandler.Data.VoiceSettings;
        var text = string.Join("，", prizes
            .Select(prize => BuildAnnouncementText(settings, GetSpecificSettings(prize), prize.Id, prize.Name))
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        return SpeakAsync(text, waitForCompletion, cancellationToken);
    }

    private async Task SpeakCoreAsync(string text, CancellationToken cancellationToken)
    {
        await _speakGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var settings = configHandler.Data.VoiceSettings;
            if (!_speechProviders.TryGetValue(settings.VoiceEngine, out var provider) ||
                provider.Engine == SystemSpeechProvider.SystemEngine && !OperatingSystem.IsWindows())
            {
                if (!_speechProviders.TryGetValue(EdgeTtsSpeechProvider.EdgeEngine, out provider))
                {
                    logger.LogWarning("Unsupported voice engine: {VoiceEngine}.", settings.VoiceEngine);
                    return;
                }

                logger.LogWarning("System TTS is unavailable on this platform. Falling back to Edge TTS.");
            }

            var voiceId = ResolveVoiceId(settings, provider.Engine);
            var path = GetCachedAudioPath(provider.Engine, voiceId, text);
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
            {
                var audio = await provider.SynthesizeAsync(
                    new SpeechSynthesisRequest(text, voiceId),
                    cancellationToken).ConfigureAwait(false);
                path = await WriteAudioAsync(path, audio, cancellationToken).ConfigureAwait(false);
            }

            await audioPlayer.PlayAsync(path, settings.VolumeSize, settings.SpeechRate, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _speakGate.Release();
        }
    }

    private static SpecificAnnouncementAttachedSettings? GetSpecificSettings(Student student) => student
        .GetAttachedObject<SpecificAnnouncementAttachedSettings>(
            Guid.Parse(GlobalConstants.SpecificAnnouncementAttachedSettings));

    private static SpecificAnnouncementAttachedSettings? GetSpecificSettings(Prize prize) => prize
        .GetAttachedObject<SpecificAnnouncementAttachedSettings>(
            Guid.Parse(GlobalConstants.SpecificAnnouncementAttachedSettings));

    private static string BuildAnnouncementText(
        VoiceSettingsConfig settings,
        SpecificAnnouncementAttachedSettings? specific,
        string id,
        string name)
    {
        var usesSpecificSettings = specific?.IsAttachSettingsEnabled == true;
        List<string> parts = [];
        AddIfNotBlank(parts, usesSpecificSettings ? specific?.Prefix : null);
        if (settings.AnnounceId)
            AddIfNotBlank(parts, id);
        if (settings.AnnounceName)
            AddIfNotBlank(parts, usesSpecificSettings && !string.IsNullOrWhiteSpace(specific?.TtsAlias) ? specific.TtsAlias : name);
        AddIfNotBlank(parts, usesSpecificSettings ? specific?.Suffix : null);
        return parts.Count == 0 ? name.Trim() : string.Join(" ", parts);
    }

    private string ResolveVoiceId(VoiceSettingsConfig settings, int engine)
    {
        if (engine == EdgeTtsSpeechProvider.EdgeEngine)
        {
            var defaultVoice = VoiceSettingsConfig.GetDefaultEdgeTtsVoiceName(configHandler.Data.General.Basic.Language);
            return string.IsNullOrWhiteSpace(settings.EdgeTtsVoiceName) ? defaultVoice : settings.EdgeTtsVoiceName;
        }

        return settings.SystemTtsVoiceName;
    }

    private static string GetCachedAudioPath(
        int engine,
        string voiceId,
        string text)
    {
        var cacheDirectory = Utils.GetDirectoryPath("audio", "voice");
        var cacheKey = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes($"{engine}\0{voiceId}\0{text}")))[..32];
        return Path.Combine(
            cacheDirectory,
            $"{GetSafeCacheFileNamePart(voiceId, VoiceNamePrefixLength)}_"
            + $"{GetSafeCacheFileNamePart(text, TextPrefixLength)}_{cacheKey}{GetAudioExtension(engine)}");
    }

    private static string GetSafeCacheFileNamePart(string value, int maximumBytes)
    {
        var safeValue = new string(value.Select(character =>
            character < ' ' || character is '"' or '*' or '/' or ':' or '<' or '>' or '?' or '\\' or '|'
                ? '_'
                : character).ToArray()).TrimEnd('.', ' ');
        if (string.IsNullOrWhiteSpace(safeValue))
            return "_";

        var builder = new StringBuilder();
        var byteCount = 0;
        foreach (var rune in safeValue.EnumerateRunes())
        {
            if (byteCount + rune.Utf8SequenceLength > maximumBytes)
                break;
            builder.Append(rune);
            byteCount += rune.Utf8SequenceLength;
        }

        return builder.Length == 0 ? "_" : builder.ToString();
    }

    private static async Task<string> WriteAudioAsync(string path, SpeechAudio audio, CancellationToken cancellationToken)
    {
        path = Path.ChangeExtension(path, audio.FileExtension);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, audio.Content, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
            return path;
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static string GetAudioExtension(int engine) => engine == EdgeTtsSpeechProvider.EdgeEngine ? ".mp3" : ".wav";

    private static void AddIfNotBlank(ICollection<string> parts, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            parts.Add(value.Trim());
    }

    private async Task ObserveAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Voice announcement failed.");
        }
    }
}
