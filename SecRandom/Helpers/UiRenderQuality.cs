using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Services.Config;

namespace SecRandom.Helpers;

/// <summary>
///     位图缩放质量。低配模式下把 <see cref="BitmapInterpolationMode.HighQuality" /> 降为
///     <see cref="BitmapInterpolationMode.LowQuality" />（后者也是 Avalonia 的默认值）。
///     <para>
///         常驻窗口和抽取动画会整块缩放图片，高质量重采样是持续的 GPU 成本，这是 RenderOptions
///         里唯一有真实开销的一项；<c>EdgeMode.Antialias</c> 与 Avalonia 默认值相同，保持原样。
///     </para>
/// </summary>
internal static class UiRenderQuality
{
    // 根视觉树数量很少（主视图、设置视图、悬浮窗），用弱引用注册以便开关切换时重新应用。
    private static readonly List<WeakReference<Visual>> RegisteredRoots = [];

    public static BitmapInterpolationMode InterpolationMode => IsLowSpecMode
        ? BitmapInterpolationMode.LowQuality
        : BitmapInterpolationMode.HighQuality;

    private static bool IsLowSpecMode =>
        IAppHost.TryGetService<MainConfigHandler>()?.Data.General.PerformanceSettings.LowSpecMode == true;

    /// <summary>
    ///     按当前设置给一个根视觉树应用渲染选项，并登记以便设置变更时重新应用。
    /// </summary>
    public static void Apply(Visual visual)
    {
        RenderOptions.SetBitmapInterpolationMode(visual, InterpolationMode);
        RenderOptions.SetEdgeMode(visual, EdgeMode.Antialias);
        TextOptions.SetTextRenderingMode(visual, TextRenderingMode.Antialias);
        Register(visual);
    }

    /// <summary>
    ///     设置变更后重新应用到所有仍存活的根视觉树。
    /// </summary>
    public static void ApplyAll()
    {
        var mode = InterpolationMode;

        for (var i = RegisteredRoots.Count - 1; i >= 0; i--)
        {
            if (!RegisteredRoots[i].TryGetTarget(out var visual))
            {
                RegisteredRoots.RemoveAt(i);
                continue;
            }

            RenderOptions.SetBitmapInterpolationMode(visual, mode);
        }
    }

    private static void Register(Visual visual)
    {
        foreach (var reference in RegisteredRoots)
        {
            if (reference.TryGetTarget(out var existing) && ReferenceEquals(existing, visual))
                return;
        }

        RegisteredRoots.Add(new WeakReference<Visual>(visual));
    }
}
