using System;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.ControlNode;
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
    private readonly ILogger<ControlSettingsPageViewModel> _logger;
    private bool _suppressPersist;

    public ControlSettingsPageViewModel(
        MainConfigHandler configHandler,
        IControlNodeStateStore stateStore,
        ControlNodeClient client,
        ControlNodeClientOptions options,
        ILogger<ControlSettingsPageViewModel> logger) : base(configHandler)
    {
        _stateStore = stateStore;
        _client = client;
        _options = options;
        _logger = logger;

        RefreshFromState(_stateStore.Current);
        ApplyLinkState(_client.LinkState);
        HostName = _options.HostName;

        _stateStore.Changed += OnStateStoreChanged;
        _client.LinkStateChanged += OnLinkStateChanged;
    }

    /// <summary>本机是否允许被集控。**这是设备自己的闸，服务端只读它。**</summary>
    [ObservableProperty] private bool _remoteControlEnabled;

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

    public void Dispose()
    {
        _stateStore.Changed -= OnStateStoreChanged;
        _client.LinkStateChanged -= OnLinkStateChanged;
    }

    [RelayCommand]
    private void Reconnect()
    {
        // 终态（未注册 / 不是组成员 / 地址错误）会一直等着，这个按钮让用户修正后立刻重试。
        _client.Wake();
    }

    partial void OnRemoteControlEnabledChanged(bool value)
    {
        if (_suppressPersist)
            return;

        _stateStore.Update(state => state with { RemoteControlEnabled = value });
        _client.Wake();
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
            return;
        }

        HasEndpointError = false;
        _stateStore.Update(state => state with { ServerUrl = endpoint });
        _client.Wake();
    }

    private void OnStateStoreChanged(object? sender, ControlNodeState state) =>
        RunOnUiThread(() => RefreshFromState(state));

    private void OnLinkStateChanged(object? sender, ControlNodeLinkState state) =>
        RunOnUiThread(() => ApplyLinkState(state));

    private void RefreshFromState(ControlNodeState state)
    {
        _suppressPersist = true;
        try
        {
            RemoteControlEnabled = state.RemoteControlEnabled;
            GroupId = state.GroupId;
            ServerUrl = state.ServerUrl;
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
