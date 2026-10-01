using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;
using SecRandom.Services.Voice;
using SecRandom.Shared.Models.Profile;
using Xunit;

namespace SecRandom.Core.Tests;

public class OmniTtsSpeechProviderTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "SecRandom", "omnitts-tests", Guid.NewGuid().ToString("N"));

    public OmniTtsSpeechProviderTests()
    {
        InvokeUtils("ResetDataRootForTests");
        InvokeUtils("ConfigureDataRoot", _dataRoot);
    }

    public void Dispose()
    {
        InvokeUtils("ResetDataRootForTests");
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    private static void InvokeUtils(string name, params object[] arguments) =>
        (typeof(SecRandom.Shared.Utils).GetMethod(name,
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
         ?? throw new InvalidOperationException($"Utils.{name} was not found."))
        .Invoke(null, arguments);

    [Theory]
    [InlineData(OmniTtsProvider.OpenAi)]
    [InlineData(OmniTtsProvider.MiMo)]
    [InlineData(OmniTtsProvider.Gemini)]
    [InlineData(OmniTtsProvider.FishAudio)]
    public async Task F04_LegacyProviderOnlyCredentialCannotAuthorizeRequests(OmniTtsProvider selectedProvider)
    {
        var handler = CreateHandler(config =>
        {
            config.VoiceSettings.OmniTtsProvider = selectedProvider;
            config.VoiceSettings.OmniTtsApiBaseUrl = OmniTtsSpeechProvider.GetDefaultBaseUrl(selectedProvider);
            config.VoiceSettings.OmniTtsModel = "tts-test";
        });
        var path = SecRandom.Shared.Utils.GetFilePath("config", "voice", "omnitts-keys.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new Dictionary<string, string>
        {
            [selectedProvider.ToString()] = "legacy-dummy-key"
        }));
        var sent = 0;
        var provider = CreateProvider(handler, CreateCredentialStore(), new StubHttpMessageHandler(_ =>
        {
            sent++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"data\":[],\"items\":[]}")
            };
        }));

        Assert.Empty(await provider.GetModelsAsync());
        Assert.Equal(0, sent);
        if (selectedProvider == OmniTtsProvider.FishAudio)
        {
            Assert.Empty(await provider.GetVoicesAsync());
            Assert.Equal(0, sent);
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.SynthesizeAsync(new SpeechSynthesisRequest("test", "alloy")));
        Assert.Equal(0, sent);
        Assert.Equal("legacy-dummy-key", JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))![selectedProvider.ToString()]);
    }

    [Fact]
    public async Task F04_RemoteHttpCannotSendCredential()
    {
        var handler = CreateHandler(config =>
        {
            config.VoiceSettings.OmniTtsProvider = OmniTtsProvider.MiMo;
            config.VoiceSettings.OmniTtsApiBaseUrl = "http://remote.example.test";
            config.VoiceSettings.OmniTtsModel = "mimo-v2.5-tts";
        });
        var store = CreateCredentialStore();
        store.SetKey(OmniTtsProvider.MiMo, "https://remote.example.test", "dummy-http-key");
        var sent = 0;
        var provider = CreateProvider(handler, store, new StubHttpMessageHandler(_ =>
        {
            sent++;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"data\":[]}") };
        }));

        Assert.Empty(await provider.GetModelsAsync());
        Assert.Equal(0, sent);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.SynthesizeAsync(new SpeechSynthesisRequest("test", "mimo_default")));
        Assert.Equal(0, sent);
    }

    [Fact]
    public void F04_CredentialStoreBindsProviderAndNormalizedOriginAcrossReload()
    {
        var store = CreateCredentialStore();
        store.SetKey(OmniTtsProvider.Custom, "https://EXAMPLE.test:443/v1", " dummy-bound-key ");
        var reloaded = CreateCredentialStore();

        Assert.Equal("dummy-bound-key", reloaded.GetKey(OmniTtsProvider.Custom, "https://example.test/another/path/"));
        Assert.True(reloaded.HasKey(OmniTtsProvider.Custom, "https://example.test/v2"));
        Assert.Null(reloaded.GetKey(OmniTtsProvider.OpenAi, "https://example.test/v1"));
        Assert.Null(reloaded.GetKey(OmniTtsProvider.Custom, "https://other.test/v1"));
        Assert.Null(reloaded.GetKey(OmniTtsProvider.Custom, "https://example.test:444/v1"));
        Assert.Null(reloaded.GetKey(OmniTtsProvider.Custom, "http://example.test/v1"));
        reloaded.ClearKey(OmniTtsProvider.Custom, "https://other.test/v1");
        Assert.True(CreateCredentialStore().HasKey(OmniTtsProvider.Custom, "https://example.test"));
        reloaded.ClearKey(OmniTtsProvider.Custom, "https://example.test/changed-path");
        Assert.False(CreateCredentialStore().HasKey(OmniTtsProvider.Custom, "https://example.test"));
    }

    [Fact]
    public void F04_LegacyKeyRequiresExplicitReentryForOriginBinding()
    {
        var path = SecRandom.Shared.Utils.GetFilePath("config", "voice", "omnitts-keys.json");
        File.WriteAllText(path, "{\"MiMo\":\"legacy-dummy-key\"}");
        var store = CreateCredentialStore();
        Assert.Null(store.GetKey(OmniTtsProvider.MiMo, OmniTtsSpeechProvider.MiMoDefaultBaseUrl));
        Assert.Single(JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!);

        store.SetKey(OmniTtsProvider.MiMo, OmniTtsSpeechProvider.MiMoDefaultBaseUrl, "reentered-dummy-key");
        var reloaded = CreateCredentialStore();
        Assert.Equal("reentered-dummy-key", reloaded.GetKey(OmniTtsProvider.MiMo, OmniTtsSpeechProvider.MiMoDefaultBaseUrl));
        Assert.Null(reloaded.GetKey(OmniTtsProvider.MiMo, "https://imported.example.test"));
    }

    [Theory]
    [InlineData("http://remote.example.test/v1")]
    [InlineData("ftp://remote.example.test/v1")]
    [InlineData("https://user:password@remote.example.test/v1")]
    [InlineData("https://remote.example.test/v1?host=other.test")]
    [InlineData("https://remote.example.test/v1#fragment")]
    [InlineData("relative/path")]
    public void F04_UnsafeBaseUrlCannotBindCredential(string baseUrl)
    {
        var store = CreateCredentialStore();
        Assert.Throws<ArgumentException>(() => store.SetKey(OmniTtsProvider.Custom, baseUrl, "dummy-key"));
        Assert.False(store.HasKey(OmniTtsProvider.Custom, baseUrl));
        var path = SecRandom.Shared.Utils.GetFilePath("config", "voice", "omnitts-keys.json");
        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData("http://localhost:8123/v1")]
    [InlineData("http://127.0.0.1:8123/v1")]
    [InlineData("http://[::1]:8123/v1")]
    public void F04_LoopbackHttpCredentialIsBoundToItsOwnOrigin(string baseUrl)
    {
        var store = CreateCredentialStore();
        store.SetKey(OmniTtsProvider.Custom, baseUrl, "dummy-loopback-key");
        Assert.Equal("dummy-loopback-key", CreateCredentialStore().GetKey(OmniTtsProvider.Custom, baseUrl));
        Assert.Null(store.GetKey(OmniTtsProvider.Custom, baseUrl.Replace(":8123", ":8124")));
        Assert.Null(store.GetKey(OmniTtsProvider.Custom, baseUrl.Replace("http:", "https:")));
    }

    [Theory]
    [InlineData(OmniTtsProvider.OpenAi)]
    [InlineData(OmniTtsProvider.Custom)]
    [InlineData(OmniTtsProvider.MiMo)]
    [InlineData(OmniTtsProvider.Gemini)]
    [InlineData(OmniTtsProvider.FishAudio)]
    public async Task F04_ChangedConfigurationOriginCannotReuseKeyButSameOriginPathCan(OmniTtsProvider selectedProvider)
    {
        var handler = CreateHandler(config =>
        {
            config.VoiceSettings.OmniTtsProvider = selectedProvider;
            config.VoiceSettings.OmniTtsApiBaseUrl = "https://trusted.example.test/v1";
            config.VoiceSettings.OmniTtsModel = "tts-test";
        });
        var store = CreateCredentialStore();
        store.SetKey(selectedProvider, handler.Data.VoiceSettings.OmniTtsApiBaseUrl, "dummy-bound-key");
        var sent = 0;
        var provider = CreateProvider(handler, CreateCredentialStore(), new StubHttpMessageHandler(request =>
        {
            sent++;
            Assert.Equal("trusted.example.test", request.RequestUri!.Host);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"data\":[{\"id\":\"tts-test\"}],\"models\":[{\"name\":\"tts-test\"}],\"items\":[]}")
            };
        }));
        handler.Data.VoiceSettings.OmniTtsApiBaseUrl = "https://TRUSTED.example.test:443/new-path";
        Assert.Contains("tts-test", await provider.GetModelsAsync());
        Assert.Equal(1, sent);

        handler.Data.VoiceSettings.OmniTtsApiBaseUrl = "https://imported.example.test/v1";
        Assert.Empty(await provider.GetModelsAsync());
        if (selectedProvider == OmniTtsProvider.FishAudio)
            Assert.Empty(await provider.GetVoicesAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.SynthesizeAsync(new SpeechSynthesisRequest("test", "alloy")));
        Assert.Equal(1, sent);
        Assert.False(store.HasKey(selectedProvider, handler.Data.VoiceSettings.OmniTtsApiBaseUrl));
        Assert.True(store.HasKey(selectedProvider, "https://trusted.example.test"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task F04_UntrustedCredentialFallsBackToEdgeAndCannotGenerateOmniCache(bool legacyCredential)
    {
        var handler = CreateHandler(config =>
        {
            config.VoiceSettings.VoiceEnable = true;
            config.VoiceSettings.VoiceEngine = OmniTtsSpeechProvider.OmniEngine;
            config.VoiceSettings.OmniTtsProvider = OmniTtsProvider.OpenAi;
            config.VoiceSettings.OmniTtsApiBaseUrl = "https://imported.example.test/v1";
            config.VoiceSettings.OmniTtsModel = "tts-test";
            config.VoiceSettings.OmniTtsVoiceId = "alloy";
        });
        if (legacyCredential)
        {
            File.WriteAllText(SecRandom.Shared.Utils.GetFilePath("config", "voice", "omnitts-keys.json"),
                "{\"OpenAi\":\"legacy-dummy-key\"}");
        }
        var store = CreateCredentialStore();
        if (!legacyCredential)
            store.SetKey(OmniTtsProvider.OpenAi, "https://trusted.example.test/v1", "dummy-bound-key");
        var sent = 0;
        var omni = CreateProvider(handler, store, new StubHttpMessageHandler(_ =>
        {
            sent++;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
        }));
        var edge = new EngineTwoTestSpeechProvider(engine: EdgeTtsSpeechProvider.EdgeEngine);
        var service = new VoiceAnnouncementService(handler, [omni, edge], new RecordingSpeechAudioPlayer(),
            NullLogger<VoiceAnnouncementService>.Instance, store);

        await service.SpeakAsync("fallback-test", waitForCompletion: true, TestContext.Current.CancellationToken);
        Assert.Equal(1, edge.SynthesisCount);
        Assert.Equal(0, sent);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GenerateCacheAsync(["batch-test"], cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(0, sent);
    }

    private static MainConfigHandler CreateHandler(Action<MainConfigModel>? configure = null)
    {
        var config = new MainConfigModel();
        configure?.Invoke(config);
        return new MainConfigHandler(
            NullLogger<MainConfigHandler>.Instance,
            new TestConfigService(config));
    }

    private static OmniTtsCredentialStore CreateCredentialStore() => new();

    private static OmniTtsSpeechProvider CreateProvider(
        MainConfigHandler handler,
        OmniTtsCredentialStore store,
        HttpMessageHandler handlerStub,
        MiMoVoiceReferenceStore? referenceStore = null)
    {
        var client = new HttpClient(handlerStub);
        var factory = new StubHttpClientFactory(client);
        return new OmniTtsSpeechProvider(
            handler,
            store,
            referenceStore ?? new MiMoVoiceReferenceStore(),
            factory,
            NullLogger<OmniTtsSpeechProvider>.Instance);
    }

    [Fact]
    public async Task OpenAiCompatibleRequestUsesExpectedBodyAndAuth()
    {
        var handler = CreateHandler(config =>
        {
            config.VoiceSettings.OmniTtsProvider = OmniTtsProvider.OpenAi;
            config.VoiceSettings.OmniTtsApiBaseUrl = "https://example.openai.test/v1";
            config.VoiceSettings.OmniTtsModel = "tts-1";
            config.VoiceSettings.OmniTtsVoiceId = "alloy";
        });
        var store = CreateCredentialStore();
        store.SetKey(OmniTtsProvider.OpenAi, handler.Data.VoiceSettings.OmniTtsApiBaseUrl, "sk-test");

        byte[] audioBytes = [0x49, 0x44, 0x33, 0x01, 0x02];
        HttpRequestMessage? captured = null;
        string? capturedBody = null;
        var provider = CreateProvider(handler, store, new StubHttpMessageHandler(request =>
        {
            captured = request;
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(audioBytes) };
        }));

        var audio = await provider.SynthesizeAsync(new SpeechSynthesisRequest("测试播报", "alloy"));

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal("https://example.openai.test/v1/audio/speech", captured.RequestUri!.ToString());
        Assert.Equal("Bearer sk-test", captured.Headers.Authorization?.ToString());
        Assert.Equal(".mp3", audio.FileExtension);
        Assert.Equal(audioBytes, audio.Content);

        var body = JsonSerializer.Deserialize<JsonElement>(capturedBody!);
        Assert.Equal("tts-1", body.GetProperty("model").GetString());
        Assert.Equal("测试播报", body.GetProperty("input").GetString());
        Assert.Equal("alloy", body.GetProperty("voice").GetString());
        Assert.Equal("mp3", body.GetProperty("response_format").GetString());
    }

    [Fact]
    public async Task InstructionsAreSentOnlyForGpt4oMiniTts()
    {
        var handler = CreateHandler(config =>
        {
            config.VoiceSettings.OmniTtsProvider = OmniTtsProvider.OpenAi;
            config.VoiceSettings.OmniTtsModel = "gpt-4o-mini-tts";
            config.VoiceSettings.OmniTtsVoiceId = "alloy";
            config.VoiceSettings.OmniTtsInstructions = "读得慢一点";
        });
        var store = CreateCredentialStore();
        store.SetKey(OmniTtsProvider.OpenAi, handler.Data.VoiceSettings.OmniTtsApiBaseUrl, "sk-test");

        HttpRequestMessage? captured = null;
        string? capturedBody = null;
        var provider = CreateProvider(handler, store, new StubHttpMessageHandler(request =>
        {
            captured = request;
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
        }));

        await provider.SynthesizeAsync(new SpeechSynthesisRequest("你好", "alloy"));
        var body = JsonSerializer.Deserialize<JsonElement>(capturedBody!);
        Assert.Equal("读得慢一点", body.GetProperty("instructions").GetString());

        handler.Data.VoiceSettings.OmniTtsModel = "tts-1";
        captured = null;
        capturedBody = null;
        await provider.SynthesizeAsync(new SpeechSynthesisRequest("你好", "alloy"));
        body = JsonSerializer.Deserialize<JsonElement>(capturedBody!);
        Assert.False(body.TryGetProperty("instructions", out _));
    }

    [Fact]
    public async Task MiMoRequestUsesApiKeyHeaderAndDecodesBase64Audio()
    {
        var handler = CreateHandler(config =>
        {
            config.VoiceSettings.OmniTtsProvider = OmniTtsProvider.MiMo;
            config.VoiceSettings.OmniTtsApiBaseUrl = "https://api.xiaomimimo.com";
            config.VoiceSettings.OmniTtsModel = "mimo-v2.5-tts";
            config.VoiceSettings.OmniTtsVoiceId = "mimo_default";
        });
        var store = CreateCredentialStore();
        store.SetKey(OmniTtsProvider.MiMo, handler.Data.VoiceSettings.OmniTtsApiBaseUrl, "mimo-key");

        byte[] rawAudio = [0x11, 0x22, 0x33];
        var payload = "{\"choices\":[{\"message\":{\"audio\":{\"data\":\"" + Convert.ToBase64String(rawAudio) + "\"}}}]}";

        HttpRequestMessage? captured = null;
        string? capturedBody = null;
        var provider = CreateProvider(handler, store, new StubHttpMessageHandler(request =>
        {
            captured = request;
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
        }));

        var audio = await provider.SynthesizeAsync(new SpeechSynthesisRequest("测试播报", "mimo_default"));

        Assert.NotNull(captured);
        Assert.Equal("https://api.xiaomimimo.com/v1/chat/completions", captured!.RequestUri!.ToString());
        Assert.True(captured.Headers.TryGetValues("api-key", out var values));
        Assert.Equal("mimo-key", values!.Single());
        Assert.Equal(rawAudio, audio.Content);
        Assert.Equal(".mp3", audio.FileExtension);

        var body = JsonSerializer.Deserialize<JsonElement>(capturedBody!);
        Assert.Equal("mimo-v2.5-tts", body.GetProperty("model").GetString());
        Assert.Equal("测试播报", body.GetProperty("messages")[0].GetProperty("content").GetString());
        Assert.Equal("mp3", body.GetProperty("audio").GetProperty("format").GetString());
        Assert.Equal("mimo_default", body.GetProperty("audio").GetProperty("voice").GetString());
    }

    [Fact]
    public async Task MiMoVoiceDesignUsesDescriptionAndWavOutput()
    {
        var handler = CreateHandler(config =>
        {
            config.VoiceSettings.OmniTtsProvider = OmniTtsProvider.MiMo;
            config.VoiceSettings.OmniTtsApiBaseUrl = "https://api.xiaomimimo.com";
            config.VoiceSettings.OmniTtsModel = OmniTtsSpeechProvider.MiMoVoiceDesignModel;
            config.VoiceSettings.MiMoVoiceDesignPrompt = "young male, warm and clear";
        });
        var store = CreateCredentialStore();
        store.SetKey(OmniTtsProvider.MiMo, handler.Data.VoiceSettings.OmniTtsApiBaseUrl, "mimo-key");
        var rawAudio = new byte[] { 0x11, 0x22, 0x33 };
        var payload = "{\"choices\":[{\"message\":{\"audio\":{\"data\":\"" +
                      Convert.ToBase64String(rawAudio) + "\"}}}]}";
        string? capturedBody = null;
        var provider = CreateProvider(handler, store, new StubHttpMessageHandler(request =>
        {
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
        }));

        var audio = await provider.SynthesizeAsync(new SpeechSynthesisRequest("hello", string.Empty));

        var body = JsonSerializer.Deserialize<JsonElement>(capturedBody!);
        Assert.Equal(OmniTtsSpeechProvider.MiMoVoiceDesignModel, body.GetProperty("model").GetString());
        Assert.Equal("young male, warm and clear", body.GetProperty("messages")[0].GetProperty("content").GetString());
        Assert.Equal("hello", body.GetProperty("messages")[1].GetProperty("content").GetString());
        Assert.Equal("wav", body.GetProperty("audio").GetProperty("format").GetString());
        Assert.True(body.GetProperty("audio").GetProperty("optimize_text_preview").GetBoolean());
        Assert.False(body.GetProperty("audio").TryGetProperty("voice", out _));
        Assert.Equal(rawAudio, audio.Content);
        Assert.Equal(".wav", audio.FileExtension);
    }

    [Fact]
    public async Task MiMoVoiceCloneUsesReferenceAudioAndWavOutput()
    {
        var handler = CreateHandler(config =>
        {
            config.VoiceSettings.OmniTtsProvider = OmniTtsProvider.MiMo;
            config.VoiceSettings.OmniTtsApiBaseUrl = "https://api.xiaomimimo.com";
            config.VoiceSettings.OmniTtsModel = OmniTtsSpeechProvider.MiMoVoiceCloneModel;
            config.VoiceSettings.OmniTtsInstructions = "speak naturally";
        });
        var store = CreateCredentialStore();
        store.SetKey(OmniTtsProvider.MiMo, handler.Data.VoiceSettings.OmniTtsApiBaseUrl, "mimo-key");
        var referenceStore = new MiMoVoiceReferenceStore();
        var referenceBytes = new byte[44];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(referenceBytes, 0);
        Encoding.ASCII.GetBytes("WAVE").CopyTo(referenceBytes, 8);
        await referenceStore.ReplaceAsync(new MemoryStream(referenceBytes));
        var rawAudio = new byte[] { 0x44, 0x55, 0x66 };
        var payload = "{\"choices\":[{\"message\":{\"audio\":{\"data\":\"" +
                      Convert.ToBase64String(rawAudio) + "\"}}}]}";
        string? capturedBody = null;
        try
        {
            var provider = CreateProvider(handler, store, new StubHttpMessageHandler(request =>
            {
                capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                };
            }), referenceStore);

            var audio = await provider.SynthesizeAsync(new SpeechSynthesisRequest("hello", string.Empty));
            var body = JsonSerializer.Deserialize<JsonElement>(capturedBody!);
            var referenceUri = body.GetProperty("audio").GetProperty("voice").GetString();

            Assert.Equal(OmniTtsSpeechProvider.MiMoVoiceCloneModel, body.GetProperty("model").GetString());
            Assert.Equal("speak naturally", body.GetProperty("messages")[0].GetProperty("content").GetString());
            Assert.Equal("hello", body.GetProperty("messages")[1].GetProperty("content").GetString());
            Assert.Equal("wav", body.GetProperty("audio").GetProperty("format").GetString());
            Assert.StartsWith("data:audio/wav;base64,", referenceUri);
            Assert.Equal(referenceBytes, Convert.FromBase64String(referenceUri!["data:audio/wav;base64,".Length..]));
            Assert.Equal(rawAudio, audio.Content);
            Assert.Equal(".wav", audio.FileExtension);
        }
        finally
        {
            referenceStore.Clear();
        }
    }

    [Fact]
    public async Task GeminiRequestUsesInteractionsApiAndWrapsPcmAsWave()
    {
        var handler = CreateHandler(config =>
        {
            config.VoiceSettings.OmniTtsProvider = OmniTtsProvider.Gemini;
            config.VoiceSettings.OmniTtsApiBaseUrl = "https://example.gemini.test/v1beta";
            config.VoiceSettings.OmniTtsModel = "gemini-3.1-flash-tts-preview";
            config.VoiceSettings.OmniTtsVoiceId = "Kore";
        });
        var store = CreateCredentialStore();
        store.SetKey(OmniTtsProvider.Gemini, handler.Data.VoiceSettings.OmniTtsApiBaseUrl, "gemini-key");

        byte[] pcm = [0x11, 0x22, 0x33, 0x44];
        HttpRequestMessage? captured = null;
        string? capturedBody = null;
        var provider = CreateProvider(handler, store, new StubHttpMessageHandler(request =>
        {
            captured = request;
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"output_audio\":{\"data\":\"" + Convert.ToBase64String(pcm) + "\"}}",
                    Encoding.UTF8,
                    "application/json")
            };
        }));

        var audio = await provider.SynthesizeAsync(new SpeechSynthesisRequest("测试播报", "Kore"));

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal("https://example.gemini.test/v1beta/interactions", captured.RequestUri!.ToString());
        Assert.True(captured.Headers.TryGetValues("x-goog-api-key", out var keyValues));
        Assert.Equal("gemini-key", keyValues!.Single());
        Assert.Null(captured.Headers.Authorization);

        var body = JsonSerializer.Deserialize<JsonElement>(capturedBody!);
        Assert.Equal("gemini-3.1-flash-tts-preview", body.GetProperty("model").GetString());
        Assert.Equal("测试播报", body.GetProperty("input").GetString());
        Assert.Equal("audio", body.GetProperty("response_format").GetProperty("type").GetString());
        Assert.Equal(
            "Kore",
            body.GetProperty("generation_config").GetProperty("speech_config")[0].GetProperty("voice").GetString());

        Assert.Equal(".wav", audio.FileExtension);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(audio.Content[..4]));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(audio.Content[8..12]));
        Assert.Equal(24000, BinaryPrimitives.ReadInt32LittleEndian(audio.Content.AsSpan(24, 4)));
        Assert.Equal(pcm, audio.Content[44..]);
    }

    [Fact]
    public async Task GetModelsAsyncFiltersTtsModelsAndUsesBearerAuth()
    {
        var handler = CreateHandler(config =>
        {
            config.VoiceSettings.OmniTtsProvider = OmniTtsProvider.OpenAi;
            config.VoiceSettings.OmniTtsApiBaseUrl = "https://example.openai.test/v1";
        });
        var store = CreateCredentialStore();
        store.SetKey(OmniTtsProvider.OpenAi, handler.Data.VoiceSettings.OmniTtsApiBaseUrl, "sk-test");

        var payload = """
        {"data":[{"id":"tts-1"},{"id":"tts-1-hd"},{"id":"gpt-4o-mini-tts"},{"id":"gpt-4o"},{"id":"fishaudio/fish-speech-1.5"}]}
        """;

        HttpRequestMessage? captured = null;
        var provider = CreateProvider(handler, store, new StubHttpMessageHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
        }));

        var models = await provider.GetModelsAsync();

        Assert.Equal("https://example.openai.test/v1/models", captured!.RequestUri!.ToString());
        Assert.Contains("tts-1", models);
        Assert.Contains("gpt-4o-mini-tts", models);
        Assert.Contains("fishaudio/fish-speech-1.5", models);
        Assert.DoesNotContain("gpt-4o", models);
    }

    [Fact]
    public async Task GetModelsAsyncUsesGeminiModelsEndpointAndApiKeyHeader()
    {
        var handler = CreateHandler(config =>
        {
            config.VoiceSettings.OmniTtsProvider = OmniTtsProvider.Gemini;
            config.VoiceSettings.OmniTtsApiBaseUrl = "https://example.gemini.test/v1beta";
        });
        var store = CreateCredentialStore();
        store.SetKey(OmniTtsProvider.Gemini, handler.Data.VoiceSettings.OmniTtsApiBaseUrl, "gemini-key");
        HttpRequestMessage? captured = null;
        var provider = CreateProvider(handler, store, new StubHttpMessageHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"models\":[{\"name\":\"models/gemini-3.1-flash-tts-preview\"},{\"name\":\"models/gemini-2.5-pro-preview-tts\"},{\"name\":\"models/gemini-3.1-flash\"}]}",
                    Encoding.UTF8,
                    "application/json")
            };
        }));

        var models = await provider.GetModelsAsync();

        Assert.Equal("https://example.gemini.test/v1beta/models", captured!.RequestUri!.ToString());
        Assert.True(captured.Headers.TryGetValues("x-goog-api-key", out var keyValues));
        Assert.Equal("gemini-key", keyValues!.Single());
        Assert.Null(captured.Headers.Authorization);
        Assert.Equal(
            ["gemini-2.5-pro-preview-tts", "gemini-3.1-flash-tts-preview"],
            models);
    }

    [Fact]
    public async Task GetModelsAsyncWithoutKeyReturnsEmptyWithoutCallingTheApi()
    {
        var handler = CreateHandler();
        var store = CreateCredentialStore();
        ClearAllKeys(store);
        var provider = CreateProvider(handler, store, new StubHttpMessageHandler(_ =>
            throw new InvalidOperationException("The API must not be called without a key.")));

        var models = await provider.GetModelsAsync();

        Assert.Empty(models);
    }

    [Fact]
    public async Task GetVoicesAsyncReturnsDocumentedPresetsWithoutCallingTheApi()
    {
        var openAiHandler = CreateHandler(config => config.VoiceSettings.OmniTtsProvider = OmniTtsProvider.OpenAi);
        var openAiProvider = CreateProvider(openAiHandler, CreateCredentialStore(), new StubHttpMessageHandler(_ =>
            throw new InvalidOperationException("Preset voices must not call the API.")));
        var openAiVoices = await openAiProvider.GetVoicesAsync();
        Assert.Equal(
            ["alloy", "ash", "ballad", "coral", "echo", "fable", "nova", "onyx", "sage", "shimmer", "verse", "marin", "cedar"],
            openAiVoices.Select(voice => voice.Id));

        var mimoHandler = CreateHandler(config => config.VoiceSettings.OmniTtsProvider = OmniTtsProvider.MiMo);
        var mimoProvider = CreateProvider(mimoHandler, CreateCredentialStore(), new StubHttpMessageHandler(_ =>
            throw new InvalidOperationException("Preset voices must not call the API.")));
        var mimoVoices = await mimoProvider.GetVoicesAsync();
        Assert.Equal(
            ["mimo_default", "冰糖", "茉莉", "苏打", "白桦", "Mia", "Chloe", "Milo", "Dean"],
            mimoVoices.Select(voice => voice.Id));

        var geminiHandler = CreateHandler(config => config.VoiceSettings.OmniTtsProvider = OmniTtsProvider.Gemini);
        var geminiProvider = CreateProvider(geminiHandler, CreateCredentialStore(), new StubHttpMessageHandler(_ =>
            throw new InvalidOperationException("Preset voices must not call the API.")));
        var geminiVoices = await geminiProvider.GetVoicesAsync();
        Assert.Equal(30, geminiVoices.Count);
        Assert.Equal("Zephyr", geminiVoices[0].Id);
        Assert.Equal("Sulafat", geminiVoices[^1].Id);

        var customHandler = CreateHandler(config => config.VoiceSettings.OmniTtsProvider = OmniTtsProvider.Custom);
        var customProvider = CreateProvider(customHandler, CreateCredentialStore(), new StubHttpMessageHandler(_ =>
            throw new InvalidOperationException("Preset voice lists must not call the API.")));
        Assert.Empty(await customProvider.GetVoicesAsync());
    }

    [Fact]
    public void VoiceSettingsDoNotPresetOmniTtsModelOrVoice()
    {
        var settings = new VoiceSettingsConfig();

        Assert.Empty(settings.OmniTtsModel);
        Assert.Empty(settings.OmniTtsVoiceId);
    }

    [Fact]
    public async Task SynthesizeWithoutKeyThrows()
    {
        var handler = CreateHandler(config =>
        {
            config.VoiceSettings.OmniTtsProvider = OmniTtsProvider.OpenAi;
            config.VoiceSettings.OmniTtsApiBaseUrl = "https://example.openai.test/v1";
        });
        var store = CreateCredentialStore();
        ClearAllKeys(store);
        var provider = CreateProvider(handler, store, new StubHttpMessageHandler(_ =>
            throw new InvalidOperationException("The API must not be called without a key.")));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.SynthesizeAsync(new SpeechSynthesisRequest("测试", "alloy")));
    }

    private static void ClearAllKeys(OmniTtsCredentialStore store)
    {
        foreach (var provider in Enum.GetValues<OmniTtsProvider>())
            store.ClearKey(provider, OmniTtsSpeechProvider.GetDefaultBaseUrl(provider));
    }

    [Fact]
    public async Task CredentialStoreKeepsKeysOutsideSettingsJson()
    {
        var config = new MainConfigModel();
        var store = CreateCredentialStore();

        store.SetKey(OmniTtsProvider.OpenAi, OmniTtsSpeechProvider.OpenAiDefaultBaseUrl, "sk-secret");
        store.SetKey(OmniTtsProvider.MiMo, OmniTtsSpeechProvider.MiMoDefaultBaseUrl, "mimo-secret");

        Assert.Equal("sk-secret", store.GetKey(OmniTtsProvider.OpenAi, OmniTtsSpeechProvider.OpenAiDefaultBaseUrl));
        Assert.Equal("mimo-secret", store.GetKey(OmniTtsProvider.MiMo, OmniTtsSpeechProvider.MiMoDefaultBaseUrl));
        Assert.True(store.HasKey(OmniTtsProvider.OpenAi, OmniTtsSpeechProvider.OpenAiDefaultBaseUrl));
        Assert.False(store.HasKey(OmniTtsProvider.FishAudio, OmniTtsSpeechProvider.FishAudioDefaultBaseUrl));

        var serializedSettings = JsonSerializer.Serialize(config);
        Assert.DoesNotContain("sk-secret", serializedSettings);
        Assert.DoesNotContain("mimo-secret", serializedSettings);

        store.ClearKey(OmniTtsProvider.OpenAi, OmniTtsSpeechProvider.OpenAiDefaultBaseUrl);
        Assert.Null(store.GetKey(OmniTtsProvider.OpenAi, OmniTtsSpeechProvider.OpenAiDefaultBaseUrl));

        // Clean up the credential file created by this test.
        var credentialPath = SecRandom.Shared.Utils.GetFilePath("config", "voice", "omnitts-keys.json");
        var directory = Path.GetDirectoryName(credentialPath);
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public async Task VoiceCacheUsesEngineTwoMp3ExtensionAndDistinctKey()
    {
        var config = new MainConfigModel();
        config.VoiceSettings.VoiceEnable = true;
        config.VoiceSettings.VoiceEngine = OmniTtsSpeechProvider.OmniEngine;
        var handler = new MainConfigHandler(
            NullLogger<MainConfigHandler>.Instance,
            new TestConfigService(config));
        var provider = new EngineTwoTestSpeechProvider();
        var player = new RecordingSpeechAudioPlayer();
        var service = new VoiceAnnouncementService(handler, [provider], player, NullLogger<VoiceAnnouncementService>.Instance);

        var sharedText = $"{Guid.NewGuid():N}测试文本";

        try
        {
            await service.SpeakAsync(sharedText, waitForCompletion: true, TestContext.Current.CancellationToken);

            Assert.Equal(1, provider.SynthesisCount);
            var playedPath = Assert.Single(player.Paths);
            Assert.EndsWith(".mp3", playedPath, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(playedPath));

            var cleared = service.ClearVoiceCache();
            Assert.Equal(1, cleared);
        }
        finally
        {
            var cacheDirectory = SecRandom.Shared.Utils.GetDirectoryPath("audio", "voice");
            foreach (var file in Directory.EnumerateFiles(cacheDirectory))
            {
                try { File.Delete(file); }
                catch (IOException) { }
            }
        }
    }

    [Fact]
    public async Task GeminiVoiceCacheUsesWavExtension()
    {
        var handler = CreateHandler(config =>
        {
            config.VoiceSettings.VoiceEnable = true;
            config.VoiceSettings.VoiceEngine = OmniTtsSpeechProvider.OmniEngine;
            config.VoiceSettings.OmniTtsProvider = OmniTtsProvider.Gemini;
            config.VoiceSettings.OmniTtsVoiceId = "Kore";
            config.VoiceSettings.AnnounceName = true;
        });
        var provider = new EngineTwoTestSpeechProvider(".wav");
        var service = new VoiceAnnouncementService(
            handler,
            [provider],
            new RecordingSpeechAudioPlayer(),
            NullLogger<VoiceAnnouncementService>.Instance);
        var student = new Student { Name = $"gemini-{Guid.NewGuid():N}" };

        try
        {
            var result = await service.GenerateStudentsCacheAsync(
                [student],
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(1, result.Generated);
            Assert.True(service.HasStudentsCache([student]));
            Assert.Equal(1, service.ClearStudentsCache([student]));
        }
        finally
        {
            var cacheDirectory = SecRandom.Shared.Utils.GetDirectoryPath("audio", "voice");
            foreach (var file in Directory.EnumerateFiles(cacheDirectory))
            {
                try { File.Delete(file); }
                catch (IOException) { }
            }
        }
    }

    [Fact]
    public async Task ClearStudentsCacheDeletesOnlySelectedRosterEntries()
    {
        var handler = CreateHandler(config =>
        {
            config.VoiceSettings.VoiceEngine = OmniTtsSpeechProvider.OmniEngine;
            config.VoiceSettings.AnnounceName = true;
        });
        var provider = new EngineTwoTestSpeechProvider();
        var player = new RecordingSpeechAudioPlayer();
        var service = new VoiceAnnouncementService(handler, [provider], player, NullLogger<VoiceAnnouncementService>.Instance);
        var selectedStudent = new Student { Name = $"selected-{Guid.NewGuid():N}" };
        var otherStudent = new Student { Name = $"other-{Guid.NewGuid():N}" };

        try
        {
            var generated = await service.GenerateStudentsCacheAsync(
                [selectedStudent, otherStudent],
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(2, generated.Generated);
            Assert.True(service.HasStudentsCache([selectedStudent]));
            Assert.True(service.HasStudentsCache([otherStudent]));

            var uncachedStudent = new Student { Name = $"uncached-{Guid.NewGuid():N}" };
            Assert.False(service.HasStudentsCache([uncachedStudent]));

            Assert.Equal(1, service.ClearStudentsCache([selectedStudent]));
            Assert.False(service.HasStudentsCache([selectedStudent]));
            Assert.True(service.HasStudentsCache([otherStudent]));

            var regenerated = await service.GenerateStudentsCacheAsync(
                [selectedStudent, otherStudent],
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(1, regenerated.Generated);
            Assert.Equal(1, regenerated.Skipped);
        }
        finally
        {
            var cacheDirectory = SecRandom.Shared.Utils.GetDirectoryPath("audio", "voice");
            foreach (var file in Directory.EnumerateFiles(cacheDirectory))
            {
                try { File.Delete(file); }
                catch (IOException) { }
            }
        }
    }

    [Fact]
    public async Task HasPrizesCacheReturnsOnlyCachedEntries()
    {
        var handler = CreateHandler(config =>
        {
            config.VoiceSettings.VoiceEngine = OmniTtsSpeechProvider.OmniEngine;
            config.VoiceSettings.AnnounceName = true;
        });
        var provider = new EngineTwoTestSpeechProvider();
        var service = new VoiceAnnouncementService(
            handler,
            [provider],
            new RecordingSpeechAudioPlayer(),
            NullLogger<VoiceAnnouncementService>.Instance);
        var cachedPrize = new Prize { Name = $"cached-{Guid.NewGuid():N}" };
        var uncachedPrize = new Prize { Name = $"uncached-{Guid.NewGuid():N}" };

        try
        {
            await service.GeneratePrizesCacheAsync([cachedPrize], cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(service.HasPrizesCache([cachedPrize]));
            Assert.False(service.HasPrizesCache([uncachedPrize]));
        }
        finally
        {
            var cacheDirectory = SecRandom.Shared.Utils.GetDirectoryPath("audio", "voice");
            foreach (var file in Directory.EnumerateFiles(cacheDirectory))
            {
                try { File.Delete(file); }
                catch (IOException) { }
            }
        }
    }

    [Fact]
    public async Task GenerateStudentsCacheReportsOneBasedProgress()
    {
        var handler = CreateHandler(config =>
        {
            config.VoiceSettings.VoiceEngine = OmniTtsSpeechProvider.OmniEngine;
            config.VoiceSettings.AnnounceName = true;
        });
        var service = new VoiceAnnouncementService(
            handler,
            [new EngineTwoTestSpeechProvider()],
            new RecordingSpeechAudioPlayer(),
            NullLogger<VoiceAnnouncementService>.Instance);
        var progress = new List<VoiceBatchProgress>();
        Student[] students =
        [
            new Student { Name = $"first-{Guid.NewGuid():N}" },
            new Student { Name = $"second-{Guid.NewGuid():N}" }
        ];

        try
        {
            await service.GenerateStudentsCacheAsync(
                students,
                new CapturingProgress(progress),
                TestContext.Current.CancellationToken);

            Assert.Collection(
                progress,
                first => Assert.Equal((1, 2), (first.Completed, first.Total)),
                second => Assert.Equal((2, 2), (second.Completed, second.Total)));
        }
        finally
        {
            var cacheDirectory = SecRandom.Shared.Utils.GetDirectoryPath("audio", "voice");
            foreach (var file in Directory.EnumerateFiles(cacheDirectory))
            {
                try { File.Delete(file); }
                catch (IOException) { }
            }
        }
    }

    private sealed class CapturingProgress(List<VoiceBatchProgress> items) : IProgress<VoiceBatchProgress>
    {
        public void Report(VoiceBatchProgress value) => items.Add(value);
    }

    private sealed class EngineTwoTestSpeechProvider(string fileExtension = ".mp3", int engine = OmniTtsSpeechProvider.OmniEngine) : ISpeechProvider
    {
        public int Engine => engine;
        public int SynthesisCount { get; private set; }

        public Task<IReadOnlyList<VoiceOption>> GetVoicesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<VoiceOption>>([new VoiceOption("alloy", "alloy")]);

        public Task<SpeechAudio> SynthesizeAsync(
            SpeechSynthesisRequest request,
            CancellationToken cancellationToken = default)
        {
            SynthesisCount++;
            return Task.FromResult(new SpeechAudio([1, 2, 3], fileExtension));
        }
    }

    private sealed class RecordingSpeechAudioPlayer : ISpeechAudioPlayer
    {
        public List<string> Paths { get; } = [];

        public Task PlayAsync(string path, int volume, int playbackSpeed, CancellationToken cancellationToken = default)
        {
            Paths.Add(path);
            return Task.CompletedTask;
        }
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class TestConfigService(MainConfigModel config) : ConfigServiceBase
    {
        public override bool IsConfigExists<T>(T fallback) => true;
        public override T LoadConfig<T>(T fallback) => config is T typed ? typed : fallback;
        public override void SaveConfig<T>(T value) { }
        public override void DeleteConfig<T>(T value) { }
    }
}
