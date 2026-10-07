using System;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Services.ControlNode;
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
    private readonly IExternalLauncher _externalLauncher;
    private readonly ILogger<ControlSettingsPageViewModel> _logger;
    private readonly INodeEnrollmentStore? _enrollmentStore;
    private readonly NodeEnrollmentClient? _enrollmentClient;
    private bool _suppressPersist;

    public ControlSettingsPageViewModel(
        MainConfigHandler configHandler,
        IControlNodeStateStore stateStore,
        ControlNodeClient client,
        ControlNodeClientOptions options,
        IControlPlaneEndpointStore controlPlaneEndpointStore,
        IExternalLauncher externalLauncher,
        ILogger<ControlSettingsPageViewModel> logger,
        INodeEnrollmentStore? enrollmentStore = null,
        NodeEnrollmentClient? enrollmentClient = null) : base(configHandler)
    {
        _stateStore = stateStore;
        _client = client;
        _options = options;
        _controlPlaneEndpointStore = controlPlaneEndpointStore;
        _externalLauncher = externalLauncher;
        _logger = logger;
        _enrollmentStore = enrollmentStore;
        _enrollmentClient = enrollmentClient;

        RefreshFromState(_stateStore.Current);
        NormalizeNodeEndpoint();
        RefreshControlPlaneEndpoint();
        RefreshEnrollment();
        ApplyLinkState(_client.LinkState);
        HostName = _options.HostName;

        _stateStore.Changed += OnStateStoreChanged;
        _client.LinkStateChanged += OnLinkStateChanged;

        if (_enrollmentStore is not null)
            _enrollmentStore.Changed += OnEnrollmentStoreChanged;
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

    /// <summary>
    ///     是否已经接入自建集控（接入码换来的节点令牌）。已接入时**不登录 SECTL 也能用**。
    /// </summary>
    [ObservableProperty] private bool _isEnrolled;

    /// <summary>
    ///     接入码输入框。用户手输或粘贴的一次性接入码，也可以是直接粘贴的 <c>srn_…</c> 令牌。
    /// </summary>
    /// <remarks>
    ///     它是普通输入框内容（用户自己贴进来的），**不是已保存的令牌**：保存后的令牌只存在于加密存储里，
    ///     这里永远不回填、不显示，因此不可能被截图或设置导出带出去。
    /// </remarks>
    [ObservableProperty] private string _enrollmentCode = string.Empty;

    /// <summary>接入状态一句话（未接入 / 已接入到哪个组 / 已过期 / 记录读不出来）。</summary>
    [ObservableProperty] private string _enrollmentStatusText = string.Empty;

    /// <summary>接入失败的提示；成功或未操作时为空。</summary>
    [ObservableProperty] private string _enrollmentMessage = string.Empty;

    /// <summary>
    ///     接入记录存在却读不出来（被改过、换了机器）。此时已按"未接入"处理，界面提示重新接入。
    /// </summary>
    [ObservableProperty] private bool _isEnrollmentUnreadable;

    /// <summary>正在接入（按钮转圈/禁用，避免连点换出多份令牌）。</summary>
    [ObservableProperty] private bool _isEnrolling;

    /// <summary>"接入"按钮是否可点：填了接入码、且当前没有正在进行的接入。</summary>
    public bool CanEnroll => !IsEnrolling && !string.IsNullOrWhiteSpace(EnrollmentCode);

    /// <summary>"清除接入"按钮是否可点（有记录、或记录已损坏需要清掉）。</summary>
    public bool CanClearEnrollment => _enrollmentStore is not null && (IsEnrolled || IsEnrollmentUnreadable);

    /// <summary>
    ///     接入码输入框的长度上限。
    /// </summary>
    /// <remarks>
    ///     数字留在 <see cref="NodeEnrollmentClient.MaxCodeLength" />，XAML 只绑这个属性：
    ///     跟设备显示名（<see cref="DisplayNameMaxLength" />）同一个写法，界面不写死业务数字。
    /// </remarks>
    public int EnrollmentCodeMaxLength => NodeEnrollmentClient.MaxCodeLength;

    /// <summary>
    ///     "接入"与"清除接入"共用 Footer 右侧同一格，这里是"接入"是否显示。
    /// </summary>
    /// <remarks>
    ///     与 <see cref="ShowClearEnrollment" /> **互补**：这一格永远恰好有一个按钮，
    ///     否则两个都显示会挤、两个都藏起来会让那一格空着、整行宽度跟着跳。
    /// </remarks>
    public bool ShowEnroll => !ShowClearEnrollment;

    /// <summary>
    ///     "清除接入"按钮是否显示：**有可清的记录、且输入框是空的**。
    /// </summary>
    /// <remarks>
    ///     输入框里有内容时用户要做的是"接入"（换组、换控制台），此时把清除藏起来——
    ///     同一格里两个动作会互相打断；想清除就先清空输入框。
    /// </remarks>
    public bool ShowClearEnrollment => CanClearEnrollment && string.IsNullOrWhiteSpace(EnrollmentCode);

    /// <summary>
    ///     失败提示出现时卡片自动展开。
    /// </summary>
    /// <remarks>
    ///     卡片默认收起（用户要求：这一页不要一进来就摊开接入卡），但提示只写在正文区，
    ///     收起的卡片会把唯一的错误信息一起藏掉；所以"有提示"是唯一的自动展开条件。
    /// </remarks>
    public bool ShowEnrollmentMessage => !string.IsNullOrEmpty(EnrollmentMessage);

    partial void OnEnrollmentCodeChanged(string value)
    {
        OnPropertyChanged(nameof(CanEnroll));
        NotifyEnrollmentButtonVisibility();
    }

    partial void OnIsEnrollingChanged(bool value) => OnPropertyChanged(nameof(CanEnroll));

    partial void OnIsEnrolledChanged(bool value)
    {
        OnPropertyChanged(nameof(CanClearEnrollment));
        NotifyEnrollmentButtonVisibility();
    }

    partial void OnIsEnrollmentUnreadableChanged(bool value)
    {
        OnPropertyChanged(nameof(CanClearEnrollment));
        NotifyEnrollmentButtonVisibility();
    }

    partial void OnEnrollmentMessageChanged(string value) => OnPropertyChanged(nameof(ShowEnrollmentMessage));

    /// <summary>
    ///     显隐必须**成对翻转**：只通知其中一个会出现"两个都在"或"一个都不在"的中间帧。
    /// </summary>
    private void NotifyEnrollmentButtonVisibility()
    {
        OnPropertyChanged(nameof(ShowClearEnrollment));
        OnPropertyChanged(nameof(ShowEnroll));
    }

    public void Dispose()
    {
        _stateStore.Changed -= OnStateStoreChanged;
        _client.LinkStateChanged -= OnLinkStateChanged;

        if (_enrollmentStore is not null)
            _enrollmentStore.Changed -= OnEnrollmentStoreChanged;
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

    /// <summary>
    ///     用控制台签发的接入码把本机接入自建集控。输入已经是 <c>srn_…</c> 时**不调接入接口**，直接存。
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         成功后写入两处：加密的接入记录（令牌真值）与节点状态里的组 ID/节点 ID——
    ///         组 ID 是节点连接配置的一部分，不写进去会出现"接入成功了却还报未配置"。
    ///     </para>
    ///     <para>
    ///         失败只把**错误码**显示出来：接入码本身绝不回显、绝不写日志。
    ///     </para>
    /// </remarks>
    [RelayCommand]
    private async Task EnrollAsync()
    {
        if (_enrollmentStore is null || _enrollmentClient is null || IsEnrolling)
            return;

        var code = EnrollmentCode?.Trim() ?? string.Empty;
        if (code.Length == 0)
            return;

        IsEnrolling = true;
        EnrollmentMessage = string.Empty;
        try
        {
            var record = NodeEnrollmentClient.LooksLikeNodeToken(code)
                // 用户直接粘贴了令牌：它自带节点身份，不经过接入码那条一次性通道。
                ? new NodeEnrollmentRecord
                {
                    NodeId = _stateStore.Current.NodeId,
                    GroupId = _stateStore.Current.GroupId,
                    NodeToken = code
                }
                : await _enrollmentClient.EnrollAsync(
                        code,
                        nodeId: _stateStore.Current.NodeId,
                        platform: _options.Platform,
                        version: _options.Version,
                        displayName: ControlNodeDisplayName.Resolve(_stateStore.Current.DisplayName, _options.HostName))
                    .ConfigureAwait(true);

            if (record is null || !_enrollmentStore.Save(record))
            {
                EnrollmentMessage = string.Format(LR.M_Enroll_Detail, "save_failed");
                return;
            }

            ApplyEnrollmentIdentity(record);
            EnrollmentCode = string.Empty;
            EnrollmentMessage = string.Empty;
        }
        catch (NodeEnrollmentException exception)
        {
            // 界面只显示服务端错误码：它足以区分"码错了/过期了/被用过了/被限流了"。
            EnrollmentMessage = string.Format(LR.M_Enroll_Detail, exception.ServerCode ?? exception.Failure.ToString());
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "接入自建集控失败。");
            EnrollmentMessage = string.Format(LR.M_Enroll_Detail, exception.GetType().Name);
        }
        finally
        {
            IsEnrolling = false;
        }
    }

    /// <summary>清除本机接入记录（令牌立即失效）并立刻断开节点连接。</summary>
    [RelayCommand]
    private void ClearEnrollment()
    {
        if (_enrollmentStore is null)
            return;

        _enrollmentStore.Clear();
        EnrollmentMessage = string.Empty;
        IsEnrollmentUnreadable = false;
        // 唤醒连接循环：它下一轮取不到凭据，会按"未接入"处理而不是继续拿着旧令牌重连。
        _client.Wake();
    }

    /// <summary>把接入结果里的身份写进节点状态：组 ID/节点 ID 是连接配置的一部分。</summary>
    private void ApplyEnrollmentIdentity(NodeEnrollmentRecord record)
    {
        var nodeId = string.IsNullOrWhiteSpace(record.NodeId) ? _stateStore.Current.NodeId : record.NodeId;
        var groupId = string.IsNullOrWhiteSpace(record.GroupId) ? _stateStore.Current.GroupId : record.GroupId;

        _suppressPersist = true;
        try
        {
            // 令牌是直接粘贴进来的那种，服务端身份要等下一次连接才会被纠正，这里只补已知的部分。
            _stateStore.Update(state => state with { NodeId = nodeId, GroupId = groupId });
        }
        finally
        {
            _suppressPersist = false;
        }

        // 接入成功就是"这台机器属于这台集控"的判定点：通道地址在这里对齐一次，
        // 之后即使基址没变过，冷启动读到的也是能连上的地址。
        NormalizeNodeEndpoint();
        _client.Wake();
    }

    private void OnEnrollmentStoreChanged(object? sender, NodeEnrollmentStatus status) =>
        RunOnUiThread(RefreshEnrollment);

    /// <summary>把接入状态翻译成一句话；**这里读不到令牌真值，所以不可能回显**。</summary>
    private void RefreshEnrollment()
    {
        var status = _enrollmentStore?.Status ?? NodeEnrollmentStatus.NotEnrolled;

        IsEnrolled = status.HasToken;
        IsEnrollmentUnreadable = status.IsUnreadable;
        EnrollmentStatusText = !status.HasToken
            ? status.IsUnreadable ? LR.M_Enroll_Unreadable : LR.M_Enroll_None
            : status.IsExpired
                ? string.Format(LR.M_Enroll_Expired, FormatExpiry(status.ExpiresAt))
                : string.Format(LR.M_Enroll_Active, DescribeEnrollmentTarget(status));
    }

    private string DescribeEnrollmentTarget(NodeEnrollmentStatus status)
    {
        var group = string.IsNullOrWhiteSpace(status.GroupName) ? status.GroupId : status.GroupName;
        return string.IsNullOrWhiteSpace(group) ? status.NodeId ?? string.Empty : group!;
    }

    private static string FormatExpiry(DateTimeOffset? expiresAt) =>
        expiresAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "-";

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
        var previousGroup = _stateStore.Current.GroupId;

        // **换组要先注销旧组**：不注销的话，这台设备会同时挂在新旧两个组的列表里。
        // 只在确实换了组时发；注销是尽力而为（超时/失败只记日志），绝不能挡住按新组重连。
        // 与另一半的区别：**退出程序不注销**（服务端"登记即列出"，关掉软件应保持 offline 可见）。
        if (!string.IsNullOrWhiteSpace(previousGroup)
            && !string.Equals(previousGroup, groupId, StringComparison.Ordinal))
        {
            _ = DeregisterAsync(previousGroup);
        }

        _stateStore.Update(state => state with { GroupId = groupId });
        _client.Wake();
    }

    /// <summary>注销的等待上限：换组/退出都不能被它拖住。</summary>
    private static readonly TimeSpan DeregisterTimeout = TimeSpan.FromSeconds(3);

    /// <summary>尽力而为地注销某个组里的登记；失败只记日志。</summary>
    private async Task DeregisterAsync(string groupId)
    {
        try
        {
            var deregistered = await _client.DeregisterAsync(groupId, DeregisterTimeout).ConfigureAwait(true);

            if (!deregistered)
                _logger.LogInformation("集控注销未得到确认（可能未连接或超时）：{Group}", groupId);
        }
        catch (Exception exception)
        {
            // 注销失败不能影响换组/退出：残留登记由服务端超时或管理员清理兜底。
            _logger.LogWarning(exception, "集控注销失败：{Group}", groupId);
        }
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

    /// <summary>
    ///     把节点通道地址对齐到自建集控的基址：<c>http://…</c> 的实例绝不能配 <c>wss://…</c> 的通道，
    ///     否则 WebSocket 会拿明文端口做 TLS 握手，永远连不上（日志里只有"集控节点连接失败"）。
    /// </summary>
    private void NormalizeNodeEndpoint()
    {
        var resolved = ControlNodeEndpointResolver.Resolve(
            _controlPlaneEndpointStore.Current,
            _stateStore.Current.ServerUrl,
            out var corrected);

        if (!corrected)
            return;

        _logger.LogInformation("集控节点通道地址按控制面基址修正：{Endpoint}", resolved);
        _stateStore.Update(state => state with { ServerUrl = resolved });
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

        // 控制面基址变了，节点通道地址要跟着走（自建实例的 http/https 决定通道的 ws/wss）。
        NormalizeNodeEndpoint();
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
            // 接入状态也要跟着刷新：本页是"显示状态"的地方，接入记录可能在别处（比如手机端）被改掉。
            RefreshEnrollment();
        }
        finally
        {
            _suppressPersist = false;
        }
    }

    private void ApplyLinkState(ControlNodeLinkState state)
    {
        StatusText = DescribeStatus(state);
        StatusDetail = DescribeDetail(state.Detail);
    }

    /// <summary>
    ///     连接状态文案。<c>Blocked</c> 还要再分一层：<b>已接入</b>却被连续拒绝时令牌多半已被服务端撤销，
    ///     唯一的出路是重新接入，只说"连接被阻断"会让老师以为集控坏了。
    /// </summary>
    private string DescribeStatus(ControlNodeLinkState state) => state.Status switch
    {
        ControlNodeLinkStatus.Disabled => LR.M_Status_Disabled,
        ControlNodeLinkStatus.Idle => LR.M_Status_Idle,
        ControlNodeLinkStatus.Connecting => LR.M_Status_Connecting,
        ControlNodeLinkStatus.Connected => LR.M_Status_Connected,
        ControlNodeLinkStatus.WaitingToRetry => LR.M_Status_WaitingToRetry,
        ControlNodeLinkStatus.Blocked => state.Detail == ControlErrorCodes.Unauthorized && IsEnrolled
            ? LR.M_Status_ReenrollRequired
            : LR.M_Status_Blocked,
        _ => state.Status.ToString()
    };

    /// <summary>
    ///     把协议错误码翻译成用户能看懂的原因。
    /// </summary>
    /// <remarks>
    ///     界面上必须区分"本机不接受远控"与"服务端没权限"：两者原因完全不同，
    ///     混在一起会让老师以为是集控坏了。
    /// </remarks>
    private string DescribeDetail(string? detail) => detail switch
    {
        null or "" or "stopped" => string.Empty,
        "not_configured" => LR.M_Detail_NotConfigured,
        "not_signed_in" => LR.M_Detail_NotSignedIn,
        // 已接入自建集控却取不到令牌（记录被清掉/读不出来）：这时让人"登录 SECTL"是死路，必须指向接入码。
        "not_enrolled" => LR.M_Detail_NotEnrolled,
        ControlErrorCodes.NodeNotFound => LR.M_Detail_NodeNotFound,
        ControlErrorCodes.GroupNotFound => LR.M_Detail_GroupNotFound,
        ControlErrorCodes.InvalidRequest => LR.M_Detail_InvalidRequest,
        // 已接入时服务端拒绝节点令牌：凭据是控制台签发的，本地无法自救，只能回接入卡重新接入。
        // 这句和"未登录"的区别在于用户要做的动作完全不同，所以不能复用 M_Detail_Unauthorized。
        ControlErrorCodes.Unauthorized when IsEnrolled => LR.M_Detail_EnrollmentRejected,
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
