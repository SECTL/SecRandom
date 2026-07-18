using System;
using Microsoft.Extensions.DependencyInjection;
using SecRandom.Core.Extensions.Registry;
using SecRandom.Core.Plugins;
using SecRandom.Core.Views;

namespace SecRandom.Services.Plugins;

public sealed class PluginServiceCollection(
    IServiceCollection services,
    PluginManifest manifest) : IPluginServiceCollection
{
    public void AddMainPage(PluginPageRegistration registration)
    {
        EnsurePluginOwnsRegistration(registration);
        services.AddPluginMainPage(registration);
    }

    public void AddSettingsPage(PluginPageRegistration registration)
    {
        EnsurePluginOwnsRegistration(registration);
        services.AddPluginSettingsPage(registration);
    }

    public void AddView(PluginViewRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        EnsurePluginOwnsRegistration(registration.PluginId);
        registration.Validate();
        services.AddViewRegistration(new ViewRegistration
        {
            Id = registration.ViewId,
            PluginId = registration.PluginId,
            ViewType = registration.ViewType,
            DefaultPresentation = registration.DefaultPresentation
        });
    }

    private void EnsurePluginOwnsRegistration(PluginPageRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        EnsurePluginOwnsRegistration(registration.PluginId);
    }

    private void EnsurePluginOwnsRegistration(string pluginId)
    {
        if (!string.Equals(pluginId, manifest.Id, StringComparison.Ordinal))
            throw new InvalidOperationException("Plugin page registration must use the current plugin id.");
    }
}
