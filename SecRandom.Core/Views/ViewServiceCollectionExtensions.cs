using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SecRandom.Core.Views;

public static class ViewServiceCollectionExtensions
{
    public static ViewEngineBuilder AddViewEngine(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IViewRegistry>(serviceProvider =>
        {
            var registry = new ViewRegistry();
            foreach (var registration in serviceProvider.GetServices<IHostedViewRegistration>())
                registry.Register(registration.Registration);
            return registry;
        });
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

        if (string.IsNullOrWhiteSpace(registration.PluginId))
            services.AddTransient(registration.ViewType);
        services.AddSingleton<IHostedViewRegistration>(_ => new HostedViewRegistration(registration));
        return services;
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
