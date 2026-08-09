using SecRandom.Platforms.Abstractions;

namespace SecRandom.Platforms.MacOs;

internal sealed class MacOsRemovableStorageBindingMarker : IRemovableStorageBindingMarker
{
    public string FileName => ".SecRandom.safety.key";

    public bool TryHide(string path) => true;
}
