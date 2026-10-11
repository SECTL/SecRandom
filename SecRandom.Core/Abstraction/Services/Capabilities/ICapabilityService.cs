using System.Reflection;

namespace SecRandom.Core.Abstraction.Services.Capabilities;

/// <summary>
///     插件可以声明的权限位。按位组合，写在 <c>manifest.yml</c> 的 <c>permissions</c> 里。
///     <para>
///         这是**声明式策略**，不是安全边界：插件与宿主同进程、FullTrust 运行。
///         它的用途是让插件自我约束、让用户在安装时看得见，以及让宿主能给出提示。
///     </para>
/// </summary>
[Flags]
public enum PluginPermissions
{
    /// <summary>没有声明任何权限。</summary>
    None = 0,

    /// <summary>往宿主界面里加东西（插槽、样式、叠加层）。</summary>
    UserInterface = 1,

    /// <summary>使用插件私有存储。</summary>
    Storage = 2,

    /// <summary>联网。</summary>
    Network = 4,

    /// <summary>读剪贴板。</summary>
    ClipboardRead = 8,

    /// <summary>写剪贴板。</summary>
    ClipboardWrite = 16,

    /// <summary>让用户挑文件/目录。</summary>
    FilePicker = 32,

    /// <summary>读写插件自己的设置页与状态文件（插件注册设置页本身不受限）。</summary>
    Settings = 64,

    /// <summary>发起抽签（通过 <c>IPluginDrawService</c>）。</summary>
    Draw = 128,

    /// <summary>读历史记录。</summary>
    History = 256,

    /// <summary>显示独立的窗口/弹层。</summary>
    Overlay = 512
}

/// <summary>宿主当前提供的能力 id。插件在跑之前先 <c>Has(...)</c> 一下，老宿主上就能优雅降级。</summary>
public static class HostCapabilities
{
    /// <summary>插件可以接管抽签结果的呈现。</summary>
    public const string DrawPresenter = "draw.presenter";

    /// <summary>宿主主窗口里有插件叠加层。</summary>
    public const string OverlayHost = "overlay.host";

    /// <summary>宿主界面里有插件内容插槽。</summary>
    public const string UiContribution = "ui.contribution";

    /// <summary>插件可以注入界面样式与资源。</summary>
    public const string UiStyle = "ui.style";

    /// <summary>插件私有存储。</summary>
    public const string PluginStorage = "plugin.storage";

    /// <summary>名单/奖池只读查询。</summary>
    public const string ListQuery = "list.query";

    /// <summary>宿主通知与确认框。</summary>
    public const string Notification = "notification";

    /// <summary>忙碌指示。</summary>
    public const string BusyIndicator = "busy.indicator";

    /// <summary>UI 线程调度。</summary>
    public const string UiScheduler = "ui.scheduler";

    /// <summary>插件独立窗口与模态对话框。</summary>
    public const string WindowService = "window.service";

    /// <summary>跨平台弹层（有叠加层走叠加层，否则退回窗口）。</summary>
    public const string PopupService = "popup.service";

    /// <summary>抽签候选过滤与结果后处理。</summary>
    public const string DrawPipeline = "draw.pipeline";

    /// <summary>读宿主主题色板。</summary>
    public const string ThemeTokens = "theme.tokens";

    /// <summary>宿主本地化。</summary>
    public const string Localization = "localization";

    /// <summary>插件事件总线。</summary>
    public const string EventBus = "event.bus";

    /// <summary>插件诊断信息。</summary>
    public const string Diagnostics = "diagnostics";

    /// <summary>当前宿主提供的全部能力 id。</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        DrawPresenter,
        OverlayHost,
        UiContribution,
        UiStyle,
        PluginStorage,
        ListQuery,
        Notification,
        BusyIndicator,
        UiScheduler,
        WindowService,
        PopupService,
        DrawPipeline,
        ThemeTokens,
        Localization,
        EventBus,
        Diagnostics
    ];

    /// <summary>把 <see cref="PluginPermissions" /> 的位翻成 id 列表（诊断/展示用）。</summary>
    public static IReadOnlyList<string> Describe(PluginPermissions permissions)
    {
        if (permissions == PluginPermissions.None)
            return [];

        var names = Enum.GetValues<PluginPermissions>()
            .Where(value => value != PluginPermissions.None && permissions.HasFlag(value))
            .Select(value => value.ToString())
            .ToArray();

        return names;
    }
}

/// <summary>某个插件实际生效的权限。</summary>
public sealed record PluginPermissionSet(string PluginId, PluginPermissions Permissions, bool IsDeclared)
{
    /// <summary>
    ///     是否允许某项权限。
    ///     <para>
    ///         清单里**没有**写 <c>permissions</c> 时视为"未声明"，一律允许——
    ///         否则所有存量插件都会被拒。
    ///     </para>
    /// </summary>
    public bool Allows(PluginPermissions permission) =>
        !IsDeclared || permission == PluginPermissions.None || (Permissions & permission) == permission;
}

/// <summary>
///     能力与权限查询扩展点：插件在用到某个扩展点前先问一句 <see cref="Has" />，
///     在支持权限声明的宿主上再问一句 <see cref="IsAllowed" />。
/// </summary>
public interface ICapabilityService
{
    /// <summary>宿主实现的插件 API 版本。</summary>
    Version HostApiVersion { get; }

    /// <summary>宿主提供的能力 id 集合。</summary>
    IReadOnlyCollection<string> Capabilities { get; }

    /// <summary>宿主是否提供某项能力。</summary>
    bool Has(string capabilityId);

    /// <summary>某个插件声明的权限（未知插件返回"未声明"= 全允许）。</summary>
    PluginPermissionSet GetPermissions(string pluginId);

    /// <summary>宿主是否允许某个插件使用某项权限。</summary>
    bool IsAllowed(string pluginId, PluginPermissions permission);
}
