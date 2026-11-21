using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Services.Config;
using SecRandom.Services.Auth;
using SecRandom.Services.ControlCenter;
using SecRandom.Services.ControlPlane;
using SecRandom.Shared.Models.ControlCenter;
using LR = SecRandom.Langs.Mobile.Resources;

namespace SecRandom.ViewModels.Mobile;

/// <summary>
///     手机 / 平板 / 桌面共用的「远程抽取」页：登录账号 → 地点 → 年级 → 班级 → 抽取 → 看结果。
/// </summary>
/// <remarks>
///     <para>
///         手机和平板**没有**被控节点身份，只能当控制端；桌面既可以是被控节点，也可以当控制端。
///         无论哪种，控制台入口都只有一条通道：**登录 SECTL 账号**，用 JWT 调 v4 级联 API。
///         配对码（地点 ID + 配对码）只用于把桌面接入成被控节点，不在这条控制链上出现。
///     </para>
///     <para>
///         抽取仍然发生在教室机：手机只发 <c>draw.trigger</c> 并显示设备回执里的
///         <c>payload.drawn</c>。抽取类型（点名/抽奖）与名单也在这里选：名单名称通过
///         <c>roster.list</c> 向占班设备现取（只回名称，operator 可读），成员明细仍留在教室机。
///         设备没声明这条能力时名单下拉为空，抽取回落到设备自己的默认名单。
///     </para>
///     <para>
///         每一种"做不了"都必须有话说：未登录、账号没有地点、地点没有年级、班级没有设备、
///         设备离线、设备未上课（draw_locked）、没有权限、限流、超时。空白页会让老师以为
///         是自己不会用。
///     </para>
/// </remarks>
public sealed partial class MobileRemoteDrawViewModel : ViewModelBase, IDisposable
{
    private readonly IControlCenterClient _client;
    private readonly SectlAuthService _auth;
    private readonly ILogger<MobileRemoteDrawViewModel> _logger;

    /// <summary>级联加载序号：新一次加载一开始就作废旧请求（取代 = 这条链路上的取消）。</summary>
    private int _loadToken;

    /// <summary>名单名称的加载序号：换班/刷新时作废旧结果，避免旧班的名单落到新班的下拉框里。</summary>
    private int _rosterToken;

    private IReadOnlyList<string> _rollCallNames = [];
    private IReadOnlyList<string> _lotteryNames = [];
    private string? _defaultRollCall;
    private string? _defaultLottery;

    private bool _subscribed;
    private bool _disposed;

    /// <summary>重置需要二次确认：第一次点只把按钮换成"再点一次"。</summary>
    [ObservableProperty] private bool _isResetConfirmPending;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string? _loadFailure;
    [ObservableProperty] private string? _statusDetail;

    [ObservableProperty] private bool _isSignedIn;
    [ObservableProperty] private string? _accountName;

    [ObservableProperty] private RemoteSiteOption? _selectedSite;
    [ObservableProperty] private RemoteGradeOption? _selectedGrade;
    [ObservableProperty] private RemoteClassOption? _selectedClass;
    [ObservableProperty] private DeviceRow? _selectedDevice;
    [ObservableProperty] private int _selectedCount = 1;
    [ObservableProperty] private bool _drawLocked = true;

    /// <summary>抽取类型：点名（roll_call）或抽奖（lottery），与设备 <c>draw.trigger.target</c> 同值。</summary>
    [ObservableProperty] private DrawKindOption _selectedDrawKind = new(DrawKindOption.RollCall, LR.RD_KindRollCall);

    /// <summary>选中的点名名单 / 奖池名；空表示用设备当前的默认名单。</summary>
    [ObservableProperty] private string? _selectedRoster;

    public MobileRemoteDrawViewModel(
        MainConfigHandler configHandler,
        IControlCenterClient client,
        SectlAuthService auth,
        ILogger<MobileRemoteDrawViewModel> logger)
        : base(configHandler)
    {
        _client = client;
        _auth = auth;
        _logger = logger;
        IsSignedIn = auth.IsSignedIn;
        AccountName = ResolveAccountName();
        DrawKindOptions = [new DrawKindOption(DrawKindOption.RollCall, LR.RD_KindRollCall),
            new DrawKindOption(DrawKindOption.Lottery, LR.RD_KindLottery)];
        Subscribe();
    }

    /// <summary>账号可管理的地点。</summary>
    public ObservableCollection<RemoteSiteOption> Sites { get; } = [];

    /// <summary>当前地点下的年级。</summary>
    public ObservableCollection<RemoteGradeOption> Grades { get; } = [];

    /// <summary>当前年级下的班级。</summary>
    public ObservableCollection<RemoteClassOption> Classes { get; } = [];

    /// <summary>当前班级的 SecRandom 设备（含占班标记）。</summary>
    public ObservableCollection<DeviceRow> Devices { get; } = [];

    /// <summary>抽取类型（点名/抽奖）。</summary>
    public IReadOnlyList<DrawKindOption> DrawKindOptions { get; }

    /// <summary>当前抽取类型下可选的名单/奖池名称；设备不支持时为 0 项。</summary>
    public ObservableCollection<string> Rosters { get; } = [];

    /// <summary>最近一次抽取结果（只来自设备回执的 <c>payload.drawn</c>）。</summary>
    public ObservableCollection<ControlDrawnMember> DrawnMembers { get; } = [];

    /// <summary>状态机是否停在"必须先登录"。</summary>
    public bool NeedsSignIn => !IsSignedIn;

    public bool HasSites => Sites.Count > 0;
    public bool HasGrades => Grades.Count > 0;
    public bool HasClasses => Classes.Count > 0;
    public bool HasDevices => Devices.Count > 0;
    public bool HasRosters => Rosters.Count > 0;
    public bool HasResult => DrawnMembers.Count > 0;
    public bool HasLoadFailure => !string.IsNullOrWhiteSpace(LoadFailure);
    public bool HasStatusDetail => !string.IsNullOrWhiteSpace(StatusDetail);

    public string DrawnCountText => DrawnMembers.Count == 1
        ? string.Format(LR.RD_DrawnCount, DrawnMembers.Count)
        : string.Format(LR.RD_DrawnPrizeCount, DrawnMembers.Count);

    public string ResultPlaceholderTitle => LR.RD_NoResult;

    public string ResultPlaceholderHint => LR.RD_NoResult_D;

    /// <summary>设备选择区是否有选择（设备本身不是抽取目标，目标由班级占班决定）。</summary>
    public bool HasSelection => SelectedClass is not null;

    /// <summary>班级当前"占班"的 SecRandom 设备 ID；没有占班时为空。</summary>
    public string? TargetDeviceId { get; private set; }

    public string? TargetDeviceName { get; private set; }

    /// <summary>
    ///     占班设备此刻是否在线。命令只会发给它，离线就一定有去无回：
    ///     所以"设备在、但离线"必须算成不能抽取，不能只凭 <see cref="TargetDeviceId" /> 就让按钮亮着。
    /// </summary>
    public bool IsTargetDeviceOnline => Devices.Any(device => device.IsTarget && device.Online);

    /// <summary>选中的班级是否已经具备抽取条件；不满足时 <see cref="DrawDisabledReason" /> 会给出原因。</summary>
    public bool CanDraw =>
        IsSignedIn &&
        !IsBusy &&
        SelectedClass is not null &&
        !string.IsNullOrEmpty(TargetDeviceId) &&
        IsTargetDeviceOnline &&
        !DrawLocked;

    public string? DrawDisabledReason
    {
        get
        {
            if (!IsSignedIn) return LR.RD_SignIn;
            if (SelectedClass is null) return LR.RD_NeedClass;
            if (string.IsNullOrEmpty(TargetDeviceId)) return LR.RD_NoTargetDevice;
            if (!IsTargetDeviceOnline) return LR.RD_TargetOffline;
            if (DrawLocked) return LR.RD_DrawLocked;
            return null;
        }
    }

    public bool HasDrawDisabledReason => DrawDisabledReason is not null;

    /// <summary>空态提示：登录、无地点、无年级、无班级各不相同。</summary>
    public string EmptyStateText
    {
        get
        {
            if (NeedsSignIn) return LR.RD_SignIn;
            if (!HasSites) return LR.RD_NoSites;
            if (SelectedSite is null) return LR.RD_SelectSite;
            if (!HasGrades) return LR.RD_NoGrades;
            if (SelectedGrade is null) return LR.RD_SelectGrade;
            if (!HasClasses) return LR.RD_NoClasses;
            if (SelectedClass is null) return LR.RD_SelectClass;
            return LR.RD_NoDevicesHint;
        }
    }

    public bool HasEmptyState => !HasResult && !IsBusy && !HasLoadFailure;

    public bool CanReset => IsSignedIn && SelectedClass is not null && HasResult && !IsBusy;

    public string ResetButtonText => IsResetConfirmPending ? LR.RD_ResetConfirm : LR.RD_Reset;

    /// <summary>页面每次显示时调用：登录态和"现在"的地点列表都要重新取。</summary>
    public async Task InitializeAsync()
    {
        Subscribe();
        IsSignedIn = _auth.IsSignedIn;
        AccountName = ResolveAccountName();
        if (!IsSignedIn)
        {
            ClearGradeSelection();
            return;
        }

        await LoadSitesAsync().ConfigureAwait(true);
    }

    /// <summary>刷新：从当前选择的最深一级重新加载；没有选择就从地点开始。</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (!IsSignedIn)
        {
            IsSignedIn = _auth.IsSignedIn;
            AccountName = ResolveAccountName();
            if (!IsSignedIn) return;
        }

        if (SelectedClass is not null)
        {
            await LoadClassDetailAsync(SelectedClass.Id).ConfigureAwait(true);
            return;
        }

        if (SelectedGrade is not null)
        {
            await LoadClassesAsync(SelectedGrade.Id).ConfigureAwait(true);
            return;
        }

        if (SelectedSite is not null)
        {
            await LoadGradesAsync(SelectedSite.PublicId).ConfigureAwait(true);
            return;
        }

        await LoadSitesAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task SignInAsync()
    {
        if (IsSignedIn) return;

        IsBusy = true;
        LoadFailure = null;
        try
        {
            await _auth.SignInAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SECTL sign-in from remote draw page failed");
            LoadFailure = LR.RD_SignInFailed;
        }
        finally
        {
            IsBusy = false;
            IsSignedIn = _auth.IsSignedIn;
            AccountName = ResolveAccountName();
        }

        if (IsSignedIn) await LoadSitesAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task DrawAsync()
    {
        if (!CanDraw || SelectedClass is null) return;

        IsBusy = true;
        LoadFailure = null;
        StatusDetail = null;
        StatusText = LR.RD_Drawing;
        ClearResult();
        try
        {
            var result = await _client
                .SendCommandAsync(
                    SelectedClass.Id,
                    ControlCenterClient.Platform,
                    "draw.trigger",
                    BuildDrawArgs())
                .ConfigureAwait(true);
            ApplyCommandResult(result);
        }
        catch (ControlPlaneException ex)
        {
            _logger.LogWarning(ex, "remote draw command failed: {Code}", ex.Code);
            LoadFailure = DescribeFailure(ex);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "remote draw command failed");
            LoadFailure = LR.RD_Failed;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanDraw));
            OnPropertyChanged(nameof(HasDrawDisabledReason));
            OnPropertyChanged(nameof(DrawDisabledReason));
            OnPropertyChanged(nameof(CanReset));
            OnPropertyChanged(nameof(HasEmptyState));
        }
    }

    [RelayCommand]
    private async Task ResetRoundAsync()
    {
        if (!CanReset || SelectedClass is null) return;
        if (!IsResetConfirmPending)
        {
            IsResetConfirmPending = true;
            OnPropertyChanged(nameof(ResetButtonText));
            return;
        }

        IsResetConfirmPending = false;
        OnPropertyChanged(nameof(ResetButtonText));
        IsBusy = true;
        LoadFailure = null;
        StatusDetail = null;
        StatusText = LR.RD_Resetting;
        try
        {
            await _client
                .SendCommandAsync(
                    SelectedClass.Id,
                    ControlCenterClient.Platform,
                    "draw.reset",
                    BuildDrawTargetArgs())
                .ConfigureAwait(true);
            ClearResult();
            StatusText = LR.RD_ResetDone;
        }
        catch (ControlPlaneException ex)
        {
            _logger.LogWarning(ex, "remote draw reset failed: {Code}", ex.Code);
            LoadFailure = DescribeFailure(ex);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "remote draw reset failed");
            LoadFailure = LR.RD_Failed;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanDraw));
            OnPropertyChanged(nameof(CanReset));
            OnPropertyChanged(nameof(HasEmptyState));
        }
    }

    [RelayCommand]
    private void CancelPendingReset()
    {
        if (!IsResetConfirmPending) return;
        IsResetConfirmPending = false;
        OnPropertyChanged(nameof(ResetButtonText));
    }

    partial void OnSelectedSiteChanged(RemoteSiteOption? value)
    {
        if (_disposed) return;
        if (value is null)
        {
            Grades.Clear();
            ClearGradeSelection();
            OnCascadeChanged();
            return;
        }

        _ = LoadGradesAsync(value.PublicId);
    }

    partial void OnSelectedGradeChanged(RemoteGradeOption? value)
    {
        if (_disposed) return;
        if (value is null)
        {
            Classes.Clear();
            ClearClassSelection();
            OnCascadeChanged();
            return;
        }

        _ = LoadClassesAsync(value.Id);
    }

    partial void OnSelectedClassChanged(RemoteClassOption? value)
    {
        if (_disposed) return;
        if (value is null)
        {
            Devices.Clear();
            ClearTarget();
            OnCascadeChanged();
            return;
        }

        _ = LoadClassDetailAsync(value.Id);
    }

    partial void OnSelectedCountChanged(int value)
    {
        if (value < 1) SelectedCount = 1;
        if (value > 50) SelectedCount = 50;
    }

    partial void OnSelectedDrawKindChanged(DrawKindOption value)
    {
        if (_disposed) return;
        ApplyRosterOptions();
    }

    private async Task LoadSitesAsync()
    {
        var token = ++_loadToken;
        IsLoading = true;
        LoadFailure = null;
        StatusDetail = null;
        try
        {
            var me = await _client.GetMeAsync().ConfigureAwait(true);
            if (token != _loadToken) return;

            var previousSite = SelectedSite?.PublicId;
            Sites.Clear();
            foreach (var site in me.Sites)
                Sites.Add(new RemoteSiteOption(site.PublicId, site.Name, site.Role));
            OnPropertyChanged(nameof(HasSites));
            OnPropertyChanged(nameof(EmptyStateText));
            OnPropertyChanged(nameof(HasEmptyState));

            // 唯一地点自动选中：少一次点击，也不至于把"地点"当成必答题。
            SelectedSite = Sites.Count == 1
                ? Sites[0]
                : Sites.FirstOrDefault(s => string.Equals(s.PublicId, previousSite, StringComparison.Ordinal));
            if (Sites.Count == 0)
            {
                ClearGradeSelection();
                OnCascadeChanged();
            }
        }
        catch (ControlPlaneException ex)
        {
            if (token != _loadToken) return;
            _logger.LogWarning(ex, "loading control sites failed: {Code}", ex.Code);
            LoadFailure = DescribeFailure(ex);
        }
        catch (Exception ex)
        {
            if (token != _loadToken) return;
            _logger.LogWarning(ex, "loading control sites failed");
            LoadFailure = LR.RD_Failed;
        }
        finally
        {
            if (token == _loadToken) IsLoading = false;
        }
    }

    private async Task LoadGradesAsync(string sitePublicId)
    {
        var token = ++_loadToken;
        IsLoading = true;
        LoadFailure = null;
        // 只清"年级及以下"：换地点当然要清年级；而换年级（LoadClassesAsync）绝不能回头清这里。
        ClearGradeSelection();
        try
        {
            var grades = await _client.GetGradesAsync(sitePublicId).ConfigureAwait(true);
            if (token != _loadToken) return;

            foreach (var grade in grades)
                Grades.Add(new RemoteGradeOption(grade.Id, grade.Name, grade.ClassCount));
            OnPropertyChanged(nameof(HasGrades));
            OnPropertyChanged(nameof(EmptyStateText));
            OnPropertyChanged(nameof(HasEmptyState));

            SelectedGrade = Grades.Count == 1 ? Grades[0] : null;
            if (Grades.Count == 0) OnCascadeChanged();
        }
        catch (ControlPlaneException ex)
        {
            if (token != _loadToken) return;
            _logger.LogWarning(ex, "loading grades failed: {Code}", ex.Code);
            LoadFailure = DescribeFailure(ex);
        }
        catch (Exception ex)
        {
            if (token != _loadToken) return;
            _logger.LogWarning(ex, "loading grades failed");
            LoadFailure = LR.RD_Failed;
        }
        finally
        {
            if (token == _loadToken) IsLoading = false;
        }
    }

    private async Task LoadClassesAsync(string gradeId)
    {
        var token = ++_loadToken;
        IsLoading = true;
        LoadFailure = null;
        // 换年级只清班级及以下：年级列表和已选年级必须留着，否则年级下拉会在选完之后消失。
        SelectedClass = null;
        Classes.Clear();
        try
        {
            var classes = await _client
                .GetClassesAsync(SelectedSite?.PublicId ?? string.Empty, gradeId)
                .ConfigureAwait(true);
            if (token != _loadToken) return;

            foreach (var item in classes)
                Classes.Add(new RemoteClassOption(item.Id, item.Name, item.Floor, item.DeviceCount));
            OnPropertyChanged(nameof(HasClasses));
            OnPropertyChanged(nameof(EmptyStateText));
            OnPropertyChanged(nameof(HasEmptyState));

            SelectedClass = Classes.Count == 1 ? Classes[0] : null;
            if (Classes.Count == 0) OnCascadeChanged();
        }
        catch (ControlPlaneException ex)
        {
            if (token != _loadToken) return;
            _logger.LogWarning(ex, "loading classes failed: {Code}", ex.Code);
            LoadFailure = DescribeFailure(ex);
        }
        catch (Exception ex)
        {
            if (token != _loadToken) return;
            _logger.LogWarning(ex, "loading classes failed");
            LoadFailure = LR.RD_Failed;
        }
        finally
        {
            if (token == _loadToken) IsLoading = false;
        }
    }

    private async Task LoadClassDetailAsync(string classId)
    {
        var token = ++_loadToken;
        IsLoading = true;
        LoadFailure = null;
        try
        {
            var detail = await _client.GetClassAsync(classId).ConfigureAwait(true);
            if (token != _loadToken) return;

            DrawLocked = detail.DrawLocked;
            TargetDeviceId = detail.Occupancy.TryGetValue(ControlCenterClient.Platform, out var occupied)
                ? occupied
                : null;

            Devices.Clear();
            foreach (var device in detail.Devices)
            {
                var isTarget = string.Equals(device.Id, TargetDeviceId, StringComparison.Ordinal);
                if (isTarget) TargetDeviceName = device.Name;
                Devices.Add(new DeviceRow(
                    device.Id,
                    device.Name,
                    device.Online,
                    isTarget,
                    device.Platforms));
            }

            if (string.IsNullOrEmpty(TargetDeviceId))
                TargetDeviceName = null;

            SelectedDevice = Devices.FirstOrDefault(d => d.IsTarget) ?? Devices.FirstOrDefault();
            StatusDetail = detail.Class.GradeName is { Length: > 0 } grade
                ? string.Format(LR.RD_ClassDetail, detail.Class.Name, grade)
                : detail.Class.Name;
            OnPropertyChanged(nameof(TargetDeviceName));
            OnPropertyChanged(nameof(HasDevices));
            OnPropertyChanged(nameof(EmptyStateText));
            OnPropertyChanged(nameof(HasEmptyState));

            // 名单名称是“设备上有什么”，与班级详情同一次选择一起取；失败只影响下拉框，不影响抽取。
            _ = LoadRosterOptionsAsync(classId);
        }
        catch (ControlPlaneException ex)
        {
            if (token != _loadToken) return;
            _logger.LogWarning(ex, "loading class detail failed: {Code}", ex.Code);
            LoadFailure = DescribeFailure(ex);
        }
        catch (Exception ex)
        {
            if (token != _loadToken) return;
            _logger.LogWarning(ex, "loading class detail failed");
            LoadFailure = LR.RD_Failed;
        }
        finally
        {
            if (token == _loadToken)
            {
                IsLoading = false;
                OnCascadeChanged();
            }
        }
    }

    private async Task LoadRosterOptionsAsync(string classId)
    {
        var token = ++_rosterToken;
        try
        {
            var rosters = await _client.ListRostersAsync(classId).ConfigureAwait(true);
            if (token != _rosterToken
                || !string.Equals(SelectedClass?.Id, classId, StringComparison.Ordinal))
                return;

            _rollCallNames = rosters.RollCall;
            _lotteryNames = rosters.Lottery;
            _defaultRollCall = rosters.DefaultRollCall;
            _defaultLottery = rosters.DefaultLottery;
            ApplyRosterOptions();
        }
        catch (ControlPlaneException ex)
        {
            // 设备不支持 roster.list（老版本）或离线：名单下拉留空，抽取仍按设备默认名单进行。
            if (token != _rosterToken) return;
            _logger.LogInformation(
                ex,
                "listing rosters for class {ClassId} failed: {Code}",
                classId,
                ex.Code);
            ResetRosters();
        }
        catch (Exception ex)
        {
            if (token != _rosterToken) return;
            _logger.LogWarning(ex, "listing rosters for class {ClassId} failed", classId);
            ResetRosters();
        }
    }

    /// <summary>按当前抽取类型刷新名单下拉框，并尽量保留/选中一个有效项。</summary>
    private void ApplyRosterOptions()
    {
        var isLottery = SelectedDrawKind.Key == DrawKindOption.Lottery;
        var names = isLottery ? _lotteryNames : _rollCallNames;
        var preferred = isLottery ? _defaultLottery : _defaultRollCall;

        Rosters.Clear();
        foreach (var name in names)
            Rosters.Add(name);
        OnPropertyChanged(nameof(HasRosters));

        if (SelectedRoster is { Length: > 0 } current
            && names.Contains(current, StringComparer.Ordinal))
            return;

        SelectedRoster = preferred is { Length: > 0 } && names.Contains(preferred, StringComparer.Ordinal)
            ? preferred
            : names.Count > 0 ? names[0] : null;
    }

    private void ResetRosters()
    {
        _rosterToken++;
        _rollCallNames = [];
        _lotteryNames = [];
        _defaultRollCall = null;
        _defaultLottery = null;
        Rosters.Clear();
        SelectedRoster = null;
        OnPropertyChanged(nameof(HasRosters));
    }

    private Dictionary<string, object?> BuildDrawArgs()
    {
        var args = BuildDrawTargetArgs();
        args["count"] = SelectedCount;
        return args;
    }

    /// <summary>点名/抽奖与名单：重置也按同一目标清，避免清错名单的进度。</summary>
    private Dictionary<string, object?> BuildDrawTargetArgs()
    {
        var args = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["target"] = SelectedDrawKind.Key
        };
        if (!string.IsNullOrWhiteSpace(SelectedRoster))
            args["list_name"] = SelectedRoster;
        return args;
    }

    private void ApplyCommandResult(ControlCenterCommandResult result)
    {
        ClearResult();
        if (!result.Ok)
        {
            LoadFailure = LR.RD_Failed;
            StatusText = string.Empty;
            return;
        }

        if (result.Payload is { ValueKind: JsonValueKind.Object } payload)
        {
            if (payload.TryGetProperty("drawn", out var drawn) && drawn.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in drawn.EnumerateArray())
                {
                    var id = ReadString(item, "id");
                    var name = ReadString(item, "name");
                    if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(name)) continue;
                    DrawnMembers.Add(new ControlDrawnMember(id, name));
                }
            }

            if (payload.TryGetProperty("count", out var count) && count.TryGetInt32(out var expected) &&
                expected > 0 && DrawnMembers.Count == 0)
            {
                // 设备回执说抽了人但没给明细：宁可明说"回执不可读"，也不假装抽到了。
                LoadFailure = LR.RD_ResultUnreadable;
            }
        }

        StatusText = HasResult ? string.Format(LR.RD_Total, DrawnMembers.Count) : LR.RD_NoCandidate;
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(DrawnCountText));
        OnPropertyChanged(nameof(CanReset));
        OnPropertyChanged(nameof(HasEmptyState));
    }

    private void ClearResult()
    {
        DrawnMembers.Clear();
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(DrawnCountText));
        OnPropertyChanged(nameof(CanReset));
        OnPropertyChanged(nameof(HasEmptyState));
    }

    private void ClearClassSelection()
    {
        Devices.Clear();
        ClearTarget();
        ResetRosters();
        SelectedClass = null;
        Classes.Clear();
        OnCascadeChanged();
    }

    /// <summary>
    ///     清掉"年级及以下"：换地点时用。**不能**清 <see cref="Grades" /> 以外的同级状态，
    ///     也不能反过来在换年级/换班时清年级列表——否则选完年级列表就被清空、年级下拉消失。
    /// </summary>
    private void ClearGradeSelection()
    {
        ClearClassSelection();
        SelectedGrade = null;
        Grades.Clear();
        OnPropertyChanged(nameof(HasGrades));
        OnCascadeChanged();
    }

    private void ClearTarget()
    {
        TargetDeviceId = null;
        TargetDeviceName = null;
        DrawLocked = true;
        SelectedDevice = null;
        OnPropertyChanged(nameof(TargetDeviceName));
        OnPropertyChanged(nameof(HasDevices));
    }

    private void OnCascadeChanged()
    {
        OnPropertyChanged(nameof(CanDraw));
        OnPropertyChanged(nameof(DrawDisabledReason));
        OnPropertyChanged(nameof(HasDrawDisabledReason));
        OnPropertyChanged(nameof(HasEmptyState));
        OnPropertyChanged(nameof(EmptyStateText));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanReset));
        OnPropertyChanged(nameof(HasDevices));
    }

    private void Subscribe()
    {
        if (_subscribed) return;
        _auth.StateChanged += OnAuthStateChanged;
        _subscribed = true;
    }

    private void OnAuthStateChanged(object? sender, EventArgs e)
    {
        // StateChanged 可能来自任意线程（刷新令牌、后台 introspection），UI 属性只在 UI 线程写。
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            ApplyAuthState();
            return;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(ApplyAuthState);
    }

    private void ApplyAuthState()
    {
        var signedIn = _auth.IsSignedIn;
        if (signedIn == IsSignedIn)
        {
            AccountName = ResolveAccountName();
            return;
        }

        IsSignedIn = signedIn;
        AccountName = ResolveAccountName();
        if (!signedIn)
        {
            ClearGradeSelection();
            return;
        }

        _ = LoadSitesAsync();
    }

    private string? ResolveAccountName()
    {
        var user = _auth.User;
        return string.IsNullOrWhiteSpace(user?.ResolvedUserName)
            ? user?.ResolvedUserId
            : user.ResolvedUserName;
    }

    private string DescribeFailure(ControlPlaneException ex)
    {
        StatusDetail = string.IsNullOrWhiteSpace(ex.Code) ? null : ex.Code;
        return ex.Code switch
        {
            "not_signed_in" or "unauthorized" or "not_authed" => LR.RD_SignIn,
            "forbidden" or "role_required" or "capability_unsupported" => LR.RD_Denied,
            "site_not_found" or "class_not_found" or "grade_not_found" => LR.RD_NotFound,
            "device_offline" or "offline" => LR.RD_Offline,
            "timeout" or "expired" or "deadline_exceeded" => LR.RD_CommandTimeout,
            "too_many_requests" or "rate_limited" => LR.RD_RateLimited,
            "network_error" => LR.RD_Network,
            "draw_locked" => LR.RD_DrawLocked,
            // RD_FailureWithCode 是「{0}（错误码：{1}）」两个占位符：只传一个 code 会抛 FormatException，
            // 把一个普通的失败回执变成崩溃报告（v3.1.5 现场就是这么崩的）。
            _ => string.IsNullOrWhiteSpace(ex.Code)
                ? LR.RD_Failed
                : string.Format(LR.RD_FailureWithCode, LR.RD_Failed, ex.Code)
        };
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null
            }
            : null;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_subscribed)
        {
            _auth.StateChanged -= OnAuthStateChanged;
            _subscribed = false;
        }

        _loadToken++;
        _rosterToken++;
    }
}

/// <summary>抽取类型：<c>draw.trigger.target</c> 的取值。</summary>
public sealed record DrawKindOption(string Key, string Label)
{
    public const string RollCall = "roll_call";
    public const string Lottery = "lottery";
}

/// <summary>可选择的地点。</summary>
public sealed record RemoteSiteOption(string PublicId, string Name, string Role)
{
    public string DisplayLabel => string.IsNullOrWhiteSpace(Role) ? Name : $"{Name} · {Role}";
}

/// <summary>可选择年级。</summary>
public sealed record RemoteGradeOption(string Id, string Name, int ClassCount)
{
    public string DisplayLabel => ClassCount > 0 ? $"{Name} ({ClassCount})" : Name;
}

/// <summary>可选择班级。</summary>
public sealed record RemoteClassOption(string Id, string Name, string? Floor, int DeviceCount)
{
    public string DisplayLabel => string.IsNullOrWhiteSpace(Floor) ? Name : $"{Name} · {Floor}";
}

/// <summary>班级里的一台 SecRandom 设备。</summary>
/// <param name="IsTarget">这台设备当前"占班"：抽取命令只会发给它。</param>
public sealed record DeviceRow(
    string Id,
    string Name,
    bool Online,
    bool IsTarget,
    IReadOnlyList<string> Platforms)
{
    public string DeviceLabel => Name;

    public string StatusText => Online ? LR.RD_Online : LR.RD_Offline;

    public bool IsUsable => Online && IsTarget;

    public string? UnavailableReason => IsTarget && !Online
        ? LR.RD_TargetOffline
        : !IsTarget ? LR.RD_NotTargetDevice : null;

    public bool HasUnavailableReason => !string.IsNullOrWhiteSpace(UnavailableReason);
}

/// <summary>结果列表里的一名成员（回执里可能有 id 也可能只有名字）。</summary>
public sealed record ControlDrawnMember(string? Id, string? Name)
{
    public string DisplayLabel =>
        !string.IsNullOrWhiteSpace(Name) ? Name! : Id ?? string.Empty;

    public string? SecondaryLabel =>
        !string.IsNullOrWhiteSpace(Id) && !string.Equals(Id, Name, StringComparison.Ordinal) ? Id : null;
}