namespace SecRandom.PluginSdk;

public interface IPluginManager
{
    IReadOnlyList<PluginInfo> Plugins { get; }

    string PluginsDirectory { get; }

    void StagePackage(string packagePath);

    bool SetEnabled(string pluginId, bool enabled);
}
