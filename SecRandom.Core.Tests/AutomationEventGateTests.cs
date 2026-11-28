using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.SingleInstance;
using SecRandom.Services.Ipc;

namespace SecRandom.Core.Tests;

public sealed class AutomationEventGateTests
{
    [Fact]
    public async Task EventPipe_RejectsDisabledSubscriptionsAndClosesExistingOnDisable()
    {
        var config = new MainConfigHandler(NullLogger<MainConfigHandler>.Instance, new MemoryConfig());
        config.Data.General.Basic.AutomationIpcEnabled = false;
        var server = new AutomationEventServer(new AutomationEventBus(), config, NullLogger<AutomationEventServer>.Instance);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await server.StartAsync(timeout.Token);
        try
        {
            using (var disabled = await ConnectAsync(timeout.Token))
            {
                using var reader = new StreamReader(disabled, Encoding.UTF8, leaveOpen: true);
                await using var writer = new StreamWriter(disabled, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                await writer.WriteLineAsync("{\"type\":\"subscribe\"}");
                using var response = JsonDocument.Parse((await reader.ReadLineAsync(timeout.Token))!);
                Assert.Equal("ipc_disabled", response.RootElement.GetProperty("error").GetProperty("code").GetString());
            }

            config.Data.General.Basic.AutomationIpcEnabled = true;
            using var enabled = await ConnectAsync(timeout.Token);
            using var eventReader = new StreamReader(enabled, Encoding.UTF8, leaveOpen: true);
            await using var eventWriter = new StreamWriter(enabled, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            await eventWriter.WriteLineAsync("{\"type\":\"subscribe\"}");
            using var accepted = JsonDocument.Parse((await eventReader.ReadLineAsync(timeout.Token))!);
            Assert.Equal("subscribed", accepted.RootElement.GetProperty("type").GetString());
            config.Data.General.Basic.AutomationIpcEnabled = false;
            using var closed = JsonDocument.Parse((await eventReader.ReadLineAsync(timeout.Token))!);
            Assert.Equal("ipc_disabled", closed.RootElement.GetProperty("error").GetProperty("code").GetString());
        }
        finally
        {
            await server.StopAsync(CancellationToken.None);
        }
    }

    private static async Task<NamedPipeClientStream> ConnectAsync(CancellationToken token)
    {
        var pipe = new NamedPipeClientStream(".", SingleInstanceService.EventPipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(token);
        return pipe;
    }

    private sealed class MemoryConfig : ConfigServiceBase
    {
        public override bool IsConfigExists<T>(T fallback) => false;
        public override T LoadConfig<T>(T fallback) => fallback;
        public override void SaveConfig<T>(T config) { }
        public override void DeleteConfig<T>(T config) { }
    }
}
