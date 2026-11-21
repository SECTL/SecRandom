using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SecRandom.Core.Abstraction;

namespace SecRandom.Core.Tests;

/// <summary>
///     <see cref="IAppHost" /> 的取服务边界。停机流程会先释放服务容器、再清空 Host，
///     这个窗口里错误处理路径仍会取服务；此时必须得到 null 而不是把 ObjectDisposedException 抛回去，
///     否则一次停机期的刷新（例如托盘 Sudo 菜单的 5 秒计时器）会二次击穿异常处理。
/// </summary>
[Collection("AppHost")]
public class AppHostServiceResolutionTests : IDisposable
{
    private readonly IHost? _originalHost = IAppHost.Host;

    public void Dispose()
    {
        IAppHost.Host = _originalHost;
    }

    [Fact]
    public void TryGetService_ReturnsNull_WhenHostIsNotBuilt()
    {
        IAppHost.Host = null;

        Assert.Null(IAppHost.TryGetService<IServiceProvider>());
    }

    [Fact]
    public void TryGetService_ReturnsNull_WhenProviderIsDisposed()
    {
        var provider = new ServiceCollection().AddSingleton(new object()).BuildServiceProvider();
        provider.Dispose();
        IAppHost.Host = new StubHost(provider);

        Assert.Null(IAppHost.TryGetService<object>());
    }

    [Fact]
    public void TryGetService_ReturnsNull_WhenServiceIsNotRegistered()
    {
        IAppHost.Host = new StubHost(new ServiceCollection().BuildServiceProvider());

        Assert.Null(IAppHost.TryGetService<object>());
    }

    [Fact]
    public void TryGetService_ReturnsRegisteredService()
    {
        var service = new object();
        IAppHost.Host = new StubHost(new ServiceCollection().AddSingleton(service).BuildServiceProvider());

        Assert.Same(service, IAppHost.TryGetService<object>());
    }

    [Fact]
    public void GetService_StillThrows_WhenHostIsNotBuilt()
    {
        IAppHost.Host = null;

        Assert.Throws<ArgumentException>(() =>
        {
            IAppHost.GetService<IServiceProvider>();
        });
    }

    private sealed class StubHost(IServiceProvider services) : IHost
    {
        public IServiceProvider Services { get; } = services;

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Dispose()
        {
        }
    }
}