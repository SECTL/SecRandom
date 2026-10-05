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
    private readonly IControlNodeStateStore? _nodeStateStore;
    private bool _subscribed;
    private bool _selectingDevice;

    /// <summary>设备加载的序号：新请求一开始就作废旧请求（取代 = 这条流程上的"取消"）。</summary>
    private int _deviceLoadToken;

    /// <summary>凭据失效（刷新后仍 401）：页面要给出"重新登录"，而不是空态。</summary>
    private bool _requiresSignIn;

    public MobileRemoteDrawViewModel(
        MainConfigHandler configHandler,
        IControlPlaneClient client,
        IControlPlaneDevicePreferenceStore preferences,
        SectlAuthService auth,
        ILogger<MobileRemoteDrawViewModel> logger,
        IControlNodeStateStore? nodeStateStore = null)
        : base(configHandler)
    {
        _client = client;
        _preferences = preferences;
        _auth = auth;
        _logger = logger;
        // 手机没有本地集控节点，因此这里允许为 null —— 那种情况下设备列表里一台都不标"本机"。
        _nodeStateStore = nodeStateStore;
        IsSignedIn = auth.IsSignedIn;

        // 选项在这里现建：界面语言可以在运行中切换，缓存在静态字段里就会冻结在首次访问时的那一国语言。
        DrawKindOptions.Add(RemoteDrawKindOption.CreateRollCall());
        DrawKindOptions.Add(RemoteDrawKindOption.CreateLottery());
        _selectedDrawKind = DrawKindOptions[0];
    }

    /// <summary>跨组扁平的设备列表（"组 × 节点"一行）。</summary>
    public ObservableCollection<DeviceRow> Devices { get; } = [];

    /// <summary>可选的抽取类型：点名与抽奖。</summary>
    public ObservableCollection<RemoteDrawKindOption> DrawKindOptions { get; } = [];

    /// <summary>读取失败的组：部分失败不能变成整页失败。</summary>
    public ObservableCollection<string> UnavailableGroups { get; } = [];

    public ObservableCollection<RosterOption> Rosters { get; } = [];

    /// <summary>已读奖池里出现过的标签：抽奖条件里 <c>prize_tags</c> 的**唯一合法取值域**。</summary>
    public ObservableCollection<PrizeTagOption> PrizeTags { get; } = [];

    /// <summary>已选标签（多选控件直接绑这个集合）。</summary>
    public ObservableCollection<PrizeTagOption> SelectedPrizeTags { get; } = [];

    /// <summary>发放对象名单（教室机上的学生名单名）：奖品要发给这些名单里的学生。</summary>
    public ObservableCollection<string> RecipientListNames { get; } = [];

    /// <summary>发放对象的性别/分组范围：从所选发放名单的成员派生，不是写死的"男/女"。</summary>
    public ObservableCollection<string> RecipientGenderOptions { get; } = [];

    public ObservableCollection<string> RecipientGroupOptions { get; } = [];

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

    /// <summary>当前抽取类型（点名 / 抽奖）。切换它会清掉上一类的名单与结果。</summary>
    [ObservableProperty] private RemoteDrawKindOption _selectedDrawKind;

    /// <summary>"重置本轮"等第二次点击确认时为真。</summary>
    /// <remarks>
    ///     重置是破坏性动作（抹掉这一轮的抽取进度），因此不做成"点一下就生效"：
    ///     手机放在讲台上时，一次误触不该把课堂进行到一半的进度清掉。
    /// </remarks>
    [ObservableProperty] private bool _isResetConfirmPending;

    [ObservableProperty] private string? _selectedGender;

    [ObservableProperty] private string? _selectedGroupScope;

    [ObservableProperty] private int _selectedCount = 1;

    /// <summary>发放对象名单（空＝不指定：奖品整池抽，不发给谁）。</summary>
    [ObservableProperty] private string? _selectedRecipientList;

    [ObservableProperty] private string? _selectedRecipientGender;

    [ObservableProperty] private string? _selectedRecipientGroup;

    /// <summary>
    ///     这台设备声明了 <c>draw.trigger.conditions</c> 吗。
    /// </summary>
    /// <remarks>
    ///     这是**发送条件集的唯一闸门**：本次改动之前的设备不认识 <c>conditions</c>，
    ///     会把它当"没写"从而静默按整池抽——所以"设备没声明就绝不发"，
    ///     宁可让界面不显示条件（并说清为什么），也不制造"设了条件其实没生效"。
    /// </remarks>
    public bool SupportsDrawConditions =>
        SelectedDevice?.Node.Supports(ControlCapabilities.DrawTriggerConditions) == true;

    /// <summary>抽奖档下的条件区是否可见：既要是抽奖，设备也要声明支持。</summary>
    public bool ShowsLotteryConditions => IsLotteryTarget && SupportsDrawConditions;

    /// <summary>能不能选发放对象（只有声明了条件的抽奖设备才有这一档）。</summary>
    public bool CanUseRecipientScope => ShowsLotteryConditions;

    /// <summary>抽奖设备不支持条件时给一句解释，而不是把控件藏得让人以为功能没了。</summary>
    public bool ShowsLotteryConditionsUnsupported => IsLotteryTarget && !SupportsDrawConditions;

    /// <summary>已经选定发放对象名单：只有这时性别/分组范围才有可筛的东西。</summary>
    public bool HasRecipientScope => !string.IsNullOrWhiteSpace(SelectedRecipientList);

    /// <summary>"不限"选项的显示文本。性别与分组列表的第一项永远是它。</summary>
    public static string AnyOption => LR.RD_Any;

    public bool HasDevices => Devices.Count > 0;

    public bool HasSelection => SelectedDevice is not null;

    public bool HasRoster => SelectedRoster is not null;

    /// <summary>
    ///     本机节点 id（没有节点身份的宿主，例如手机，返回空串）。
    /// </summary>
    /// <remarks>
    ///     只读本机状态文件，**不额外发请求、也不动协议**：服务端本来就把 node_id 放在设备行里，
    ///     本机自己也知道自己是谁，两边比一下就够了。
    /// </remarks>
    private string OwnNodeId => _nodeStateStore?.Current.NodeId ?? string.Empty;

    /// <summary>选中的那台就是本机：页面要给出明确说明，避免"把命令下给自己"。</summary>
    /// <remarks>
    ///     只提示、**不禁止**选择：有人确实想给本机下发命令，直接禁掉会变成一句说不清的拒绝。
    /// </remarks>
    public bool IsSelfSelected => SelectedDevice?.IsSelf == true;

    /// <summary>选中本机时给出的说明（两个视图共用同一份文案）。</summary>
    public static string SelfSelectedHint => LR.RD_SelfSelectedHint;

    /// <summary>
    ///     结果区空态的第一行（"还没有结果"）。
    /// </summary>
    /// <remarks>
    ///     空态文案放在 **VM** 里而不是各视图里各绑一个资源键：两个视图共用同一份来源，
    ///     否则改一次文案要改两处，迟早有一边留着旧话。
    /// </remarks>
    public static string ResultPlaceholderTitle => LR.RD_NoResult;

    /// <summary>结果区空态的第二行（回执里会出现什么）。</summary>
    public static string ResultPlaceholderHint => LR.RD_NoResult_D;

    public bool HasResult => DrawnMembers.Count > 0;

    /// <summary>
    ///     结果区标题旁的计数（"抽到 N 人" / "抽到 N 个奖品"）。没有回执时是空串。
    /// </summary>
    /// <remarks>
    ///     抽奖抽的是奖品、点名抽的是人，量词不能混用；放在 VM 里而不是各视图里拼，
    ///     是为了让手机视图与桌面视图显示同一句话——两侧各写一遍必然有一天对不上。
    /// </remarks>
    public string DrawnCountText => DrawnMembers.Count == 0
        ? string.Empty
        : string.Format(IsLotteryTarget ? LR.RD_DrawnPrizeCount : LR.RD_DrawnCount, DrawnMembers.Count);

    /// <summary>当前选的是抽奖：名单下拉读奖池，且没有性别/分组条件。</summary>
    public bool IsLotteryTarget => string.Equals(
        SelectedDrawKind?.Target, ControlDrawTriggerRequest.TargetLottery, StringComparison.Ordinal);

    /// <summary>名单下拉的标题：点名读名单，抽奖读奖池。</summary>
    /// <remarks>奖池不叫名单。用同一个词会让老师以为自己在选点名名单，而抽出来的是奖品。</remarks>
    public string ListFieldLabel => IsLotteryTarget ? LR.RD_Pool : LR.RD_List;

    /// <summary>性别与分组只在点名时有意义，抽奖时整块隐藏（并给一句解释）。</summary>
    public bool IsScopeSelectorVisible => HasRoster && !IsLotteryTarget;

    /// <summary>抽奖模式下的解释行：为什么这里没有性别/分组。</summary>
    public bool IsLotteryScopeHintVisible => IsLotteryTarget;

    /// <summary>
    ///     能不能"重置本轮"：要有已读到的名单、设备可用、且当前没有别的命令在跑。
    /// </summary>
    /// <remarks>
    ///     重置按钮是**次级按钮**，但它仍然要遵守与抽取相同的可用性前提：
    ///     没有名单就没有"这一轮"可清，设备不可用则命令注定失败。
    /// </remarks>
    public bool CanReset =>
        !IsBusy
        && HasRoster
        && SelectedDevice is { IsUsable: true };

    /// <summary>重置按钮上的文字；等确认时变成提示语。</summary>
    public string ResetButtonText => IsResetConfirmPending ? LR.RD_ResetConfirm : LR.RD_Reset;

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

        CancelPendingReset();
        IsBusy = true;
        StatusText = LR.RD_Loading;
        RefreshDerived();

        try
        {
            var finished = await RequestRosterAsync(device).ConfigureAwait(true);
            if (!finished.IsSucceeded)
            {
                StatusText = ControlPlaneMessages.DescribeCommand(finished);
                return;
            }

            ApplyRoster(finished);

            // 抽奖且设备声明了条件能力时，再读一次**学生名单**：发放对象是抽奖条件的第二个维度
            // （奖品没有性别/分组，但"发给哪个范围的学生"有）。读不到只是这一档暂时空着，不阻塞整页。
            if (IsLotteryTarget && SupportsDrawConditions && device is not null)
            {
                ApplyRecipients(await RequestRosterAsync(device, ControlRosterReadRequest.Students)
                    .ConfigureAwait(true));
            }
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

    /// <summary>
    ///     下发一次 <c>roster.read</c> 并轮询到终态。
    /// </summary>
    /// <remarks>
    ///     <c>roster_kind</c> 跟着当前的抽取类型走：点名读成员名单，抽奖读奖池（奖项与奖品）。
    ///     两条通道的设备侧形状完全一样，区别只在读的是哪一批文件，因此这里共用一次请求。
    /// </remarks>
    /// <param name="device">目标设备。</param>
    /// <param name="rosterKind">
    ///     要读哪一批：缺省跟着抽取类型走（点名读成员、抽奖读奖池）；抽奖还会额外用 <c>students</c> 读一次
    ///     发放对象名单。
    /// </param>
    private async Task<NodeCommandDto> RequestRosterAsync(DeviceRow device, string? rosterKind = null)
    {
        var command = await _client.SubmitCommandAsync(
            device.GroupId,
            device.NodeId,
            new ControlPlaneCommandRequest
            {
                Capability = ControlCapabilities.RosterRead,
                Kind = "query",
                Payload = JsonSerializer.SerializeToElement(
                    new RosterReadPayload
                    {
                        RosterKind = rosterKind ?? (IsLotteryTarget
                            ? ControlRosterReadRequest.Prizes
                            : ControlRosterReadRequest.Students)
                    },
                    ControlProtocolJson.Options)
            }).ConfigureAwait(true);

        return await _client.PollCommandAsync(device.GroupId, command.CommandId!).ConfigureAwait(true);
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
        CancelPendingReset();
        DrawnMembers.Clear();
        LoadFailure = string.Empty;
        StatusText = LR.RD_Drawing;
        RefreshDerived();

        try
        {
            // target 跟着页面上选的那一类走：点名在教室里点一次名，抽奖在教室里抽一次奖。
            // 名单/奖池与数量按页面上看到的原样下发，设备侧用对应的会话与同一套候选规则执行，
            // 结果留在教室机屏幕上——远程抽取必须让课堂看得见。
            //
            // 抽奖**不带** gender/group：奖品没有这两个维度，设备侧收到就会以 not_applicable 拒绝。
            // 与其发一个注定被拒的字段，不如在这里就不发（页面上那两块也已经隐藏）。
            var payload = JsonSerializer.SerializeToElement(
                new DrawTriggerPayload
                {
                    Target = SelectedDrawKind.Target,
                    ListName = roster.Name,
                    Count = Math.Clamp(SelectedCount, 1, Math.Max(1, roster.MemberCount)),
                    Gender = IsLotteryTarget ? null : ScopeOf(SelectedGender),
                    Group = IsLotteryTarget ? null : ScopeOf(SelectedGroupScope),
                    // 抽奖条件：设备没声明 draw.trigger.conditions 时这里是 null（绝不发出去被静默忽略）。
                    Conditions = IsLotteryTarget ? BuildConditions() : null
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

    /// <summary>
    ///     "重置本轮"：清掉教室机这一轮的抽取进度，让下一轮从头开始。
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>两步确认。</b>第一次点击只把按钮变成"再点一次确认"，不碰设备：
    ///         重置是破坏性动作（课堂进行到一半的进度会被清掉），而手机常常放在讲台上被顺手碰到。
    ///         任何别的动作（换设备、换名单、换抽取类型、重新读取名单）都会取消这个待确认状态，
    ///         否则一个几分钟前的"确认"会突然生效。
    ///     </para>
    ///     <para>
    ///         <b>成功之后要重读名单。</b>设备把这一轮的临时记录清零了，
    ///         "剩余/已抽"这类计数就过期了；不重读的话页面还显示着上一轮的数字。
    ///         重读失败不回滚结论：重置真的成功了，只是数字没刷新，状态行仍然以重置回执为准。
    ///     </para>
    ///     <para>
    ///         <c>target</c> 跟着页面上选的那一类走（点名清学生进度、抽奖清奖品进度），
    ///         <c>list_name</c> 是当前这一份名单/奖池：只清当前这一份，不碰别的名单的进度。
    ///     </para>
    /// </remarks>
    [RelayCommand]
    private async Task ResetRoundAsync()
    {
        var device = SelectedDevice;
        var roster = SelectedRoster;
        if (device is null || roster is null || !device.IsUsable
            || string.IsNullOrWhiteSpace(device.GroupId) || string.IsNullOrWhiteSpace(device.NodeId))
            return;

        if (!IsResetConfirmPending)
        {
            IsResetConfirmPending = true;
            StatusText = LR.RD_ResetConfirm;
            RefreshDerived();
            return;
        }

        IsResetConfirmPending = false;
        IsBusy = true;
        // 重置之后"抽到了谁"就不再成立了：设备那边这一轮已经归零，界面上还挂着上一轮的人就是在说谎。
        DrawnMembers.Clear();
        LoadFailure = string.Empty;
        StatusText = LR.RD_Resetting;
        RefreshDerived();

        try
        {
            var payload = JsonSerializer.SerializeToElement(
                new DrawResetPayload
                {
                    Target = SelectedDrawKind.Target,
                    ListName = roster.Name
                },
                ControlProtocolJson.Options);

            var command = await _client.SubmitCommandAsync(
                device.GroupId,
                device.NodeId,
                new ControlPlaneCommandRequest
                {
                    Capability = ControlCapabilities.DrawReset,
                    Payload = payload
                }).ConfigureAwait(true);

            var finished = await _client.PollCommandAsync(device.GroupId, command.CommandId!).ConfigureAwait(true);

            if (finished.IsSucceeded)
            {
                try
                {
                    var rosterCommand = await RequestRosterAsync(device).ConfigureAwait(true);
                    if (rosterCommand.IsSucceeded)
                        ApplyRoster(rosterCommand);
                    else
                        _logger.LogWarning(
                            "远程重置后重读名单失败：status={Status}，detail={Detail}",
                            rosterCommand.Status,
                            rosterCommand.ResultDiagnostics ?? "(none)");
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, "远程重置后重读名单失败。");
                }
            }

            // 重置回执是最终结论，放在重读之后：重读名单会写状态行（"共 N 人"），
            // 顺序反了的话用户看到的会是名单条数，而不是"重置成功、清掉几条"。
            StatusText = ControlPlaneMessages.DescribeResetResult(finished);
        }
        catch (Exception exception)
        {
            ApplyFailure(exception, "远程重置失败。");
        }
        finally
        {
            IsBusy = false;
            RefreshDerived();
        }
    }

    /// <summary>取消"等待确认"的重置：别的动作一来，那个待确认状态就不再代表用户的意图了。</summary>
    private void CancelPendingReset() => IsResetConfirmPending = false;

    public void Dispose() => Unsubscribe();

    /// <summary>
    ///     拉取"我的组 → 每组的设备"，合并成一份扁平列表。
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>加载态必须在所有退出路径上被清掉</b>（成功、失败、0 台、异常、被更新的请求取代）。
    ///         这条流程曾经在成功路径上只清了 <see cref="IsLoadingDevices" /> 却**没清状态行**，
    ///         于是设备早就读到了，左下角还挂着"正在读取设备…"（三端同错，因为状态在共享 VM 里）。
    ///         现在收成 <see cref="BeginDeviceLoad" /> / <see cref="EndDeviceLoad" /> 一对：
    ///         开始只在一处写、结束只在 <c>finally</c> 里跑，漏清变成不可能。
    ///     </para>
    ///     <para>
    ///         <b>取代（supersede）</b>：每次加载领一个序号，只有"还是最新的那次"才允许提交结果、
    ///         才允许清加载态。旧请求晚回来时既不会把新请求的转圈清掉，也不会把过期的设备列表写上去。
    ///         这里没有取消令牌（请求本身很短，取消只会多一条竞态），取代就是等价的下线路径。
    ///     </para>
    /// </remarks>
    private async Task LoadDevicesAsync()
    {
        var token = BeginDeviceLoad();

        try
        {
            IReadOnlyList<GroupDto> groups;
            try
            {
                groups = await _client.GetGroupsAsync().ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                // 失败**一定**要落进 LoadFailure：只写状态行的话，页面的空态会继续显示
                // "这些组里还没有设备"，用户看到的仍然是"没有组"，而不是"请求失败了"。
                if (token == _deviceLoadToken)
                    ApplyFailure(exception, "读取控制面分组失败。");
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
                                rows.Add(new DeviceRow(group, node, DeviceRow.IsSameNode(OwnNodeId, node.NodeId)));
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

            // 被更新的请求取代了：这份结果已经过期，一条都不要写进界面（新请求会自己写）。
            if (token != _deviceLoadToken)
                return;

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
            RefreshDerived();
        }
        finally
        {
            // 成功、失败、异常、被取代都在这里收口：只有"还是最新那次"才允许收掉加载态。
            EndDeviceLoad(token);
        }
    }

    /// <summary>开始一次设备加载：只在这一处置加载态与进度文案。</summary>
    private int BeginDeviceLoad()
    {
        // 序号即"作废上一轮"：新请求一开始，旧请求的结果与收尾就不再有效。
        var token = ++_deviceLoadToken;

        IsLoadingDevices = true;
        LoadFailure = string.Empty;
        StatusText = LR.RD_LoadingDevices;
        Devices.Clear();
        UnavailableGroups.Clear();
        SelectedDevice = null;
        RefreshDerived();

        return token;
    }

    /// <summary>结束一次设备加载：<b>所有</b>退出路径都经过这里，漏清不可能。</summary>
    private void EndDeviceLoad(int token)
    {
        // 被更新的请求取代：把状态留给新请求，别在这里清掉它的转圈。
        if (token != _deviceLoadToken)
            return;

        IsLoadingDevices = false;

        // 读完之后不留下"正在读取设备"的残影：这一行显示的就是那句进度文案时清空它
        // （台数在上面的"共 N 台设备"里已经有了）；失败时 ApplyFailure 已经写成原因，这里不动它。
        if (string.Equals(StatusText, LR.RD_LoadingDevices, StringComparison.Ordinal))
            StatusText = string.Empty;

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

        // 抽奖档：标签取值域只能从**已读奖池**派生（与设备侧的校验同一批值）。
        if (IsLotteryTarget)
            RefreshPrizeTags();

        if (SelectedRoster is null)
        {
            StatusText = LR.RD_NoRoster;
            return;
        }

        StatusText = SelectedRoster.Truncated
            ? string.Format(LR.RD_Truncated, SelectedRoster.MemberCount)
            : string.Format(LR.RD_Total, SelectedRoster.MemberCount);
    }

    /// <summary>
    ///     用**已读奖池的成员标签**重建标签选项。
    /// </summary>
    /// <remarks>
    ///     与设备侧 <c>ControlDrawConditions.PrizeTagOptions</c> 同一条归一化
    ///     （复用 <see cref="ControlRosterMemberPayload.NormalizeTags" /> 的规则）：两边用同一批值，
    ///     才不会出现"手机里能选、发过去说不存在"。已经在选的标签如果消失了（奖池换了/标签被删了）也要去掉，
    ///     否则会把一个过期的取值继续发出去。
    /// </remarks>
    private void RefreshPrizeTags()
    {
        var available = (SelectedRoster?.Members ?? [])
            .SelectMany(member => member.Tags ?? [])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(tag => new PrizeTagOption(tag))
            .ToList();

        PrizeTags.Clear();
        foreach (var option in available)
            PrizeTags.Add(option);

        foreach (var stale in SelectedPrizeTags
                     .Where(selected => available.All(option => !string.Equals(option.Tag, selected.Tag, StringComparison.Ordinal)))
                     .ToList())
        {
            SelectedPrizeTags.Remove(stale);
        }
    }

    /// <summary>记住每个发放对象名单的成员：选名单时要据此派生性别/分组范围。</summary>
    private readonly Dictionary<string, IReadOnlyList<ControlRosterMemberPayload>> _recipientMembers =
        new(StringComparer.Ordinal);

    /// <summary>把"发放对象名单"的读取结果填进界面；失败不影响整页（只是这一档暂时没有可选名单）。</summary>
    private void ApplyRecipients(NodeCommandDto command)
    {
        RecipientListNames.Clear();
        _recipientMembers.Clear();

        if (command.ResultPayload is not { } payload)
            return;

        ControlRosterReadResponse? response;
        try
        {
            response = payload.Deserialize<ControlRosterReadResponse>(ControlProtocolJson.Options);
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "教室机返回的学生名单无法解析。");
            return;
        }

        foreach (var list in response?.Lists ?? [])
        {
            if (string.IsNullOrWhiteSpace(list.Name))
                continue;

            RecipientListNames.Add(list.Name);
            _recipientMembers[list.Name] = list.Members;
        }

        // 之前选的名单如果在这台机器上不存在了，就当没选（而不是发一个必然被拒的名字）。
        if (SelectedRecipientList is { } selected
            && !RecipientListNames.Contains(selected, StringComparer.Ordinal))
        {
            SelectedRecipientList = null;
        }
    }

    partial void OnSelectedRecipientListChanged(string? value)
    {
        RecipientGenderOptions.Clear();
        RecipientGroupOptions.Clear();
        SelectedRecipientGender = null;
        SelectedRecipientGroup = null;
        OnPropertyChanged(nameof(HasRecipientScope));

        if (value is null || !_recipientMembers.TryGetValue(value, out var members))
            return;

        foreach (var gender in members
                     .Select(member => member.Gender)
                     .Where(static gender => !string.IsNullOrWhiteSpace(gender))
                     .Select(static gender => gender!)
                     .Distinct(StringComparer.Ordinal)
                     .Order(StringComparer.Ordinal))
        {
            RecipientGenderOptions.Add(gender);
        }

        foreach (var group in members
                     .Select(member => member.Group)
                     .Where(static group => !string.IsNullOrWhiteSpace(group))
                     .Select(static group => group!)
                     .Distinct(StringComparer.Ordinal)
                     .Order(StringComparer.Ordinal))
        {
            RecipientGroupOptions.Add(group);
        }
    }

    /// <summary>
    ///     按当前选择拼出 <c>conditions</c>；**设备没声明能力时一律返回 null**。
    /// </summary>
    /// <remarks>
    ///     没有条件时不发这个子对象（不是发一个空对象）：空对象在老服务端看来同样是未知字段，
    ///     而"没写"永远是最兼容的形态。
    /// </remarks>
    private DrawConditionsPayload? BuildConditions()
    {
        if (IsLotteryTarget is false || SupportsDrawConditions is false)
            return null;

        var tags = SelectedPrizeTags
            .Select(option => option.Tag)
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var studentList = string.IsNullOrWhiteSpace(SelectedRecipientList) ? null : SelectedRecipientList;
        var gender = studentList is null ? null : ScopeOf(SelectedRecipientGender);
        var group = studentList is null ? null : ScopeOf(SelectedRecipientGroup);

        // 范围是在选定了发放名单之后才有意义的；没有名单就没有条件。
        if (tags.Length == 0 && studentList is null)
            return null;

        return new DrawConditionsPayload
        {
            Version = 1,
            PrizeTags = tags.Length == 0 ? null : tags,
            StudentList = studentList,
            Gender = gender,
            Group = group
        };
    }

    partial void OnSelectedDeviceChanged(DeviceRow? value)
    {
        CancelPendingReset();
        RefreshDerived();

        if (value is null)
            return;

        // 换设备＝换机器：上一台机器的名单与结果都属于那台机器，留着只会让人对着 A 的名单给 B 发命令。
        if (!_selectingDevice)
        {
            Rosters.Clear();
            SelectedRoster = null;
            // 条件也属于"上一台机器"：标签、发放名单、范围全都不能跟着换设备留下来。
            PrizeTags.Clear();
            SelectedPrizeTags.Clear();
            RecipientListNames.Clear();
            _recipientMembers.Clear();
            SelectedRecipientList = null;
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
        CancelPendingReset();

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

    /// <summary>
    ///     换抽取类型：上一类的名单与结果都不再适用，必须整体清掉。
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         点名名单与奖池是两批完全不同的文件，名字也可能撞车（"高一（1）班"可以既是名单名又是奖池名）。
    ///         留着上一类的选择，用户会以为自己看的还是刚才那份东西，实际发出去的是另一批数据。
    ///     </para>
    ///     <para>
    ///         已经选中可用设备时自动重读一次：切换类型的目的就是"换一批数据看"，
    ///         还要求用户再点一次"读取名单"才算完成切换，是让用户替我们收尾。
    ///     </para>
    /// </remarks>
    partial void OnSelectedDrawKindChanged(RemoteDrawKindOption value)
    {
        CancelPendingReset();
        Rosters.Clear();
        SelectedRoster = null;
        DrawnMembers.Clear();
        StatusText = string.Empty;
        SelectedGender = AnyOption;
        SelectedGroupScope = AnyOption;
        RefreshDerived();

        if (CanLoadRoster)
            _ = LoadRosterAsync();
    }

    partial void OnIsResetConfirmPendingChanged(bool value) => RefreshDerived();

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
        OnPropertyChanged(nameof(DrawnCountText));
        OnPropertyChanged(nameof(IsSelfSelected));
        OnPropertyChanged(nameof(HasUnavailableGroups));
        OnPropertyChanged(nameof(HasEmptyState));
        OnPropertyChanged(nameof(HasLoadFailure));
        OnPropertyChanged(nameof(NeedsSignIn));
        OnPropertyChanged(nameof(DeviceSummary));
        OnPropertyChanged(nameof(DevicesCountText));
        OnPropertyChanged(nameof(CanLoadRoster));
        OnPropertyChanged(nameof(CanDraw));
        OnPropertyChanged(nameof(CanReset));
        OnPropertyChanged(nameof(EmptyStateText));
        OnPropertyChanged(nameof(IsLotteryTarget));
        OnPropertyChanged(nameof(SupportsDrawConditions));
        OnPropertyChanged(nameof(ShowsLotteryConditions));
        OnPropertyChanged(nameof(CanUseRecipientScope));
        OnPropertyChanged(nameof(ShowsLotteryConditionsUnsupported));
        OnPropertyChanged(nameof(ListFieldLabel));
        OnPropertyChanged(nameof(IsScopeSelectorVisible));
        OnPropertyChanged(nameof(IsLotteryScopeHintVisible));
        OnPropertyChanged(nameof(ResetButtonText));
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

        /// <summary>
        ///     抽奖条件集（<c>draw.trigger.conditions</c> v1）。**只在设备声明了该能力时才写**。
        /// </summary>
        /// <remarks>
        ///     这个字段的存在与否是协议兼容的关键：老设备不认识它，会按"没写"静默整池抽。
        ///     因此发送侧的能力闸门不是可选项（见 <see cref="SupportsDrawConditions" />）。
        /// </remarks>
        [JsonPropertyName("conditions")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DrawConditionsPayload? Conditions { get; init; }
    }

    /// <summary><c>conditions</c> 子对象（字段名与设备侧解析的一一对应）。</summary>
    private sealed record DrawConditionsPayload
    {
        [JsonPropertyName("version")] public int Version { get; init; } = 1;

        /// <summary>标签筛选（任一命中）；空＝不筛，因此不写。</summary>
        [JsonPropertyName("prize_tags")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyList<string>? PrizeTags { get; init; }

        [JsonPropertyName("student_list")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? StudentList { get; init; }

        [JsonPropertyName("gender")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Gender { get; init; }

        [JsonPropertyName("group")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Group { get; init; }
    }

    /// <summary><c>draw.reset</c> 的载荷（协议字段名钉在这里）。</summary>
    private sealed record DrawResetPayload
    {
        [JsonPropertyName("target")] public required string Target { get; init; }

        [JsonPropertyName("list_name")] public required string ListName { get; init; }
    }
}
