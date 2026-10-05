using System;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Services.ControlPlane;
using SecRandom.Services.Desktop;
using SecRandom.Shared.Models.ControlNode;
using LR = SecRandom.Langs.SettingsPages.General.Control.Resources;

namespace SecRandom.ViewModels.SettingsPages;

/// <summary>
///     集控设置页：本机开关、节点身份与连接状态。
/// </summary>
/// <remarks>
///     <para>
///         这里**不经过 <c>settings.json</c>**：本机开关与已应用的期望状态都在
///         <c>data/config/control/node-state.json</c>。理由是安全边界——一次设置导入或备份恢复
///         不该能把"这台机器允许被远控"悄悄打开。
///     </para>
///     <para>
///         每次修改立刻原子落盘，所以页面不需要"保存"按钮；连接状态由 <see cref="ControlNodeClient" />
///         推送，订阅回调会切回 UI 线程再改属性。
///     </para>
/// </remarks>
public sealed partial class ControlSettingsPageViewModel : ViewModelBase, IDisposable
{
    private readonly IControlNodeStateStore _stateStore;
    private readonly ControlNodeClient _client;
    private readonly ControlNodeClientOptions _options;
    private readonly IControlPlaneEndpointStore _controlPlaneEndpointStore;
    private readonly IControlPlaneEndpointSettingsGate _controlPlaneEndpointGate;
    private readonly IExternalLauncher _externalLauncher;
    private readonly ILogger<ControlSettingsPageViewModel> _logger;
    private bool _suppressPersist;

    public ControlSettingsPageViewModel(
        MainConfigHandler configHandler,
        IControlNodeStateStore stateStore,
        ControlNodeClient client,
        ControlNodeClientOptions options,
        IControlPlaneEndpointStore controlPlaneEndpointStore,
        IControlPlaneEndpointSettingsGate controlPlaneEndpointGate,
        IExternalLauncher externalLauncher,
        ILogger<ControlSettingsPageViewModel> logger) : base(configHandler)
    {
        _stateStore = stateStore;
        _client = client;
        _options = options;
        _controlPlaneEndpointStore = controlPlaneEndpointStore;
        _controlPlaneEndpointGate = controlPlaneEndpointGate;
        _externalLauncher = externalLauncher;
        _logger = logger;

        RefreshFromState(_stateStore.Current);
        RefreshControlPlaneEndpoint();
        IsControlPlaneEndpointVisible = _controlPlaneEndpointGate.IsRevealed;
        ApplyLinkState(_client.LinkState);
        HostName = _options.HostName;

        _stateStore.Changed += OnStateStoreChanged;
        _client.LinkStateChanged += OnLinkStateChanged;
        _controlPlaneEndpointGate.Changed += OnControlPlaneEndpointGateChanged;
    }

    /// <summary>本机是否允许被集控。**这是设备自己的闸，服务端只读它。**</summary>
    [ObservableProperty] private bool _remoteControlEnabled;

    /// <summary>
    ///     本机主界面是否显示"远程抽取"页。**默认关闭**，打开后侧栏立刻出现，关掉立刻消失（不需要重启）。
    /// </summary>
    /// <remarks>
    ///     它决定的是**看得见看不见**，不是能不能抽：真的能不能下发抽取仍由服务端按组成员角色判定。
    ///     之所以不放进 <c>settings.json</c>、而和"允许被远程控制"共用 <c>node-state.json</c>：
    ///     这个入口会把本机账号令牌发往控制面，因此一次设置导入不该能替用户打开它。
    /// </remarks>
    [ObservableProperty] private bool _remoteDrawPageEnabled;

    [ObservableProperty] private string _groupId = string.Empty;

    [ObservableProperty] private string _serverUrl = string.Empty;

    [ObservableProperty] private string _nodeId = string.Empty;

    /// <summary>
    ///     控制台里显示的名称。**由用户在这台机器上填写**；留空则上报 <see cref="HostName" />。
    /// </summary>
    [ObservableProperty] private string _displayName = string.Empty;

    /// <summary>留空时上报的回落值（主机名）。同时作为输入框水印，省得用户去猜。</summary>
    public string HostName { get; }

    /// <summary>输入框的上限，直接取协议上限，避免这里再写一个字面量 64。</summary>
    public int DisplayNameMaxLength => ControlNodeDisplayName.MaxLength;

    [ObservableProperty] private string _statusText = string.Empty;

    [ObservableProperty] private string _statusDetail = string.Empty;

    /// <summary>抽取是否被控制台的期望状态锁定。</summary>
    [ObservableProperty] private bool _drawLocked;

    /// <summary>地址校验失败：仅提示，**无效地址绝不落盘**。</summary>
    [ObservableProperty] private bool _hasEndpointError;

    /// <summary>节点通道地址当前是否不是默认值（"恢复默认"按钮据此启用）。</summary>
    [ObservableProperty] private bool _isServerUrlCustom;

    /// <summary>
    ///     控制面地址设置卡是否可见。
    /// </summary>
    /// <remarks>
    ///     默认隐藏，只有调试页的总开关打开后才出现——改了它等于决定"这台设备的控制台连哪个平台"，
    ///     不是一项日常设置。开关只对本次运行有效（见 <see cref="IControlPlaneEndpointSettingsGate" />）。
    /// </remarks>
    [ObservableProperty] private bool _isControlPlaneEndpointVisible;

    /// <summary>控制台（手机/平板）访问集控平台所用的基址。留空即回到线上默认。</summary>
    [ObservableProperty] private string _controlPlaneEndpoint = string.Empty;

    /// <summary>控制面地址无效：仅提示，**无效地址绝不落盘**。</summary>
    [ObservableProperty] private bool _hasControlPlaneEndpointError;

    /// <summary>当前是否用的是自定义地址（"恢复默认"按钮据此启用）。</summary>
    [ObservableProperty] private bool _isControlPlaneEndpointCustom;

    /// <summary>
    ///     "打开集控平台"按钮的文案。
    /// </summary>
    /// <remarks>
    ///     配了第三方地址就**明说**打开的是哪一家：让按钮写着"SecRandom 集控平台"却跳到别人的服务器，
    ///     是这页面上最容易被误点的一处。
    /// </remarks>
    [ObservableProperty] private string _openControlPlaneLabel = string.Empty;

    /// <summary>线上默认地址，作为输入框水印：用户一眼能看到"不填会连哪"。</summary>
    public string ControlPlaneEndpointDefault => ControlPlaneClient.DefaultBaseUrl;

    public void Dispose()
    {
        _stateStore.Changed -= OnStateStoreChanged;
        _client.LinkStateChanged -= OnLinkStateChanged;
        _controlPlaneEndpointGate.Changed -= OnControlPlaneEndpointGateChanged;
    }

    [RelayCommand]
    private void Reconnect()
    {
        // 终态（未注册 / 不是组成员 / 地址错误）会一直等着，这个按钮让用户修正后立刻重试。
        _client.Wake();
    }

    /// <summary>丢弃自定义控制面地址，回到线上默认。</summary>
    [RelayCommand]
    private void ResetControlPlaneEndpoint()
    {
        _controlPlaneEndpointStore.ResetToDefault();
        RefreshControlPlaneEndpoint();
    }

    /// <summary>丢弃自定义节点通道地址，回到线上默认。</summary>
    [RelayCommand]
    private void ResetServerUrl()
    {
        // 只走状态存储这一条写路径：它原子落盘并触发 Changed，界面从同一个事件刷新。
        _stateStore.Update(state => state with { ServerUrl = ControlNodeClientOptions.DefaultEndpoint });
        _client.Wake();
    }

    /// <summary>在浏览器里打开集控平台网页控制台（配了第三方地址就打开那一家）。</summary>
    [RelayCommand]
    private void OpenControlPlane()
    {
        var url = _controlPlaneEndpointStore.Current;
        if (!_externalLauncher.TryOpenUri(url))
            _logger.LogWarning("打开集控平台失败：{Url}", url);
    }

    partial void OnControlPlaneEndpointChanged(string value)
    {
        if (_suppressPersist)
            return;

        var endpoint = value?.Trim() ?? string.Empty;

        // 清空输入框 = 回到默认：这条比"空值是非法输入"更好用，也让水印（默认地址）名副其实。
        if (endpoint.Length == 0)
        {
            _controlPlaneEndpointStore.ResetToDefault();
            HasControlPlaneEndpointError = false;
            RefreshControlPlaneEndpoint();
            return;
        }

        if (!_controlPlaneEndpointStore.TryUpdate(endpoint, out _))
        {
            // 无效地址绝不落盘：否则控制台会带着一个连不上的基址，每次请求都失败。
            HasControlPlaneEndpointError = true;
            return;
        }

        HasControlPlaneEndpointError = false;
        RefreshControlPlaneEndpoint();
    }

    partial void OnRemoteControlEnabledChanged(bool value)
    {
        if (_suppressPersist)
            return;

        _stateStore.Update(state => state with { RemoteControlEnabled = value });
        _client.Wake();
    }

    /// <summary>远程抽取页的显隐开关：只写本机状态，界面从 <c>Changed</c> 事件即时刷新。</summary>
    partial void OnRemoteDrawPageEnabledChanged(bool value)
    {
        if (_suppressPersist)
            return;

        _stateStore.Update(state => state with { RemoteDrawPageEnabled = value });
    }

    partial void OnGroupIdChanged(string value)
    {
        if (_suppressPersist)
            return;

        var groupId = value?.Trim() ?? string.Empty;
        _stateStore.Update(state => state with { GroupId = groupId });
        _client.Wake();
    }

    partial void OnDisplayNameChanged(string value)
    {
        if (_suppressPersist)
            return;

        // 名字保留用户输入的原样（含首尾空白），规范化只发生在落盘与上报两个边界：
        // 每次按键都 Trim 会跟输入光标打架——想在词中间打空格都做不到。
        _stateStore.Update(state => state with { DisplayName = value });
        _client.Wake();
    }

    partial void OnServerUrlChanged(string value)
    {
        if (_suppressPersist)
            return;

        var endpoint = value?.Trim() ?? string.Empty;
        if (!ControlEndpointPolicy.TryValidate(endpoint, out _, out _))
        {
            // 无效地址绝不落盘：否则节点会带着一个连不上的地址进入"已停止重试"。
            HasEndpointError = true;
            RefreshServerUrlCustomFlag();
            return;
        }

        HasEndpointError = false;
        _stateStore.Update(state => state with { ServerUrl = endpoint });
        _client.Wake();
        RefreshServerUrlCustomFlag();
    }

    /// <summary>节点通道地址是否偏离默认值（"恢复默认"按钮据此启用）。</summary>
    private void RefreshServerUrlCustomFlag() =>
        IsServerUrlCustom = !string.Equals(
            (ServerUrl ?? string.Empty).Trim(),
            ControlNodeClientOptions.DefaultEndpoint,
            StringComparison.OrdinalIgnoreCase);

    private void OnStateStoreChanged(object? sender, ControlNodeState state) =>
        RunOnUiThread(() => RefreshFromState(state));

    private void OnLinkStateChanged(object? sender, ControlNodeLinkState state) =>
        RunOnUiThread(() => ApplyLinkState(state));

    private void OnControlPlaneEndpointGateChanged(object? sender, EventArgs e) =>
        RunOnUiThread(() => IsControlPlaneEndpointVisible = _controlPlaneEndpointGate.IsRevealed);

    /// <summary>把存储里的控制面地址投影到界面（写入过程要被抑制，否则会回环写一遍）。</summary>
    private void RefreshControlPlaneEndpoint()
    {
        _suppressPersist = true;
        try
        {
            ControlPlaneEndpoint = _controlPlaneEndpointStore.Current;
            IsControlPlaneEndpointCustom = _controlPlaneEndpointStore.IsCustom;
            HasControlPlaneEndpointError = false;
            OpenControlPlaneLabel = IsControlPlaneEndpointCustom
                ? LR.C_OpenControlPlane_ThirdParty
                : LR.C_OpenControlPlane_Official;
        }
        finally
        {
            _suppressPersist = false;
        }
    }

    private void RefreshFromState(ControlNodeState state)
    {
        _suppressPersist = true;
        try
        {
            RemoteControlEnabled = state.RemoteControlEnabled;
            RemoteDrawPageEnabled = state.RemoteDrawPageEnabled;
            GroupId = state.GroupId;
            ServerUrl = state.ServerUrl;
            RefreshServerUrlCustomFlag();
            NodeId = state.NodeId;
            DisplayName = state.DisplayName ?? string.Empty;
            DrawLocked = state.DrawLocked;
        }
        finally
        {
            _suppressPersist = false;
        }
    }

    private void ApplyLinkState(ControlNodeLinkState state)
    {
        StatusText = DescribeStatus(state.Status);
        StatusDetail = DescribeDetail(state.Detail);
    }

    private static string DescribeStatus(ControlNodeLinkStatus status) => status switch
    {
        ControlNodeLinkStatus.Disabled => LR.M_Status_Disabled,
        ControlNodeLinkStatus.Idle => LR.M_Status_Idle,
        ControlNodeLinkStatus.Connecting => LR.M_Status_Connecting,
        ControlNodeLinkStatus.Connected => LR.M_Status_Connected,
        ControlNodeLinkStatus.WaitingToRetry => LR.M_Status_WaitingToRetry,
        ControlNodeLinkStatus.Blocked => LR.M_Status_Blocked,
        _ => status.ToString()
    };

    /// <summary>
    ///     把协议错误码翻译成用户能看懂的原因。
    /// </summary>
    /// <remarks>
    ///     界面上必须区分"本机不接受远控"与"服务端没权限"：两者原因完全不同，
    ///     混在一起会让老师以为是集控坏了。
    /// </remarks>
    private static string DescribeDetail(string? detail) => detail switch
    {
        null or "" or "stopped" => string.Empty,
        "not_configured" => LR.M_Detail_NotConfigured,
        "not_signed_in" => LR.M_Detail_NotSignedIn,
        ControlErrorCodes.NodeNotFound => LR.M_Detail_NodeNotFound,
        ControlErrorCodes.GroupNotFound => LR.M_Detail_GroupNotFound,
        ControlErrorCodes.InvalidRequest => LR.M_Detail_InvalidRequest,
        ControlErrorCodes.Unauthorized => LR.M_Detail_Unauthorized,
        "empty_endpoint" or "invalid_endpoint" or "endpoint_must_not_carry_credentials"
            or "plaintext_endpoint_requires_loopback" or "unsupported_endpoint_scheme" => LR.M_Detail_EndpointInvalid,
        _ => detail
    };

    private void RunOnUiThread(Action action)
    {
        try
        {
            if (Dispatcher.UIThread.CheckAccess())
                action();
            else
                Dispatcher.UIThread.Post(action);
        }
        catch (Exception exception)
        {
            // 订阅回调抛异常不能带走连接循环。
            _logger.LogDebug(exception, "刷新集控设置页状态失败。");
        }
    }
}
