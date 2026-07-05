using Avalonia.Data.Converters;
using SecRandom.Core.Enums.Configs;

namespace SecRandom.Core.Converters;

public static class AnimationModeConverters
{
    public static FuncValueConverter<AnimationMode, bool> IsAnimated { get; } =
        new(mode => mode != AnimationMode.NoAnimation);
}
