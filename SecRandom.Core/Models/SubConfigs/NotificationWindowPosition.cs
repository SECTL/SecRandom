namespace SecRandom.Core.Models.SubConfigs;

/// <summary>
///     内置通知浮窗在屏幕工作区里的九宫格锚点。
/// </summary>
/// <remarks>
///     数值就是 <see cref="NotificationChannelSettings.WindowPosition" /> 的持久化值，必须逐字节稳定：
///     0–4 是旧版只有五档时的取值（中心 / 顶部 / 底部 / 左侧 / 右侧），5–8 是后来补上的四角，
///     只能追加、不能重排，否则旧配置会跑到别的格子上。
/// </remarks>
public enum NotificationWindowPosition
{
    /// <summary>屏幕工作区正中间（旧配置缺键时的默认值）。</summary>
    Center = 0,

    Top = 1,

    Bottom = 2,

    Left = 3,

    Right = 4,

    TopLeft = 5,

    TopRight = 6,

    BottomLeft = 7,

    BottomRight = 8
}

/// <summary>浮窗在水平方向上的落位：贴左、居中还是贴右。</summary>
public enum NotificationWindowHorizontalAnchor
{
    Left,
    Center,
    Right
}

/// <summary>浮窗在垂直方向上的落位：贴顶、居中还是贴底。</summary>
public enum NotificationWindowVerticalAnchor
{
    Top,
    Center,
    Bottom
}

/// <summary>
///     九宫格锚点的纯计算规则：持久化数值 ↔ 3×3 网格位置 ↔ 水平/垂直落位。
/// </summary>
/// <remarks>
///     设置页的圆点选择器与 <c>App</c> 的窗口定位共用这一份映射，两者不会各写一套 switch 而慢慢跑偏。
///     这里是纯函数，不依赖 Avalonia 的窗口类型，因此可以直接单元测试。
/// </remarks>
public static class NotificationWindowAnchorLayout
{
    /// <summary>九宫格按行优先排列的持久化值：左上 → 上 → 右上 → 左 → 中心 → 右 → 左下 → 下 → 右下。</summary>
    public static IReadOnlyList<NotificationWindowPosition> GridOrder { get; } =
    [
        NotificationWindowPosition.TopLeft,
        NotificationWindowPosition.Top,
        NotificationWindowPosition.TopRight,
        NotificationWindowPosition.Left,
        NotificationWindowPosition.Center,
        NotificationWindowPosition.Right,
        NotificationWindowPosition.BottomLeft,
        NotificationWindowPosition.Bottom,
        NotificationWindowPosition.BottomRight
    ];

    /// <summary>九宫格的格子数（3×3）。</summary>
    public const int CellCount = 9;

    /// <summary>持久化值是否落在九宫格内。</summary>
    public static bool IsDefined(int value) => value is >= 0 and < CellCount;

    /// <summary>把任意整数收敛成合法的九宫格值；越界值按"中心"处理，与旧配置缺键时一致。</summary>
    public static NotificationWindowPosition Normalize(int value) =>
        IsDefined(value) ? (NotificationWindowPosition)value : NotificationWindowPosition.Center;

    /// <summary>持久化值在九宫格里的格子序号（行优先，0 是左上角）。</summary>
    public static int ToCellIndex(int value)
    {
        var normalized = Normalize(value);
        for (var index = 0; index < GridOrder.Count; index++)
        {
            if (GridOrder[index] == normalized)
                return index;
        }

        return GridOrder.Count / 2;
    }

    /// <summary>解析出浮窗贴哪一侧、贴哪一端。</summary>
    public static (NotificationWindowHorizontalAnchor Horizontal, NotificationWindowVerticalAnchor Vertical) Resolve(
        int value) =>
        Normalize(value) switch
        {
            NotificationWindowPosition.Top => (NotificationWindowHorizontalAnchor.Center,
                NotificationWindowVerticalAnchor.Top),
            NotificationWindowPosition.Bottom => (NotificationWindowHorizontalAnchor.Center,
                NotificationWindowVerticalAnchor.Bottom),
            NotificationWindowPosition.Left => (NotificationWindowHorizontalAnchor.Left,
                NotificationWindowVerticalAnchor.Center),
            NotificationWindowPosition.Right => (NotificationWindowHorizontalAnchor.Right,
                NotificationWindowVerticalAnchor.Center),
            NotificationWindowPosition.TopLeft => (NotificationWindowHorizontalAnchor.Left,
                NotificationWindowVerticalAnchor.Top),
            NotificationWindowPosition.TopRight => (NotificationWindowHorizontalAnchor.Right,
                NotificationWindowVerticalAnchor.Top),
            NotificationWindowPosition.BottomLeft => (NotificationWindowHorizontalAnchor.Left,
                NotificationWindowVerticalAnchor.Bottom),
            NotificationWindowPosition.BottomRight => (NotificationWindowHorizontalAnchor.Right,
                NotificationWindowVerticalAnchor.Bottom),
            _ => (NotificationWindowHorizontalAnchor.Center, NotificationWindowVerticalAnchor.Center)
        };
}