using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;

namespace SecRandom.Services.Voice;

/// <summary>
/// OmniTTS unified cloud speech engine (engine 2).
/// Providers are OpenAI-compatible REST endpoints plus native MiMo and Gemini adapters.
/// Models are fetched from the provider API when available or typed manually; documented
/// provider presets are used for OpenAI, MiMo, and Gemini voices.
/// </summary>
public sealed class OmniTtsSpeechProvider(
    MainConfigHandler configHandler,
    OmniTtsCredentialStore credentialStore,
    MiMoVoiceReferenceStore miMoVoiceReferenceStore,
    IHttpClientFactory httpClientFactory,
    ILogger<OmniTtsSpeechProvider> logger) : ISpeechProvider, IOmniTtsCatalog
{
    public const int OmniEngine = 2;
    public const string OpenAiDefaultBaseUrl = "https://api.openai.com/v1";
    public const string FishAudioDefaultBaseUrl = "https://api.fish.audio";
    public const string MiMoDefaultBaseUrl = "https://api.xiaomimimo.com";
    public const string GeminiDefaultBaseUrl = "https://generativelanguage.googleapis.com/v1beta";
    public const string MiMoVoiceCloneModel = "mimo-v2.5-tts-voiceclone";
    public const string MiMoVoiceDesignModel = "mimo-v2.5-tts-voicedesign";

    private static readonly IReadOnlyList<VoiceOption> OpenAiPresetVoices =
    [
        new("alloy", "alloy"),
        new("ash", "ash"),
        new("ballad", "ballad"),
        new("coral", "coral"),
        new("echo", "echo"),
        new("fable", "fable"),
        new("nova", "nova"),
        new("onyx", "onyx"),
        new("sage", "sage"),
        new("shimmer", "shimmer"),
        new("verse", "verse"),
        new("marin", "marin"),
        new("cedar", "cedar")
    ];

    private static readonly IReadOnlyList<VoiceOption> MiMoPresetVoices =
    [
        new("mimo_default", "MiMo default"),
        new("冰糖", "冰糖"),
        new("茉莉", "茉莉"),
        new("苏打", "苏打"),
        new("白桦", "白桦"),
        new("Mia", "Mia"),
        new("Chloe", "Chloe"),
        new("Milo", "Milo"),
        new("Dean", "Dean")
    ];

    private static readonly IReadOnlyList<VoiceOption> GeminiPresetVoices =
    [
        new("Zephyr", "Zephyr"),
        new("Puck", "Puck"),
        new("Charon", "Charon"),
        new("Kore", "Kore"),
        new("Fenrir", "Fenrir"),
        new("Leda", "Leda"),
        new("Orus", "Orus"),
        new("Aoede", "Aoede"),
        new("Callirrhoe", "Callirrhoe"),
        new("Autonoe", "Autonoe"),
        new("Enceladus", "Enceladus"),
        new("Iapetus", "Iapetus"),
        new("Umbriel", "Umbriel"),
        new("Algieba", "Algieba"),
        new("Despina", "Despina"),
        new("Erinome", "Erinome"),
        new("Algenib", "Algenib"),
        new("Rasalgethi", "Rasalgethi"),
        new("Laomedeia", "Laomedeia"),
        new("Achernar", "Achernar"),
        new("Alnilam", "Alnilam"),
        new("Schedar", "Schedar"),
        new("Gacrux", "Gacrux"),
        new("Pulcherrima", "Pulcherrima"),
        new("Achird", "Achird"),
        new("Zubenelgenubi", "Zubenelgenubi"),
        new("Vindemiatrix", "Vindemiatrix"),
        new("Sadachbia", "Sadachbia"),
        new("Sadaltager", "Sadaltager"),
        new("Sulafat", "Sulafat")
    ];

    public int Engine => OmniEngine;

    private VoiceSettingsConfig Settings => configHandler.Data.VoiceSettings;

    // The provider is a singleton: reuse one pooled HttpClient for its whole lifetime
    // instead of creating/disposing a client per synthesis request.
    private readonly Lazy<HttpClient> _http = new(() => httpClientFactory.CreateClient("omnitts"));

    public static string GetDefaultBaseUrl(OmniTtsProvider provider) => provider switch
    {
        OmniTtsProvider.FishAudio => FishAudioDefaultBaseUrl,
        OmniTtsProvider.MiMo => MiMoDefaultBaseUrl,
        OmniTtsProvider.Gemini => GeminiDefaultBaseUrl,
        _ => OpenAiDefaultBaseUrl
    };

    public static bool IsMiMoVoiceCloneModel(string? model) => string.Equals(
        model,
        MiMoVoiceCloneModel,
        StringComparison.OrdinalIgnoreCase);

    public static bool IsMiMoVoiceDesignModel(string? model) => string.Equals(
        model,
        MiMoVoiceDesignModel,
        StringComparison.OrdinalIgnoreCase);

    public Task<IReadOnlyList<VoiceOption>> GetVoicesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Settings.OmniTtsProvider switch
        {
            OmniTtsProvider.OpenAi => Task.FromResult(OpenAiPresetVoices),
            OmniTtsProvider.FishAudio => GetFishAudioVoicesAsync(cancellationToken),
            OmniTtsProvider.MiMo => Task.FromResult(MiMoPresetVoices),
            OmniTtsProvider.Gemini => Task.FromResult(GeminiPresetVoices),
            _ => Task.FromResult<IReadOnlyList<VoiceOption>>([])
        };
    }

    public async Task<IReadOnlyList<string>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        var provider = Settings.OmniTtsProvider;
        var key = credentialStore.GetKey(provider);
        if (string.IsNullOrWhiteSpace(key))
            return [];

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, BuildModelsUrl(Settings.OmniTtsApiBaseUrl, provider));
            AddApiKeyHeader(request, provider, key);
            using var response = await _http.Value
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("OmniTTS model list request failed with status {Status}.", response.StatusCode);
                return [];
            }

            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var models = ResolveModelIds(document.RootElement, provider);
            return models
                .Where(id => IsTtsModelId(id))
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException or JsonException)
        {
            logger.LogWarning(exception, "Failed to fetch OmniTTS model list.");
            return [];
        }
    }

    public async Task<SpeechAudio> SynthesizeAsync(SpeechSynthesisRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var provider = Settings.OmniTtsProvider;
        var key = credentialStore.GetKey(provider);
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("OmniTTS API key is not configured.");
        if (string.IsNullOrWhiteSpace(Settings.OmniTtsModel))
            throw new InvalidOperationException("OmniTTS model is not selected.");
        var isMiMoSpecialModel = provider == OmniTtsProvider.MiMo &&
                                (IsMiMoVoiceCloneModel(Settings.OmniTtsModel) ||
                                 IsMiMoVoiceDesignModel(Settings.OmniTtsModel));
        if (!isMiMoSpecialModel && string.IsNullOrWhiteSpace(request.VoiceId))
            throw new InvalidOperationException("OmniTTS voice is not selected.");
        if (provider == OmniTtsProvider.MiMo &&
            IsMiMoVoiceDesignModel(Settings.OmniTtsModel) &&
            string.IsNullOrWhiteSpace(Settings.MiMoVoiceDesignPrompt))
        {
            throw new InvalidOperationException("MiMo voice-design description is not configured.");
        }

        return provider switch
        {
            OmniTtsProvider.MiMo => await SynthesizeMiMoAsync(request, key, cancellationToken).ConfigureAwait(false),
            OmniTtsProvider.Gemini => await SynthesizeGeminiAsync(request, key, cancellationToken).ConfigureAwait(false),
            _ => await SynthesizeOpenAiCompatibleAsync(request, provider, key, cancellationToken).ConfigureAwait(false)
        };
    }

    private async Task<SpeechAudio> SynthesizeOpenAiCompatibleAsync(
        SpeechSynthesisRequest request,
        OmniTtsProvider provider,
        string key,
        CancellationToken cancellationToken)
    {
        var settings = Settings;
        var baseUrl = settings.OmniTtsApiBaseUrl.TrimEnd('/');
        if (!baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            baseUrl += "/v1";
        var url = $"{baseUrl}/audio/speech";

        var body = new Dictionary<string, object?>
        {
            ["model"] = settings.OmniTtsModel,
            ["input"] = request.Text,
            ["voice"] = request.VoiceId,
            ["response_format"] = "mp3"
        };
        if (provider == OmniTtsProvider.OpenAi
            && string.Equals(settings.OmniTtsModel, "gpt-4o-mini-tts", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(settings.OmniTtsInstructions))
        {
            body["instructions"] = settings.OmniTtsInstructions;
        }

        using var apiRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body)
        };
        apiRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {key}");
        using var response = await _http.Value.SendAsync(apiRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw await CreateHttpErrorAsync(response, cancellationToken).ConfigureAwait(false);

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (bytes.Length == 0)
            throw new InvalidOperationException("OmniTTS returned an empty audio response.");
        return new SpeechAudio(bytes, ".mp3");
    }

    private async Task<SpeechAudio> SynthesizeMiMoAsync(
        SpeechSynthesisRequest request,
        string key,
        CancellationToken cancellationToken)
    {
        var settings = Settings;
        var isVoiceClone = IsMiMoVoiceCloneModel(settings.OmniTtsModel);
        var isVoiceDesign = IsMiMoVoiceDesignModel(settings.OmniTtsModel);
        var baseUrl = settings.OmniTtsApiBaseUrl.TrimEnd('/');
        if (baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            baseUrl = baseUrl[..^3];
        var url = $"{baseUrl}/v1/chat/completions";

        var messages = isVoiceClone
            ? new object[]
            {
                new { role = "user", content = settings.OmniTtsInstructions },
                new { role = "assistant", content = request.Text }
            }
            : isVoiceDesign
                ? new object[]
                {
                    new { role = "user", content = settings.MiMoVoiceDesignPrompt },
                    new { role = "assistant", content = request.Text }
                }
                : new object[] { new { role = "assistant", content = request.Text } };
        var outputFormat = isVoiceClone || isVoiceDesign ? "wav" : "mp3";
        var audio = new Dictionary<string, object?>
        {
            ["format"] = outputFormat
        };
        if (isVoiceClone)
        {
            audio["voice"] = await miMoVoiceReferenceStore
                .GetDataUriAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        else if (isVoiceDesign)
        {
            audio["optimize_text_preview"] = true;
        }
        else
        {
            audio["voice"] = request.VoiceId;
        }

        var body = new Dictionary<string, object?>
        {
            ["model"] = settings.OmniTtsModel,
            ["messages"] = messages,
            ["audio"] = audio,
            ["stream"] = false
        };

        using var apiRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body)
        };
        apiRequest.Headers.TryAddWithoutValidation("api-key", key);
        using var response = await _http.Value.SendAsync(apiRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw await CreateHttpErrorAsync(response, cancellationToken).ConfigureAwait(false);

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var audioData = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("audio")
            .GetProperty("data")
            .GetString();
        if (string.IsNullOrWhiteSpace(audioData))
            throw new InvalidOperationException("MiMo returned no audio data.");

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(audioData);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("MiMo returned invalid audio data.", exception);
        }

        return new SpeechAudio(bytes, outputFormat == "wav" ? ".wav" : ".mp3");
    }

    private async Task<SpeechAudio> SynthesizeGeminiAsync(
        SpeechSynthesisRequest request,
        string key,
        CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>
        {
            ["model"] = Settings.OmniTtsModel,
            ["input"] = request.Text,
            ["response_format"] = new Dictionary<string, object?>
            {
                ["type"] = "audio"
            },
            ["generation_config"] = new Dictionary<string, object?>
            {
                ["speech_config"] = new object[]
                {
                    new Dictionary<string, object?>
                    {
                        ["voice"] = request.VoiceId
                    }
                }
            }
        };

        using var apiRequest = new HttpRequestMessage(HttpMethod.Post, $"{BuildGeminiApiBaseUrl(Settings.OmniTtsApiBaseUrl)}/interactions")
        {
            Content = JsonContent.Create(body)
        };
        AddApiKeyHeader(apiRequest, OmniTtsProvider.Gemini, key);
        using var response = await _http.Value.SendAsync(apiRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw await CreateHttpErrorAsync(response, cancellationToken).ConfigureAwait(false);

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!TryGetProperty(document.RootElement, out var outputAudio, "output_audio", "outputAudio") ||
            !TryGetProperty(outputAudio, out var data, "data") ||
            string.IsNullOrWhiteSpace(data.GetString()))
        {
            throw new InvalidOperationException("Gemini returned no audio data.");
        }

        byte[] pcm;
        try
        {
            pcm = Convert.FromBase64String(data.GetString()!);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("Gemini returned invalid audio data.", exception);
        }

        if (pcm.Length == 0)
            throw new InvalidOperationException("Gemini returned an empty audio response.");

        return new SpeechAudio(CreatePcmWaveFile(pcm), ".wav");
    }

    private async Task<IReadOnlyList<VoiceOption>> GetFishAudioVoicesAsync(CancellationToken cancellationToken)
    {
        var key = credentialStore.GetKey(OmniTtsProvider.FishAudio);
        if (string.IsNullOrWhiteSpace(key))
            return [];

        try
        {
            var baseUrl = Settings.OmniTtsApiBaseUrl.TrimEnd('/');
            if (!baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
                baseUrl += "/v1";
            var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/voices");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {key}");
            using var response = await _http.Value
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return [];

            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var items = ResolveJsonArray(document.RootElement, "items", "voices");
            if (items is null)
                return [];

            return items.Value.EnumerateArray()
                .Select(item => item.TryGetProperty("voice_id", out var voiceId)
                    ? (voiceId.GetString(), item.TryGetProperty("name", out var name) ? name.GetString() : null)
                    : (item.TryGetProperty("id", out var id) ? id.GetString() : null, null))
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Item1))
                .Select(entry => new VoiceOption(
                    entry.Item1!,
                    string.IsNullOrWhiteSpace(entry.Item2) ? entry.Item1! : entry.Item2!))
                .ToList();
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException or JsonException)
        {
            logger.LogWarning(exception, "Failed to fetch FishAudio voice list.");
            return [];
        }
    }

    private static async Task<Exception> CreateHttpErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string detail;
        try
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            detail = content.Length <= 300 ? content : content[..300];
        }
        catch (Exception)
        {
            detail = string.Empty;
        }

        return new InvalidOperationException(
            $"OmniTTS request failed with status {(int)response.StatusCode} ({response.ReasonPhrase}). {detail}".Trim());
    }

    private static JsonElement? ResolveJsonArray(JsonElement root, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array)
                return value;
        }

        return root.ValueKind == JsonValueKind.Array ? root : null;
    }

    private static IReadOnlyList<string> ResolveModelIds(JsonElement root, OmniTtsProvider provider)
    {
        var arrayName = provider == OmniTtsProvider.Gemini ? "models" : "data";
        var identifierName = provider == OmniTtsProvider.Gemini ? "name" : "id";
        if (!root.TryGetProperty(arrayName, out var models) || models.ValueKind != JsonValueKind.Array)
            return [];

        return models.EnumerateArray()
            .Select(item => item.TryGetProperty(identifierName, out var id) ? id.GetString() : null)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => provider == OmniTtsProvider.Gemini && id!.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
                ? id["models/".Length..]
                : id!)
            .ToArray();
    }

    private static void AddApiKeyHeader(HttpRequestMessage request, OmniTtsProvider provider, string key)
    {
        if (provider == OmniTtsProvider.Gemini)
            request.Headers.TryAddWithoutValidation("x-goog-api-key", key);
        else if (provider == OmniTtsProvider.MiMo)
            request.Headers.TryAddWithoutValidation("api-key", key);
        else
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {key}");
    }

    private static bool TryGetProperty(JsonElement element, out JsonElement value, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (element.TryGetProperty(propertyName, out value))
                return true;
        }

        value = default;
        return false;
    }

    private static byte[] CreatePcmWaveFile(byte[] pcm)
    {
        const int headerSize = 44;
        const int sampleRate = 24000;
        const short channelCount = 1;
        const short bitsPerSample = 16;
        const short blockAlign = channelCount * (bitsPerSample / 8);
        var wave = new byte[checked(headerSize + pcm.Length)];
        var span = wave.AsSpan();

        span[0] = (byte)'R';
        span[1] = (byte)'I';
        span[2] = (byte)'F';
        span[3] = (byte)'F';
        BinaryPrimitives.WriteInt32LittleEndian(span[4..8], 36 + pcm.Length);
        span[8] = (byte)'W';
        span[9] = (byte)'A';
        span[10] = (byte)'V';
        span[11] = (byte)'E';
        span[12] = (byte)'f';
        span[13] = (byte)'m';
        span[14] = (byte)'t';
        span[15] = (byte)' ';
        BinaryPrimitives.WriteInt32LittleEndian(span[16..20], 16);
        BinaryPrimitives.WriteInt16LittleEndian(span[20..22], 1);
        BinaryPrimitives.WriteInt16LittleEndian(span[22..24], channelCount);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..28], sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..32], sampleRate * blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(span[32..34], blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(span[34..36], bitsPerSample);
        span[36] = (byte)'d';
        span[37] = (byte)'a';
        span[38] = (byte)'t';
        span[39] = (byte)'a';
        BinaryPrimitives.WriteInt32LittleEndian(span[40..44], pcm.Length);
        pcm.CopyTo(span[headerSize..]);
        return wave;
    }

    private static bool IsTtsModelId(string modelId)
    {
        var lowered = modelId.ToLowerInvariant();
        return lowered.Contains("tts", StringComparison.Ordinal)
               || lowered.Contains("audio", StringComparison.Ordinal)
               || lowered.Contains("speech", StringComparison.Ordinal);
    }

    private static string BuildModelsUrl(string baseUrl, OmniTtsProvider provider)
    {
        var normalized = baseUrl.TrimEnd('/');
        return provider switch
        {
            OmniTtsProvider.Gemini => $"{BuildGeminiApiBaseUrl(normalized)}/models",
            OmniTtsProvider.MiMo => normalized.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
                ? $"{normalized}/models"
                : $"{normalized}/v1/models",
            _ => normalized.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
                ? $"{normalized}/models"
                : $"{normalized}/v1/models"
        };
    }

    private static string BuildGeminiApiBaseUrl(string baseUrl)
    {
        var normalized = baseUrl.TrimEnd('/');
        return normalized.EndsWith("/v1beta", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : $"{normalized}/v1beta";
    }

}
