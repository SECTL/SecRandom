using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Enums;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Notification;
using SecRandom.Services.Notification;
using SecRandom.Shared.Abstraction;

namespace SecRandom.Core.Tests;

[Collection("AppHost")]
public sealed class FullscreenTextLibraryIntegrationTests
{
    [Fact]
    public void Snapshot_UsesFrozenLibraryAndCountsOnlyVisibleTypewriterText()
    {
        var path = Path.Combine(Path.GetTempPath(), "sr-text-snapshot-" + Guid.NewGuid().ToString("N"), "library" + SecRandom.Shared.Utils.TextLibraryDatabaseExtension);
        var previousHost = IAppHost.Host;
        try
        {
            var library = new TextLibraryService(NullLogger<TextLibraryService>.Instance, path);
            library.ReplaceAll(["默认 {result}"]);
            var selected = library.CreateLibrary("Class");
            library.ReplaceAll(["课堂 {result}"]);
            var config = new MainConfigHandler(NullLogger<MainConfigHandler>.Instance, new MemoryConfig());
            using var provider = new ServiceCollection().AddSingleton(library).AddSingleton(config).BuildServiceProvider();
            IAppHost.Host = new StubHost(provider);

            var snapshot = FullscreenNotificationComposer.CreateSnapshot(NotificationSettingsType.RollCall, [], [], 0, 0, null, null, null, selected);
            Assert.Equal("课堂 {result}", snapshot.TemplateText);
            Assert.Equal(1, library.GetEntries(selected).Single().UseCount);

            config.Data.NotificationSettings.Fullscreen.ShowText = false;
            FullscreenNotificationComposer.CreateSnapshot(NotificationSettingsType.RollCall, [], [], 0, 0, null, null, null, selected);
            config.Data.NotificationSettings.Fullscreen.ShowText = true;
            config.Data.NotificationSettings.Fullscreen.AnimationStyle = FullscreenAnimationStyle.Grid;
            FullscreenNotificationComposer.CreateSnapshot(NotificationSettingsType.RollCall, [], [], 0, 0, null, null, null, selected);
            Assert.Equal(1, library.GetEntries(selected).Single().UseCount);
            Assert.Equal(0, library.GetEntries(library.DefaultLibraryId).Single().UseCount);
        }
        finally
        {
            IAppHost.Host = previousHost;
            if (Directory.Exists(Path.GetDirectoryName(path))) Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }

    private sealed class MemoryConfig : ConfigServiceBase
    {
        public override bool IsConfigExists<T>(T fallback) => false;
        public override T LoadConfig<T>(T fallback) => fallback;
        public override void SaveConfig<T>(T config) { }
        public override void DeleteConfig<T>(T config) { }
    }

    private sealed class StubHost(IServiceProvider services) : IHost
    {
        public IServiceProvider Services { get; } = services;
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }
}
