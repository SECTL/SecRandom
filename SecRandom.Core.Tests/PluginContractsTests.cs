using Avalonia.Controls;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Plugins;
using SecRandom.Core.Services.Draw;
using SecRandom.Core.Views;
using SecRandom.Services.Plugins;
using SecRandom.Shared;
using System.IO.Compression;
using System.Text.Json;

namespace SecRandom.Core.Tests;

public class PluginContractsTests
{
    [Fact]
    public void PluginPageRegistration_RequiresPluginOwnedPageId()
    {
        PluginPageRegistration registration = new()
        {
            PluginId = "demo",
            PageId = "settings.about",
            Name = "Demo",
            IconGlyph = "icon",
            PageType = typeof(UserControl)
        };

        Assert.Throws<ArgumentException>(() => registration.Validate());
    }

    [Fact]
    public void PluginPageRegistration_RequiresUserControlPageType()
    {
        PluginPageRegistration registration = new()
        {
            PluginId = "demo",
            PageId = "plugin.demo.settings.page",
            Name = "Demo",
            IconGlyph = "icon",
            PageType = typeof(TextBlock)
        };

        Assert.Throws<ArgumentException>(() => registration.Validate());
    }

    [Fact]
    public void PluginPageRegistration_AcceptsPluginOwnedUserControl()
    {
        PluginPageRegistration registration = new()
        {
            PluginId = "demo",
            PageId = "plugin.demo.settings.page",
            Name = "Demo",
            IconGlyph = "icon",
            PageType = typeof(UserControl)
        };

        registration.Validate();
    }

    [Fact]
    public void PluginViewRegistration_RequiresAnOwnedViewBaseId()
    {
        var registration = new PluginViewRegistration
        {
            PluginId = "demo",
            ViewId = "plugin.demo.settings.page",
            ViewType = typeof(PluginTestView)
        };

        Assert.Throws<ArgumentException>(registration.Validate);
    }

    [Fact]
    public void PluginViewRegistration_RequiresAParameterlessViewConstructor()
    {
        var registration = new PluginViewRegistration
        {
            PluginId = "demo",
            ViewId = "plugin.demo.view.dialog",
            ViewType = typeof(PluginViewWithDependencies)
        };

        Assert.Throws<ArgumentException>(registration.Validate);
    }

    [Fact]
    public void PluginViewRegistration_AcceptsAnOwnedParameterlessView()
    {
        var registration = new PluginViewRegistration
        {
            PluginId = "demo",
            ViewId = "plugin.demo.view.dialog",
            ViewType = typeof(PluginTestView)
        };

        registration.Validate();
    }

    [Fact]
    public async Task PluginViewService_RejectsAnotherPluginsViewId()
    {
        var service = new PluginViewService("demo", new RecordingViewEngine());

        await Assert.ThrowsAsync<ArgumentException>(() => service.ShowAsync(
            "plugin.other.view.dialog",
            cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PluginViewService_DelegatesOwnedViewIds()
    {
        var engine = new RecordingViewEngine();
        var service = new PluginViewService("demo", engine);

        await Assert.ThrowsAsync<NotSupportedException>(() => service.ShowAsync(
            "plugin.demo.view.dialog",
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("plugin.demo.view.dialog", engine.LastShownViewId);
    }

    [Fact]
    public async Task PluginViewService_RejectsNamedHostSelection()
    {
        var engine = new RecordingViewEngine();
        var service = new PluginViewService("demo", engine);

        await Assert.ThrowsAsync<ArgumentException>(() => service.ShowAsync(
            "plugin.demo.view.dialog",
            new ViewShowOptions { HostId = "desktop.main" },
            TestContext.Current.CancellationToken));

        Assert.Null(engine.LastShownViewId);
    }

    [Fact]
    public void PluginDrawInvokerContract_DoesNotExposeFairDrawInternals()
    {
        var publicSurface = typeof(IPluginDrawInvoker)
            .GetMethods()
            .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType)
                .Append(method.ReturnType))
            .ToList();

        Assert.DoesNotContain(typeof(DrawEngine), publicSurface);
        Assert.DoesNotContain(typeof(WeightedDrawEngine<>), publicSurface);
        Assert.All(publicSurface, type => Assert.False(type.FullName?.StartsWith("SecRandom.Core.Services.Draw.") ?? false));
    }

    [Fact]
    public void PluginRuntimeContext_ExposesPluginInfoAndConfigDirectory()
    {
        var manifest = new PluginManifest { Id = "com.example.plugin", Name = "Example" };
        var pluginInfo = new PluginInfo
        {
            Manifest = manifest,
            PluginDirectory = @"C:\Plugins\com.example.plugin",
            ConfigDirectory = @"C:\Data\configs\plugins\com.example.plugin"
        };

        PluginRuntimeContext context = new(manifest, pluginInfo, NullLogger.Instance, null, pluginInfo.ConfigDirectory);

        Assert.Same(manifest, context.Manifest);
        Assert.Same(pluginInfo, context.PluginInfo);
        Assert.Equal(pluginInfo.ConfigDirectory, context.DataDirectory);
    }

    [Fact]
    public void PluginManager_ImportsSrpxPackage()
    {
        var pluginId = $"test.srpx.{Guid.NewGuid():N}";
        var packagePath = Path.Combine(Path.GetTempPath(), $"{pluginId}.srpx");
        var installedDirectory = Path.Combine(PluginManagerService.PluginsDirectory, pluginId);

        try
        {
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                var manifestEntry = archive.CreateEntry("plugin.json");
                using var writer = new StreamWriter(manifestEntry.Open());
                writer.Write(JsonSerializer.Serialize(new PluginManifest
                {
                    Id = pluginId,
                    Name = "SRPX Test Plugin",
                    Version = "1.0.0",
                    Author = "SecRandom",
                    Description = "SRPX import test.",
                    ApiVersion = PluginManagerService.SupportedApiVersion,
                    EntryAssembly = "TestPlugin.dll",
                    EntryType = "TestPlugin.Plugin"
                }));
            }

            var manager = new PluginManagerService(NullLogger<PluginManagerService>.Instance, new PluginStateStore());
            var importedPluginId = manager.ImportPluginPackage(packagePath);

            Assert.Equal(pluginId, importedPluginId);
            Assert.True(File.Exists(Path.Combine(installedDirectory, "plugin.json")));
            Assert.Contains(manager.Plugins, plugin => plugin.Id == pluginId);
        }
        finally
        {
            if (File.Exists(packagePath))
                File.Delete(packagePath);

            if (Directory.Exists(installedDirectory))
                Directory.Delete(installedDirectory, true);

            var stateFilePath = Utils.GetFilePath("plugins", "plugins-state.json");
            if (File.Exists(stateFilePath))
                File.Delete(stateFilePath);
        }
    }

    public sealed class PluginTestView : ViewBase
    {
    }

    private sealed class PluginViewWithDependencies : ViewBase
    {
        public PluginViewWithDependencies(object dependency)
        {
            ArgumentNullException.ThrowIfNull(dependency);
        }
    }

    private sealed class RecordingViewEngine : IViewEngine
    {
        public string? LastShownViewId { get; private set; }

        public Task<IViewHandle> ShowAsync(string viewId, ViewShowOptions? options = null, CancellationToken cancellationToken = default)
        {
            LastShownViewId = viewId;
            throw new NotSupportedException();
        }

        public Task<IViewHandle> ShowExclusiveAsync(string hostId, string viewId, ViewShowOptions? options = null, CancellationToken cancellationToken = default)
        {
            LastShownViewId = viewId;
            throw new NotSupportedException();
        }

        public Task<ViewCloseResult> ShowModalAsync(string viewId, ViewShowOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ViewCloseResult> CloseAsync(string viewId, ViewCloseReason reason = ViewCloseReason.Programmatic,
            object? result = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task CloseHostAsync(IViewHost host, ViewCloseReason reason, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

    }
}
