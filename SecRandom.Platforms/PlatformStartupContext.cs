using SecRandom.Platforms.Abstractions;

namespace SecRandom.Platforms;

public static class PlatformStartupContext
{
    private static IPlatformServiceRoot? _current;

    public static IPlatformServiceRoot Current => _current ?? PlatformServiceRootStub.Instance;

    public static void Set(IPlatformServiceRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (_current is not null)
            throw new InvalidOperationException("The platform service root has already been configured.");

        _current = root;
    }
}
