namespace SecRandom.Core.Enums.Configs;

/// <summary>
///     悬浮窗自定义按钮能执行的动作。
/// </summary>
/// <remarks>
///     整数值会持久化到 settings.json，只允许在末尾追加，绝不重排或复用已有值，
///     否则旧配置会静默指向另一个动作。
/// </remarks>
public enum FloatingWindowButtonActionKind
{
    /// <summary>切换一个主页面（点名 / 抽奖 / 历史记录）。</summary>
    MainPage = 0,

    /// <summary>打开闪抽窗口。</summary>
    QuickDraw = 1,

    /// <summary>打开计时器。</summary>
    Timer = 2,

    /// <summary>打开设置窗口并跳到指定设置页。</summary>
    SettingsPage = 3,

    /// <summary>用系统默认浏览器打开 http/https 链接。</summary>
    Uri = 4,

    /// <summary>用系统默认程序打开本地文件或文件夹。</summary>
    Path = 5
}
