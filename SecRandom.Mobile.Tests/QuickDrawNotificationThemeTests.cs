using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Presentation;
using SecRandom.Services.Draw;
using SecRandom.Shared;
using SecRandom.Shared.Models.Profile;
using SecRandom.ViewModels.MainPages;
using SecRandom.Views.MainPages;

namespace SecRandom.Mobile.Tests;

public sealed class QuickDrawNotificationThemeTests
{
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void FirstStudentFrameUsesTheSameThemeAsItsBackground(bool darkApp)
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), "SecRandom.NotificationFirstFrame", Guid.NewGuid().ToString("N"));
        var previousTheme = Application.Current!.RequestedThemeVariant;
        var previousHost = IAppHost.Host;
        Window? window = null;
        GetUtilsMethod("ConfigureDataRoot").Invoke(null, [dataRoot]);
        using var audio = new DrawAudioService(null!, NullLogger<DrawAudioService>.Instance);
        var config = new MainConfigModel();
        config.Appearance.Theme = darkApp ? ThemeMode.Dark : ThemeMode.Light;
        config.QuickDrawSettings.DefaultClass = string.Empty;
        config.QuickDrawSettings.AlgorithmId = "builtin.random";
        config.DefaultDrawSettings.AnimationColorTheme = AnimationColorThemeMode.None;
        var handler = new MainConfigHandler(NullLogger<MainConfigHandler>.Instance, new TestConfigService(config));
        using var viewModel = new QuickDrawPageViewModel(handler, null!, null!, null!, null!, audio,
            NullLogger<QuickDrawPageViewModel>.Instance, null!, null!, null!, null!);
        using var provider = new ServiceCollection().AddSingleton(viewModel).BuildServiceProvider();
        try
        {
            Application.Current.RequestedThemeVariant = darkApp ? ThemeVariant.Dark : ThemeVariant.Light;
            IAppHost.Host = new TestHost(provider);
            var page = new QuickDrawPage();
            window = new Window { Content = page, Width = 460, Height = 320, Opacity = 0 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // Production prepares the theme before starting preview and revealing the window.
            var theme = AdaptiveBackdropResolver.FromAppTheme(!darkApp);
            viewModel.ApplyNotificationTheme(theme);
            var replace = typeof(QuickDrawPageViewModel).GetMethod("ReplaceResults", BindingFlags.Instance | BindingFlags.NonPublic)!;
            replace.Invoke(viewModel, new object[] { new Student[] { new() { Id = "13", Name = "首帧学生" } } });
            viewModel.IsResultVisible = true;
            window.Opacity = 1;
            Dispatcher.UIThread.RunJobs();

            var root = page.FindControl<Border>("RootBorder")!;
            Assert.Equal(theme.Surface, Assert.IsAssignableFrom<ISolidColorBrush>(root.Background).Color);
            var name = Assert.Single(page.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("首帧学生") == true);
            Assert.Equal(theme.Foreground, Assert.IsAssignableFrom<ISolidColorBrush>(name.Foreground).Color);
            var card = name.GetVisualAncestors().OfType<Border>().First(border => border.Classes.Contains("result-animation-item"));
            Assert.Equal(theme.CardSurface, Assert.IsAssignableFrom<ISolidColorBrush>(card.Background).Color);
        }
        finally
        {
            window?.Close();
            IAppHost.Host = previousHost;
            Application.Current.RequestedThemeVariant = previousTheme;
            GetUtilsMethod("ResetDataRootForTests").Invoke(null, null);
            if (Directory.Exists(dataRoot))
                Directory.Delete(dataRoot, recursive: true);
        }
    }

    private static MethodInfo GetUtilsMethod(string name) =>
        typeof(Utils).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;

    private sealed class TestConfigService(MainConfigModel config) : ConfigServiceBase
    {
        public override bool IsConfigExists<T>(T fallback) => true;
        public override T LoadConfig<T>(T fallback) => config is T typed ? typed : fallback;
        public override void SaveConfig<T>(T value) { }
        public override void DeleteConfig<T>(T value) { }
    }

    private sealed class TestHost(IServiceProvider services) : IHost
    {
        public IServiceProvider Services { get; } = services;
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }
}
