using Avalonia.Controls.Primitives;

namespace SecRandom.Core.Controls;

/// <summary>
/// 开发构建装饰层
/// </summary>
public class DevelopmentBuildAdorner : TemplatedControl
{
    /// <inheritdoc />
    public DevelopmentBuildAdorner()
    {
        ClipToBounds = false;
    }
}