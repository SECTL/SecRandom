using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SecRandom.Core.Views;

public static class ViewServiceCollectionExtensions
{
    public static ViewEngineBuilder AddViewEngine(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IViewRegistry, ViewRegistry>();
        services.TryAddSingleton<IViewEngine, ViewEngine>();
        return new ViewEngineBuilder(services);
    }

    public static IServiceCollection AddViewRegistration<TView>(
        this IServiceCollection services,
        string viewId,
        ViewPresentation defaultPresentation = ViewPresentation.Page)
        where TView : ViewBase
    {
        return services.AddViewRegistration(new ViewRegistration
        {
            Id = viewId,
            ViewType = typeof(TView),
            DefaultPresentation = defaultPresentation
        });
    }

    public static IServiceCollection AddViewRegistration(this IServiceCollection services, ViewRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(registration);
        registration.Validate();

        services.AddTransient(registration.ViewType);
        services.AddSingleton<IHostedViewRegistration>(_ => new HostedViewRegistration(registration));
        return services;
    }

    public static void RegisterHostedViews(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var registry = services.GetRequiredService<IViewRegistry>();
        foreach (var registration in services.GetServices<IHostedViewRegistration>())
            registry.Register(registration.Registration);
    }
}

internal interface IHostedViewRegistration
{
    ViewRegistration Registration { get; }
}

internal sealed class HostedViewRegistration(ViewRegistration registration) : IHostedViewRegistration
{
    public ViewRegistration Registration { get; } = registration;
}
