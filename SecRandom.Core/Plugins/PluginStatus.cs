namespace SecRandom.Core.Plugins;

public enum PluginStatus
{
    Discovered,
    Disabled,
    Loaded,
    LoadFailed,
    Incompatible,
    PendingRestart
}
