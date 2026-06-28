using System;
using Microsoft.Extensions.DependencyInjection;
using SecRandom.Core.Extensions.Registry;
using SecRandom.Core.Plugins;

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

    private void EnsurePluginOwnsRegistration(PluginPageRegistration registration)
    {
        if (!string.Equals(registration.PluginId, manifest.Id, StringComparison.Ordinal))
            throw new InvalidOperationException("Plugin page registration must use the current plugin id.");
    }
}
