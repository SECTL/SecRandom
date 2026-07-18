using Microsoft.Extensions.DependencyInjection;

namespace SecRandom.Core.Views;

public sealed class ViewEngineBuilder(IServiceCollection services)
{
    public IServiceCollection Services { get; } = services;

    public ViewEngineBuilder AddView<TView>(string viewId, ViewPresentation defaultPresentation = ViewPresentation.Page)
        where TView : ViewBase
    {
        Services.AddViewRegistration<TView>(viewId, defaultPresentation);
        return this;
    }
}
