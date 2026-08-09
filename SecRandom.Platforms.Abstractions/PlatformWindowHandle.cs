namespace SecRandom.Platforms.Abstractions;

public readonly record struct PlatformWindowHandle(nint Value, string? Descriptor)
{
    public bool IsValid => Value != nint.Zero;
}
