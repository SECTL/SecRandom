using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SecRandom.Core;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Models.AttachedSettings;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;
using SecRandom.Shared;
using SecRandom.Shared.Extensions;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Services;

public sealed class VoiceAnnouncementService : IVoiceAnnouncementService
{
    private const string EdgeTrustedClientToken = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";
    private const string EdgeChromiumFullVersion = "143.0.3650.75";
    private const string EdgeChromiumMajorVersion = "143";
    private const string EdgeSecMsGecVersion = "1-" + EdgeChromiumFullVersion;
    private const long WindowsEpochSeconds = 11644473600;
    private const string EdgeVoiceListUrl =
        "https://speech.platform.bing.com/consumer/speech/synthesize/readaloud/voices/list?trustedclienttoken="
        + EdgeTrustedClientToken;
    private const string EdgeWssUrl =
        "wss://speech.platform.bing.com/consumer/speech/synthesize/readaloud/edge/v1?TrustedClientToken="
        + EdgeTrustedClientToken;

    private static readonly IReadOnlyList<VoiceOption> DefaultEdgeVoices =
    [
        new("zh-CN-XiaoxiaoNeural", "zh-CN-XiaoxiaoNeural", "Female | zh-CN"),
        new("zh-CN-YunxiNeural", "zh-CN-YunxiNeural", "Male | zh-CN"),
        new("zh-CN-XiaoyiNeural", "zh-CN-XiaoyiNeural", "Female | zh-CN"),
        new("en-US-JennyNeural", "en-US-JennyNeural", "Female | en-US"),
        new("en-US-GuyNeural", "en-US-GuyNeural", "Male | en-US")
    ];

    private readonly MainConfigHandler _configHandler;
    private readonly ILogger<VoiceAnnouncementService> _logger;
    private readonly SemaphoreSlim _speakGate = new(1, 1);
    private readonly HttpClient _httpClient = new();

    public VoiceAnnouncementService(MainConfigHandler configHandler, ILogger<VoiceAnnouncementService> logger)
    {
        _configHandler = configHandler;
        _logger = logger;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
            + $"(KHTML, like Gecko) Chrome/{EdgeChromiumMajorVersion}.0.0.0 "
            + $"Safari/537.36 Edg/{EdgeChromiumMajorVersion}.0.0.0");
        _httpClient.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("zh-CN"));
    }

    public async Task<IReadOnlyList<VoiceOption>> GetVoicesAsync(
        int engine,
        CancellationToken cancellationToken = default)
    {
        return engine switch
        {
            0 => GetSystemVoices(),
            1 => await GetEdgeVoicesAsync(cancellationToken),
            _ => []
        };
    }

    public Task SpeakAsync(string text, bool waitForCompletion = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Task.CompletedTask;

        var settings = _configHandler.Data.VoiceSettings;
        if (!settings.VoiceEnable)
            return Task.CompletedTask;

        var task = SpeakCoreAsync(text, settings, cancellationToken);
        if (waitForCompletion || settings.VoiceWaitComplete)
            return task;

        _ = ObserveAsync(task);
        return Task.CompletedTask;
    }

    public Task SpeakStudentsAsync(
        IEnumerable<Student> students,
        bool waitForCompletion = false,
        CancellationToken cancellationToken = default)
    {
        var settings = _configHandler.Data.VoiceSettings;
        var texts = students
            .Select(student => BuildStudentAnnouncement(settings, student))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text!)
            .ToList();

        return SpeakAsync(string.Join("，", texts), waitForCompletion, cancellationToken);
    }

    public Task SpeakPrizesAsync(
        IEnumerable<Prize> prizes,
        bool waitForCompletion = false,
        CancellationToken cancellationToken = default)
    {
        var settings = _configHandler.Data.VoiceSettings;
        var texts = prizes
            .Select(prize => BuildPrizeAnnouncement(settings, prize))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text!)
            .ToList();

        return SpeakAsync(string.Join("，", texts), waitForCompletion, cancellationToken);
    }

    private async Task SpeakCoreAsync(
        string text,
        VoiceSettingsConfig settings,
        CancellationToken cancellationToken)
    {
        await _speakGate.WaitAsync(cancellationToken);
        try
        {
            switch (settings.VoiceEngine)
            {
                case 0:
                    await SpeakWithSystemTtsAsync(text, settings, cancellationToken);
                    break;
                case 1:
                    await SpeakWithEdgeTtsAsync(text, settings, cancellationToken);
                    break;
                default:
                    _logger.LogWarning("Unsupported voice engine: {VoiceEngine}.", settings.VoiceEngine);
                    break;
            }
        }
        finally
        {
            _speakGate.Release();
        }
    }

    private static string? BuildStudentAnnouncement(VoiceSettingsConfig settings, Student student)
    {
        var specific = settings.SpecificAnnouncementsEnabled
            ? student.GetAttachedObject<SpecificAnnouncementAttachedSettings>(
                Guid.Parse(GlobalConstants.SpecificAnnouncementAttachedSettings))
            : null;
        return BuildAnnouncementText(
            settings,
            specific,
            student.Id,
            student.Name);
    }

    private static string? BuildPrizeAnnouncement(VoiceSettingsConfig settings, Prize prize)
    {
        var specific = settings.SpecificAnnouncementsEnabled
            ? prize.GetAttachedObject<SpecificAnnouncementAttachedSettings>(
                Guid.Parse(GlobalConstants.SpecificAnnouncementAttachedSettings))
            : null;
        return BuildAnnouncementText(
            settings,
            specific,
            prize.Id,
            prize.Name);
    }

    private static string? BuildAnnouncementText(
        VoiceSettingsConfig settings,
        SpecificAnnouncementAttachedSettings? specific,
        string id,
        string name)
    {
        var useSpecific = settings.SpecificAnnouncementsEnabled && specific?.IsAttachSettingsEnabled == true;
        List<string> parts = [];
        var prefix = useSpecific && !string.IsNullOrWhiteSpace(specific?.Prefix)
            ? specific.Prefix
            : settings.AnnouncementPrefix;
        var suffix = useSpecific && !string.IsNullOrWhiteSpace(specific?.Suffix)
            ? specific.Suffix
            : settings.AnnouncementSuffix;
        var spokenName = useSpecific && !string.IsNullOrWhiteSpace(specific?.TtsAlias)
            ? specific.TtsAlias
            : name;

        AddIfNotBlank(parts, prefix);
        if (settings.AnnounceId)
            AddIfNotBlank(parts, id);
        if (settings.AnnounceName)
            AddIfNotBlank(parts, spokenName);
        AddIfNotBlank(parts, suffix);

        return parts.Count == 0 ? name : string.Join(" ", parts);
    }

    private async Task SpeakWithSystemTtsAsync(
        string text,
        VoiceSettingsConfig settings,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            _logger.LogWarning("System TTS is only available on Windows.");
            return;
        }

#pragma warning disable CA1416
        await Task.Run(
            () => SpeakWithSystemTtsWindows(text, settings, cancellationToken),
            cancellationToken);
#pragma warning restore CA1416
    }

    [SupportedOSPlatform("windows")]
    private static void SpeakWithSystemTtsWindows(
        string text,
        VoiceSettingsConfig settings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var type = Type.GetTypeFromProgID("SAPI.SpVoice");
        if (type == null)
            throw new InvalidOperationException("SAPI.SpVoice is not available.");

        dynamic voice = Activator.CreateInstance(type)!;
        try
        {
            var selectedVoice = FindSapiVoice(voice, settings.SystemTtsVoiceName);
            if (selectedVoice != null)
                voice.Voice = selectedVoice;

            voice.Volume = Math.Clamp(settings.VolumeSize, 0, 100);
            voice.Rate = MapSapiRate(settings.SpeechRate);
            voice.Speak(text, 0);
        }
        finally
        {
            Marshal.FinalReleaseComObject(voice);
        }
    }

    private async Task SpeakWithEdgeTtsAsync(
        string text,
        VoiceSettingsConfig settings,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            _logger.LogWarning("Edge TTS playback currently requires Windows MCI playback.");
            return;
        }

        var voice = string.IsNullOrWhiteSpace(settings.EdgeTtsVoiceName)
            ? "zh-CN-XiaoxiaoNeural"
            : settings.EdgeTtsVoiceName;
        var path = await GetOrCreateEdgeAudioAsync(text, voice, settings.SpeechRate, cancellationToken);
        await PlayAudioFileAsync(path, settings.VolumeSize, cancellationToken);
    }

    private IReadOnlyList<VoiceOption> GetSystemVoices()
    {
        if (!OperatingSystem.IsWindows())
            return [];

        var type = Type.GetTypeFromProgID("SAPI.SpVoice");
        if (type == null)
            return [];

        dynamic voice = Activator.CreateInstance(type)!;
        try
        {
            dynamic voices = voice.GetVoices();
            List<VoiceOption> result = [];
            for (var i = 0; i < voices.Count; i++)
            {
                dynamic item = voices.Item(i);
                var id = Convert.ToString(item.Id) ?? string.Empty;
                var name = Convert.ToString(item.GetDescription()) ?? id;
                result.Add(new VoiceOption(id, name));
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate system TTS voices.");
            return [];
        }
        finally
        {
            Marshal.FinalReleaseComObject(voice);
        }
    }

    private async Task<IReadOnlyList<VoiceOption>> GetEdgeVoicesAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(EdgeVoiceListUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            var voices = document.RootElement.EnumerateArray()
                .Select(ReadEdgeVoice)
                .Where(voice => voice != null)
                .Select(voice => voice!)
                .OrderBy(voice => !voice.Id.StartsWith("zh-CN", StringComparison.OrdinalIgnoreCase))
                .ThenBy(voice => voice.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return voices.Count == 0 ? DefaultEdgeVoices : voices;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Edge TTS voices. Falling back to built-in voices.");
            return DefaultEdgeVoices;
        }
    }

    private static VoiceOption? ReadEdgeVoice(JsonElement element)
    {
        if (!element.TryGetProperty("ShortName", out var shortNameProperty))
            return null;

        var shortName = shortNameProperty.GetString();
        if (string.IsNullOrWhiteSpace(shortName))
            return null;

        var displayName = element.TryGetProperty("DisplayName", out var displayNameProperty)
            ? displayNameProperty.GetString()
            : shortName;
        var locale = element.TryGetProperty("Locale", out var localeProperty)
            ? localeProperty.GetString()
            : string.Empty;
        var gender = element.TryGetProperty("Gender", out var genderProperty)
            ? genderProperty.GetString()
            : string.Empty;

        return new VoiceOption(shortName, $"{shortName} ({displayName})", $"{gender} | {locale}".Trim(' ', '|'));
    }

    private async Task<string> GetOrCreateEdgeAudioAsync(
        string text,
        string voice,
        int rate,
        CancellationToken cancellationToken)
    {
        var cacheDir = Utils.GetDirectoryPath("audio", "voice-cache");
        var cacheKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{voice}|{rate}|{text}")));
        var path = Path.Combine(cacheDir, $"{cacheKey}.mp3");
        if (File.Exists(path) && new FileInfo(path).Length > 0)
            return path;

        var bytes = await SynthesizeEdgeAudioAsync(text, voice, rate, cancellationToken);
        await File.WriteAllBytesAsync(path, bytes, cancellationToken);
        return path;
    }

    private async Task<byte[]> SynthesizeEdgeAudioAsync(
        string text,
        string voice,
        int rate,
        CancellationToken cancellationToken)
    {
        var connectionId = CreateConnectionId();
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Pragma", "no-cache");
        socket.Options.SetRequestHeader("Cache-Control", "no-cache");
        socket.Options.SetRequestHeader("Origin", "chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold");
        socket.Options.SetRequestHeader(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
            + $"(KHTML, like Gecko) Chrome/{EdgeChromiumMajorVersion}.0.0.0 "
            + $"Safari/537.36 Edg/{EdgeChromiumMajorVersion}.0.0.0");
        socket.Options.SetRequestHeader("Accept-Language", "zh-CN,zh;q=0.9");
        socket.Options.SetRequestHeader("Cookie", $"muid={CreateConnectionId().ToUpperInvariant()};");

        var uri = new Uri(
            EdgeWssUrl
            + $"&ConnectionId={connectionId}"
            + $"&Sec-MS-GEC={CreateSecMsGec()}"
            + $"&Sec-MS-GEC-Version={EdgeSecMsGecVersion}");
        await socket.ConnectAsync(uri, cancellationToken);

        await SendTextAsync(socket, CreateSpeechConfigMessage(), cancellationToken);
        await SendTextAsync(socket, CreateSsmlMessage(text, voice, rate), cancellationToken);

        using var audio = new MemoryStream();
        while (socket.State == WebSocketState.Open)
        {
            var message = await ReceiveWebSocketMessageAsync(socket, cancellationToken);
            if (message.MessageType == WebSocketMessageType.Close)
                break;

            if (message.MessageType == WebSocketMessageType.Text)
            {
                var textMessage = Encoding.UTF8.GetString(message.Payload);
                if (textMessage.Contains("Path:turn.end", StringComparison.OrdinalIgnoreCase))
                    break;
                continue;
            }

            var payload = StripBinaryWebSocketHeaders(message.Payload);
            if (payload.Length > 0)
                audio.Write(payload);
        }

        if (audio.Length == 0)
            throw new InvalidOperationException("No audio was received from Edge TTS.");

        return audio.ToArray();
    }

    private static async Task SendTextAsync(
        ClientWebSocket socket,
        string message,
        CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }

    private static async Task<WebSocketMessage> ReceiveWebSocketMessageAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        using var stream = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken);
            stream.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        return new WebSocketMessage(result.MessageType, stream.ToArray());
    }

    private static string CreateSpeechConfigMessage()
    {
        return $"X-Timestamp:{CreateEdgeTimestamp()}\r\n" +
               "Content-Type:application/json; charset=utf-8\r\n" +
               "Path:speech.config\r\n\r\n" +
               "{\"context\":{\"synthesis\":{\"audio\":{\"metadataoptions\":{" +
               "\"sentenceBoundaryEnabled\":\"false\",\"wordBoundaryEnabled\":\"false\"}," +
               "\"outputFormat\":\"audio-24khz-48kbitrate-mono-mp3\"}}}}\r\n";
    }

    private static string CreateSsmlMessage(string text, string voice, int rate)
    {
        var escapedText = System.Security.SecurityElement.Escape(text) ?? string.Empty;
        var escapedVoice = System.Security.SecurityElement.Escape(voice) ?? "zh-CN-XiaoxiaoNeural";
        var rateDelta = Math.Clamp(rate - 100, -100, 100);
        var requestId = Guid.NewGuid().ToString("N").ToUpperInvariant();

        return $"X-RequestId:{requestId}\r\n" +
               "Content-Type:application/ssml+xml\r\n" +
               $"X-Timestamp:{CreateEdgeTimestamp()}Z\r\n" +
               "Path:ssml\r\n\r\n" +
               "<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' " +
               "xmlns:mstts='https://www.w3.org/2001/mstts' xml:lang='zh-CN'>" +
               $"<voice name='{escapedVoice}'><prosody rate='{rateDelta:+0;-0;0}%'>{escapedText}</prosody></voice>" +
               "</speak>";
    }

    private static string CreateEdgeTimestamp()
    {
        return DateTimeOffset.UtcNow.ToString("ddd MMM dd yyyy HH:mm:ss 'GMT+0000 (Coordinated Universal Time)'", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string CreateConnectionId()
    {
        return Guid.NewGuid().ToString("N");
    }

    private static string CreateSecMsGec()
    {
        var unixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var roundedSeconds = unixSeconds - unixSeconds % 300;
        var windowsTicks = (roundedSeconds + WindowsEpochSeconds) * 10_000_000L;
        var payload = $"{windowsTicks}{EdgeTrustedClientToken}";
        return Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(payload)));
    }

    private static byte[] StripBinaryWebSocketHeaders(byte[] message)
    {
        if (message.Length < 2)
            return [];

        var headerLength = (message[0] << 8) | message[1];
        var payloadOffset = headerLength + 2;
        return payloadOffset > message.Length ? [] : message[payloadOffset..];
    }

    private static async Task PlayAudioFileAsync(string path, int volume, CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var alias = "SecRandomVoice" + Guid.NewGuid().ToString("N");
            var normalizedVolume = Math.Clamp(volume, 0, 100) * 10;
            try
            {
                MciSendString($"open \"{path}\" type mpegvideo alias {alias}", null, 0, IntPtr.Zero);
                MciSendString($"setaudio {alias} volume to {normalizedVolume}", null, 0, IntPtr.Zero);
                MciSendString($"play {alias} wait", null, 0, IntPtr.Zero);
            }
            finally
            {
                MciSendString($"close {alias}", null, 0, IntPtr.Zero);
            }
        }, cancellationToken);
    }

    private static dynamic? FindSapiVoice(dynamic voice, string voiceId)
    {
        if (string.IsNullOrWhiteSpace(voiceId))
            return null;

        dynamic voices = voice.GetVoices();
        for (var i = 0; i < voices.Count; i++)
        {
            dynamic item = voices.Item(i);
            var id = Convert.ToString(item.Id) ?? string.Empty;
            var description = Convert.ToString(item.GetDescription()) ?? string.Empty;
            if (string.Equals(id, voiceId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(description, voiceId, StringComparison.OrdinalIgnoreCase))
                return item;
        }

        return null;
    }

    private static int MapSapiRate(int speechRate)
    {
        return Math.Clamp((speechRate - 100) / 10, -10, 10);
    }

    private static void AddIfNotBlank(ICollection<string> parts, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
            parts.Add(text.Trim());
    }

    private async Task ObserveAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Voice announcement failed.");
        }
    }

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "mciSendStringW")]
    private static extern int MciSendString(string command, StringBuilder? returnValue, int returnLength, IntPtr hwndCallback);

    private sealed record WebSocketMessage(WebSocketMessageType MessageType, byte[] Payload);
}
