using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Models;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Services.Auth;
using SecRandom.Services.ControlPlane;
using SecRandom.Shared.Models.ControlNode;
using SecRandom.Shared.Models.ControlPlane;
using LR = SecRandom.Langs.Mobile.Resources;

namespace SecRandom.ViewModels.Mobile;

/// <summary>
///     手机端"远程抽取"页：选设备 → 读名单 → 选条件 → 抽取 → 看结果。
/// </summary>
/// <remarks>
///     <para>
///         手机不自己抽：抽取发生在**教室机**上（那里才有名单、历史与公平性记录），
///         手机只负责选与看。因此每一步都是一次控制面命令：<c>roster.read</c> 取名单成员
///         （条件选项从成员派生，不写死"男/女/第一组"），<c>draw.trigger</c> 带参数下发，
///         再轮询回执把"抽到了谁"显示出来。
///     </para>
///     <para>
///         设备列表是**跨组扁平**的：我的组先拿到，再并发把每个组下面的节点合并进来。
///         一台机器挂在多个组下就按组分别列出一行——"哪台机器"和"挂在哪个组"是两个问题，
///         分两步选只会让老师多点一次。
///     </para>
///     <para>
///         每一种"做不了"都必须有话说：未登录、没有组、没有设备、部分组读不到、角色不够、
///         设备本机关了远控、设备没声明能力、命令被拒、超时。空白页会让老师以为是自己不会用。
///     </para>
/// </remarks>
public sealed partial class MobileRemoteDrawViewModel : ViewModelBase, IDisposable
{
    /// <summary>并发读组的上限。</summary>
    /// <remarks>
    ///     组数在几十个的账号上很常见，全部并发会在手机网络下把首屏拖成"转圈很久"；
    ///     4 个并发是"够快"与"不压垮手机/服务端"之间的折中。
    /// </remarks>
    private const int GroupLoadConcurrency = 4;

    private readonly IControlPlaneClient _client;
    private readonly IControlPlaneDevicePreferenceStore _preferences;
    private readonly SectlAuthService _auth;
    private readonly ILogger<MobileRemoteDrawViewModel> _logger;
    private bool _subscribed;
    private bool _selectingDevice;

    /// <summary>凭据失效（刷新后仍 401）：页面要给出"重新登录"，而不是空态。</summary>
    private bool _requiresSignIn;

    public MobileRemoteDrawViewModel(
        MainConfigHandler configHandler,
        IControlPlaneClient client,
        IControlPlaneDevicePreferenceStore preferences,
        SectlAuthService auth,
        ILogger<MobileRemoteDrawViewModel> logger)
        : base(configHandler)
    {
        _client = client;
        _preferences = preferences;
        _auth = auth;
        _logger = logger;
        IsSignedIn = auth.IsSignedIn;
    }

    /// <summary>跨组扁平的设备列表（"组 × 节点"一行）。</summary>
    public ObservableCollection<DeviceRow> Devices { get; } = [];

    /// <summary>读取失败的组：部分失败不能变成整页失败。</summary>
    public ObservableCollection<string> UnavailableGroups { get; } = [];

    public ObservableCollection<RosterOption> Rosters { get; } = [];

    public ObservableCollection<string> GenderOptions { get; } = [];

    public ObservableCollection<string> GroupOptions { get; } = [];

    public ObservableCollection<int> CountOptions { get; } = [];

    public ObservableCollection<NodeCommandDrawnMember> DrawnMembers { get; } = [];

    [ObservableProperty] private bool _isBusy;

    [ObservableProperty] private bool _isLoadingDevices;

    [ObservableProperty] private string _statusText = string.Empty;

    /// <summary>加载设备列表时的失败说明；成功或还没加载过时为空。</summary>
    /// <remarks>
    ///     <b>必须单独成一个字段，并且抢在"没有组/没有设备"之前显示。</b>
    ///     线上就是这么坏的：请求打错域名拿了 404，页面却只显示"这些组里还没有设备"，
    ///     同一个账号在控制台里明明看得见组，用户只能得出"手机拿不到组"的结论。
    ///     把失败呈现成"没有数据"是最糟的一种错误处理——它把故障伪装成正常状态。
    /// </remarks>
    [ObservableProperty] private string _loadFailure = string.Empty;

    [ObservableProperty] private bool _isSignedIn;

    [ObservableProperty] private DeviceRow? _selectedDevice;

    [ObservableProperty] private RosterOption? _selectedRoster;

    [ObservableProperty] private string? _selectedGender;

    [ObservableProperty] private string? _selectedGroupScope;

    [ObservableProperty] private int _selectedCount = 1;

    /// <summary>"不限"选项的显示文本。性别与分组列表的第一项永远是它。</summary>
    public static string AnyOption => LR.RD_Any;

    public bool HasDevices => Devices.Count > 0;

    public bool HasSelection => SelectedDevice is not null;

    public bool HasRoster => SelectedRoster is not null;

    public bool HasResult => DrawnMembers.Count > 0;

    public bool HasUnavailableGroups => UnavailableGroups.Count > 0;

    /// <summary>该说点什么的时候（失败、空态或降级原因）为真。</summary>
    public bool HasEmptyState => EmptyStateText.Length > 0;

    /// <summary>上次加载失败了（页面必须显示失败原因，而不是"没有设备"）。</summary>
    public bool HasLoadFailure => LoadFailure.Length > 0;

    /// <summary>需要重新登录：未登录，或凭据已失效。</summary>
    public bool NeedsSignIn => !IsSignedIn || _requiresSignIn;

    public string DeviceSummary => SelectedDevice?.Summary ?? string.Empty;

    public string DevicesCountText => string.Format(LR.RD_DevicesCount, Devices.Count);

    /// <summary>能不能"读取名单"：选中设备可用、本机没关远控、设备声明了名单读取、组角色够。</summary>
    public bool CanLoadRoster =>
        !IsBusy
        && IsSignedIn
        && SelectedDevice is { IsUsable: true }
        && SelectedDevice.SupportsRosterRead
        && SelectedDevice.Group.CanReadRoster;

    /// <summary>能不能抽：还要有已读到的名单、设备声明了抽取、组角色够。</summary>
    public bool CanDraw =>
        CanLoadRoster
        && SelectedDevice!.SupportsDraw
        && SelectedDevice.Group.CanOperateNodes
        && HasRoster;

    /// <summary>该显示的失败/空态/降级说明；不需要时为空白。</summary>
    /// <remarks>
    ///     判定顺序就是这个页面的"用户体验优先级"：**失败先说失败**，然后才是未登录、
    ///     正在加载、没有设备。把失败排到后面，就会出现"网络断了却告诉用户没有组"这种把人带偏的提示。
    /// </remarks>
    public string EmptyStateText => ResolveEmptyState(
        IsSignedIn,
        IsLoadingDevices,
        HasDevices,
        LoadFailure,
        UnavailableGroups.Count,
        SelectedDevice?.UnavailableReason,
        SelectedDevice?.Group.IsKnownInsufficientRole == true
            ? SelectedDevice.Group.CanReadRoster ? LR.RD_NotOperator : LR.RD_NotAdmin
            : null,
        HasRoster);

    /// <summary>
    ///     失败/空态文案的判定（纯函数，便于单测）。
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         四种状态必须分开，顺序错了就会把故障说成空数据——线上就是"请求 404"被渲染成
    ///         "这些组里还没有设备"：
    ///     </para>
    ///     <list type="number">
    ///         <item>请求失败（未登录/凭据失效/网络/服务端错误码）→ 直接说失败原因；</item>
    ///         <item>未登录 → 未登录文案；</item>
    ///         <item>正在加载 → 加载中文案；</item>
    ///         <item><b>有组读取失败且一台设备都没读到</b> → "有 N 个组没读到"，<b>不是</b>"没有设备"；</item>
    ///         <item>真的读到 0 台设备 → 才是"还没有设备"。</item>
    ///     </list>
    /// </remarks>
    public static string ResolveEmptyState(
        bool isSignedIn,
        bool isLoading,
        bool hasDevices,
        string? loadFailure,
        int failedGroupCount,
        string? unavailableReason,
        string? roleHint,
        bool hasRoster)
    {
        if (!string.IsNullOrWhiteSpace(loadFailure))
            return loadFailure!;

        if (!isSignedIn)
            return LR.RD_SignedOut;

        if (isLoading)
            return LR.RD_LoadingDevices;

        if (!hasDevices)
        {
            // 关键的一条：**读失败**不等于**没有设备**。
            return failedGroupCount > 0
                ? string.Format(LR.RD_GroupsUnavailable, failedGroupCount)
                : LR.RD_NoDevicesHint;
        }

        return unavailableReason ?? roleHint ?? (hasRoster ? string.Empty : LR.RD_NoRoster);
    }

    /// <summary>进页面时调用：记住的设备优先，其次第一台在线的。</summary>
    public async Task InitializeAsync()
    {
        Subscribe();
        IsSignedIn = _auth.IsSignedIn;
        _requiresSignIn = _auth.RequiresReauthorization;
        RefreshDerived();

        if (!IsSignedIn)
        {
            LoadFailure = string.Empty;
            RefreshDerived();
            return;
        }

        await LoadDevicesAsync().ConfigureAwait(true);
    }

    /// <summary>凭据失效时给用户的下一步：重新走一次 SECTL 登录，然后重新拉设备。</summary>
    [RelayCommand]
    private async Task SignInAsync()
    {
        try
        {
            await _auth.SignInAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "重新登录 SECTL 账号失败。");
            LoadFailure = ControlPlaneMessages.Describe(exception);
            RefreshDerived();
            return;
        }

        IsSignedIn = _auth.IsSignedIn;
        _requiresSignIn = _auth.RequiresReauthorization;
        LoadFailure = string.Empty;
        RefreshDerived();

        if (!IsSignedIn)
            return;

        await LoadDevicesAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsSignedIn = _auth.IsSignedIn;
        _requiresSignIn = _auth.RequiresReauthorization;
        if (!IsSignedIn)
        {
            LoadFailure = string.Empty;
            RefreshDerived();
            return;
        }

        await LoadDevicesAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task LoadRosterAsync()
    {
        var device = SelectedDevice;
        if (device is null || !device.IsUsable
            || string.IsNullOrWhiteSpace(device.GroupId) || string.IsNullOrWhiteSpace(device.NodeId))
            return;

        IsBusy = true;
        StatusText = LR.RD_Loading;
        RefreshDerived();

        try
        {
            var command = await _client.SubmitCommandAsync(
                device.GroupId,
                device.NodeId,
                new ControlPlaneCommandRequest
                {
                    Capability = ControlCapabilities.RosterRead,
                    Kind = "query",
                    Payload = JsonSerializer.SerializeToElement(
                        new RosterReadPayload { RosterKind = "students" },
                        ControlProtocolJson.Options)
                }).ConfigureAwait(true);

            var finished = await _client.PollCommandAsync(device.GroupId, command.CommandId!).ConfigureAwait(true);
            if (!finished.IsSucceeded)
            {
                StatusText = ControlPlaneMessages.DescribeCommand(finished);
                return;
            }

            ApplyRoster(finished);
        }
        catch (Exception exception)
        {
            ApplyFailure(exception, "读取教室机名单失败。");
        }
        finally
        {
            IsBusy = false;
            RefreshDerived();
        }
    }

    [RelayCommand]
    private async Task DrawAsync()
    {
        var device = SelectedDevice;
        var roster = SelectedRoster;
        if (device is null || roster is null || !device.IsUsable
            || string.IsNullOrWhiteSpace(device.GroupId) || string.IsNullOrWhiteSpace(device.NodeId))
            return;

        IsBusy = true;
        // 上一次的提示（失败态、上一次抽到的人）必须在这里全部清掉：留着就会出现
        // "这次抽成功了，屏幕上还挂着上一次的失败文案"。
        DrawnMembers.Clear();
        LoadFailure = string.Empty;
        StatusText = LR.RD_Drawing;
        RefreshDerived();

        try
        {
            // target=roll_call：手机要的是"在教室里点名抽一次"，不是快抽一次。
            // 名单与条件按页面上看到的原样下发，设备侧用点名会话与同一套候选规则执行，
            // 结果留在教室机屏幕上——远程抽取必须让课堂看得见。
            var payload = JsonSerializer.SerializeToElement(
                new DrawTriggerPayload
                {
                    Target = "roll_call",
                    ListName = roster.Name,
                    Count = Math.Clamp(SelectedCount, 1, Math.Max(1, roster.MemberCount)),
                    Gender = ScopeOf(SelectedGender),
                    Group = ScopeOf(SelectedGroupScope)
                },
                ControlProtocolJson.Options);

            var command = await _client.SubmitCommandAsync(
                device.GroupId,
                device.NodeId,
                new ControlPlaneCommandRequest
                {
                    Capability = ControlCapabilities.DrawTrigger,
                    Payload = payload
                }).ConfigureAwait(true);

            var finished = await _client.PollCommandAsync(device.GroupId, command.CommandId!).ConfigureAwait(true);

            foreach (var member in finished.DrawnMembers())
                DrawnMembers.Add(member);

            // 回执取不到成员时把原始 detail 记下来：这正是"抽成功却显示没人"的案发现场。
            if (finished.IsSucceeded && DrawnMembers.Count == 0)
            {
                _logger.LogWarning(
                    "远程抽取回执里没有成员：status={Status}，detail={Detail}",
                    finished.Status,
                    finished.ResultDiagnostics ?? "(none)");
            }

            StatusText = ControlPlaneMessages.DescribeDrawResult(finished);
        }
        catch (Exception exception)
        {
            ApplyFailure(exception, "远程抽取失败。");
        }
        finally
        {
            IsBusy = false;
            RefreshDerived();
        }
    }

    public void Dispose() => Unsubscribe();

    /// <summary>
    ///     拉取"我的组 → 每组的设备"，合并成一份扁平列表。
    /// </summary>
    private async Task LoadDevicesAsync()
    {
        IsLoadingDevices = true;
        LoadFailure = string.Empty;
        StatusText = LR.RD_LoadingDevices;
        Devices.Clear();
        UnavailableGroups.Clear();
        SelectedDevice = null;
        RefreshDerived();

        IReadOnlyList<GroupDto> groups;
        try
        {
            groups = await _client.GetGroupsAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            // 失败**一定**要落进 LoadFailure：只写状态行的话，页面的空态会继续显示
            // "这些组里还没有设备"，用户看到的仍然是"没有组"，而不是"请求失败了"。
            ApplyFailure(exception, "读取控制面分组失败。");
            IsLoadingDevices = false;
            RefreshDerived();
            return;
        }

        var rows = new List<DeviceRow>();
        var failed = new List<string>();
        var gate = new SemaphoreSlim(GroupLoadConcurrency);

        // 并发但有上限；单组失败只记这一组，别的组照常可用（否则一个坏组会让整页变空）。
        await Task.WhenAll(groups.Select(async group =>
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (string.IsNullOrWhiteSpace(group.GroupId))
                    return;

                var nodes = await _client.GetNodesAsync(group.GroupId!).ConfigureAwait(false);
                lock (rows)
                {
                    foreach (var node in nodes)
                    {
                        if (!string.IsNullOrWhiteSpace(node.NodeId))
                            rows.Add(new DeviceRow(group, node));
                    }
                }
            }
            catch (Exception exception)
            {
                // **"这个组读失败"与"这个组没有设备"是两件事**：404/401/网络错误必须显示出来，
                // 否则整页会落进"还没有设备"的空态，把服务端错误说成设备不存在（线上就是这样）。
                _logger.LogWarning(
                    exception,
                    "读取组 {Group} 的设备失败：{Reason}",
                    group.GroupId,
                    exception is ControlPlaneException { RequestUri: { } uri } controlPlane
                        ? $"{controlPlane.Kind}/{controlPlane.Code} {uri}"
                        : exception.Message);

                lock (failed)
                    failed.Add(string.Format(
                        LR.RD_GroupLoadFailed,
                        group.DisplayLabel,
                        exception is ControlPlaneException { Code: { } code } known ? known.Code : "unknown"));
            }
            finally
            {
                gate.Release();
            }
        })).ConfigureAwait(true);

        gate.Dispose();

        // 在线优先、同组相邻：老师扫一眼就能找到那台正在亮着的机器。
        foreach (var row in rows
                     .OrderByDescending(static row => row.IsUsable)
                     .ThenByDescending(static row => row.IsOnline)
                     .ThenBy(static row => row.GroupLabel, StringComparer.Ordinal)
                     .ThenBy(static row => row.DeviceLabel, StringComparer.Ordinal))
        {
            Devices.Add(row);
        }

        foreach (var message in failed)
            UnavailableGroups.Add(message);

        ApplyPreferredDevice();

        // 一组设备都没读到、但组本身读到了：不是网络失败，而是"组里没有设备"或"部分组读不到"，
        // 两者都由 UnavailableGroups / 空态文案说明，因此这里**清掉**失败标记（它只表示"列表没拿到"）。
        IsLoadingDevices = false;
        RefreshDerived();
    }

    /// <summary>把一个失败同时落到状态行与失败态，并识别"凭据失效"。</summary>
    private void ApplyFailure(Exception exception, string logMessage)
    {
        _logger.LogWarning(
            exception,
            "{Message}{Url}",
            logMessage,
            exception is ControlPlaneException { RequestUri: { } uri } ? $"（{uri}）" : string.Empty);

        var message = ControlPlaneMessages.DescribeWithCode(exception);
        StatusText = message;
        LoadFailure = message;

        if (exception is ControlPlaneException { Kind: ControlPlaneErrorKind.Unauthorized })
            _requiresSignIn = true;
    }

    /// <summary>记住的设备优先，其次第一台能用的、再其次第一台在线的、最后第一台。</summary>
    private void ApplyPreferredDevice()
    {
        var preference = _preferences.Load();

        _selectingDevice = true;
        try
        {
            SelectedDevice =
                (preference is null ? null : Devices.FirstOrDefault(row => row.Matches(preference.GroupId, preference.NodeId)))
                ?? Devices.FirstOrDefault(static row => row.IsUsable && row.IsOnline)
                ?? Devices.FirstOrDefault(static row => row.IsUsable)
                ?? Devices.FirstOrDefault(static row => row.IsOnline)
                ?? Devices.FirstOrDefault();
        }
        finally
        {
            _selectingDevice = false;
        }

        RefreshDerived();
    }

    private void ApplyRoster(NodeCommandDto command)
    {
        if (command.ResultPayload is not { } payload)
        {
            StatusText = LR.RD_NoRoster;
            return;
        }

        ControlRosterReadResponse? response;
        try
        {
            response = payload.Deserialize<ControlRosterReadResponse>(ControlProtocolJson.Options);
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "教室机返回的名单无法解析。");
            StatusText = string.Format(LR.RD_Error, exception.Message);
            return;
        }

        Rosters.Clear();
        foreach (var list in response?.Lists ?? [])
        {
            Rosters.Add(new RosterOption(
                list.Name,
                list.IsDefault,
                list.Count,
                list.Total,
                list.Truncated,
                list.Members));
        }

        SelectedRoster = Rosters.FirstOrDefault(option => option.IsDefault) ?? Rosters.FirstOrDefault();

        if (SelectedRoster is null)
        {
            StatusText = LR.RD_NoRoster;
            return;
        }

        StatusText = SelectedRoster.Truncated
            ? string.Format(LR.RD_Truncated, SelectedRoster.MemberCount)
            : string.Format(LR.RD_Total, SelectedRoster.MemberCount);
    }

    partial void OnSelectedDeviceChanged(DeviceRow? value)
    {
        RefreshDerived();

        if (value is null)
            return;

        // 换设备＝换机器：上一台机器的名单与结果都属于那台机器，留着只会让人对着 A 的名单给 B 发命令。
        if (!_selectingDevice)
        {
            Rosters.Clear();
            SelectedRoster = null;
            DrawnMembers.Clear();
            StatusText = value.UnavailableReason ?? string.Empty;
        }

        try
        {
            _preferences.Save(new ControlPlaneDevicePreference(value.GroupId, value.NodeId));
        }
        catch (Exception exception)
        {
            // 记不住上次的选择只是少一点方便，不该让选设备这一步失败。
            _logger.LogWarning(exception, "保存上次选中的设备失败。");
        }
    }

    partial void OnSelectedRosterChanged(RosterOption? value)
    {
        // 条件选项**从这份名单的成员派生**：分组名是老师自己输入的，写死的选项在真实名单上一定选不中。
        GenderOptions.Clear();
        GenderOptions.Add(AnyOption);
        foreach (var gender in OptionsOf(value?.Members.Select(member => member.Gender)))
            GenderOptions.Add(gender);

        GroupOptions.Clear();
        GroupOptions.Add(AnyOption);
        foreach (var group in OptionsOf(value?.Members.Select(member => member.Group)))
            GroupOptions.Add(group);

        CountOptions.Clear();
        var maximum = Math.Max(1, value?.MemberCount ?? 1);
        for (var count = 1; count <= maximum; count++)
            CountOptions.Add(count);

        SelectedGender = AnyOption;
        SelectedGroupScope = AnyOption;
        SelectedCount = 1;

        RefreshDerived();
    }

    partial void OnIsBusyChanged(bool value) => RefreshDerived();

    private void Subscribe()
    {
        if (_subscribed)
            return;

        _auth.StateChanged += OnAuthStateChanged;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed)
            return;

        _auth.StateChanged -= OnAuthStateChanged;
        _subscribed = false;
    }

    private void OnAuthStateChanged(object? sender, EventArgs e)
    {
        var wasSignedIn = IsSignedIn;
        IsSignedIn = _auth.IsSignedIn;
        _requiresSignIn = _auth.RequiresReauthorization;
        RefreshDerived();

        // 登录态是启动后异步装回来的（也可能刚在设置页登录完）：页面已经打开时，这次变化必须自己
        // 把设备列表拉起来，否则用户看到的是一个不会再刷新的"未登录"空态。
        if (!wasSignedIn && IsSignedIn && !HasDevices && !IsLoadingDevices && LoadFailure.Length == 0)
            _ = RefreshAsync();
    }

    private void RefreshDerived()
    {
        OnPropertyChanged(nameof(HasDevices));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasRoster));
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(HasUnavailableGroups));
        OnPropertyChanged(nameof(HasEmptyState));
        OnPropertyChanged(nameof(HasLoadFailure));
        OnPropertyChanged(nameof(NeedsSignIn));
        OnPropertyChanged(nameof(DeviceSummary));
        OnPropertyChanged(nameof(DevicesCountText));
        OnPropertyChanged(nameof(CanLoadRoster));
        OnPropertyChanged(nameof(CanDraw));
        OnPropertyChanged(nameof(EmptyStateText));
    }

    private static string? ScopeOf(string? option) =>
        string.IsNullOrEmpty(option) || string.Equals(option, AnyOption, StringComparison.Ordinal) ? null : option;

    private static IEnumerable<string> OptionsOf(IEnumerable<string?>? values) =>
        (values ?? [])
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value!.Trim())
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal);

    private sealed record RosterReadPayload
    {
        [JsonPropertyName("roster_kind")] public required string RosterKind { get; init; }
    }

    /// <summary><c>draw.trigger</c> 的载荷（协议字段名钉在这里）。</summary>
    private sealed record DrawTriggerPayload
    {
        [JsonPropertyName("target")] public required string Target { get; init; }

        [JsonPropertyName("list_name")] public required string ListName { get; init; }

        [JsonPropertyName("count")] public int Count { get; init; }

        [JsonPropertyName("gender")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Gender { get; init; }

        [JsonPropertyName("group")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Group { get; init; }
    }
}
