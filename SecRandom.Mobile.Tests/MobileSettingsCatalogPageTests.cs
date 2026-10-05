using System.Net.Http;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;
using SecRandom.Core.Models;
using SecRandom.Core.Services;
using SecRandom.Core.Services.Config;
using SecRandom.Mobile;
using SecRandom.Services.Auth;
using SecRandom.Services.Config;
using SecRandom.Services.Mobile;
using SecRandom.ViewModels.Mobile;
using SecRandom.Views.Mobile;
using SecRandom.Views.Mobile.Settings;

namespace SecRandom.Mobile.Tests;

public sealed class MobileSettingsCatalogPageTests
{
    [AvaloniaFact]
    public void CatalogLoadsAndRoutesRegisteredDesktopPages()
    {
        using var provider = CreateProvider();
        IAppHost.Host = new TestHost(provider);
        try
        {
            PagesRegistryService.SettingsItems.Clear();
            PagesRegistryService.GroupItems.Clear();
            PagesRegistryService.SettingsItems.Add(new PageInfo("settings.general.basic", FluentIcons.SettingsFilled,
                groupId: "settings.general") { Name = "Basic" });
            PagesRegistryService.GroupItems.Add(new PageGroupInfo("General", "settings.general", FluentIcons.SettingsFilled));

            // 页面走 DI 取，与 `AddSettingsPage<MobileSettingsCatalogPage>()` 的注册路径一致：
            // 构造函数再长出新的依赖时，这里会以"解析不出服务"直接失败，而不是某天在手机上白屏。
            var page = provider.GetRequiredService<MobileSettingsCatalogPage>();

            Assert.NotNull(page.FindControl<ScrollViewer>("PageScroll"));
            Assert.Single(page.Items);
            Assert.Equal("settings.general.basic", page.Items[0].Pages[0].Id);
        }
        finally
        {
            PagesRegistryService.SettingsItems.Clear();
            PagesRegistryService.GroupItems.Clear();
            IAppHost.Host = null;
        }
    }

    /// <summary>
    ///     目录页要用到的服务图。**账号区用真的 VM、真的 <see cref="SectlAuthService" />**：
    ///     被测页面会把它渲染出来（未登录态的占位），用一个假的替身就等于把这段真实依赖藏起来。
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="SectlAuthService" /> 的构造函数只做字段初始化：不读令牌文件、不发请求、不碰设备 UUID，
    ///         所以这里给它一套最小协作者就够——令牌文件落在临时目录，测试永远不会读到或写掉本机真实的登录态。
    ///     </para>
    ///     <para>
    ///         这条注释是为了防止下一个人"图省事"把账号区参数从页面构造函数里删掉：页面在生产里
    ///         （<c>AddSettingsPage&lt;MobileSettingsCatalogPage&gt;()</c>）本来就从容器里取依赖，
    ///         让测试也走同一条路，才是这类"构造函数变了、测试没跟上"的漂移真正的防线。
    ///     </para>
    /// </remarks>
    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IMobileSettingsNavigator, TestSettingsNavigator>();

        services.AddSingleton(new SectlTokenStore(Path.Combine(
            Path.GetTempPath(), "secrandom-tests", Guid.NewGuid().ToString("N"), "sectl-auth.json")));
        services.AddSingleton<IHttpClientFactory, StubHttpClientFactory>();
        services.AddSingleton(new DeviceUuidStore(
            new MainConfigHandler(NullLogger<MainConfigHandler>.Instance, new TestConfigService(new MainConfigModel())),
            NullLogger<DeviceUuidStore>.Instance));
        services.AddSingleton<IAuthRedirectBrokerFactory, LoopbackAuthRedirectBrokerFactory>();

        services.AddSingleton<SectlAuthService>();
        services.AddSingleton<MobileAccountSectionViewModel>();
        services.AddTransient<MobileSettingsCatalogPage>();
        return services.BuildServiceProvider();
    }

    private sealed class TestSettingsNavigator : IMobileSettingsNavigator
    {
        public bool IsOpen => false;
        public Task OpenAsync(string? pageId = null) => Task.CompletedTask;
        public Task NavigateAsync(string pageId) => Task.CompletedTask;
        public Task CloseAsync() => Task.CompletedTask;
    }

    /// <summary>账号服务在测试里不会发请求；http 客户端只为满足构造函数。</summary>
    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class TestConfigService(MainConfigModel config) : ConfigServiceBase
    {
        public override bool IsConfigExists<T>(T fallback) => true;

        public override T LoadConfig<T>(T fallback) => config is T typed ? typed : fallback;

        public override void SaveConfig<T>(T value)
        {
        }

        public override void DeleteConfig<T>(T value)
        {
        }
    }

    private sealed class TestHost(IServiceProvider services) : IHost
    {
        public IServiceProvider Services { get; } = services;
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }
}
