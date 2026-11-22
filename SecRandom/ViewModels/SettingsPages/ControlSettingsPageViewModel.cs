using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Services.Auth;
using SecRandom.Services.ControlCenter;
using SecRandom.Services.ControlNode;
using SecRandom.Services.ControlPlane;
using SecRandom.Services.Desktop;
using SecRandom.Shared.Models.ControlNode;
using SecRandom.Shared.Models.ControlNode.V4;
using CC = SecRandom.Shared.Models.ControlCenter;
using LR = SecRandom.Langs.SettingsPages.General.Control.Resources;

namespace SecRandom.ViewModels.SettingsPages;

/// <summary>
///     集控设置页：本机开关、集控接入、班级绑定与连接状态。
/// </summary>
/// <remarks>
///     <para>
///         这里**不经过 <c>settings.json</c>**：本机开关、已应用的期望状态都在
///         <c>data/config/control/node-state.json</c>；设备凭据在独立的加密记录里。
///         理由是安全边界——一次设置导入或备份恢复不该能把"这台机器允许被远控"悄悄打开，
///         也不该能把设备凭据搬到另一台机器上。
///     </para>
///     <para>
///         v4 接入是**双因子**：地点 ID 说明"接进哪个学校"，配对码说明"谁允许你接"。
///         两者缺一不可，所以页面上分成接入卡与班级卡：没有接入就不显示年级/班级，
///         避免用户在未接入时以为选个班级就能被控制。
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
    private readonly IControlCenterClient? _controlCenterClient;
    private readonly SectlAuthService? _auth;
    private bool _accountSubscribed;
    private int _accountWork;
    private bool _suppressPersist;
    private bool _suppressPairingFormat;
    private bool _suppressAutoParse;

    public ControlSettingsPageViewModel(
        MainConfigHandler configHandler,
        IControlNodeStateStore stateStore,
        ControlNodeClient client,
        ControlNodeClientOptions options,
        IControlPlaneEndpointStore controlPlaneEndpointStore,
        IExternalLauncher externalLauncher,
        ILogger<ControlSettingsPageViewModel> logger,
        INodeEnrollmentStore? enrollmentStore = null,
        NodeEnrollmentClient? enrollmentClient = null,
        IControlCenterClient? controlCenterClient = null,
        SectlAuthService? auth = null) : base(configHandler)
    {
        _stateStore = stateStore;
        _client = client;
        _options = options;
        _controlPlaneEndpointStore = controlPlaneEndpointStore;
        _externalLauncher = externalLauncher;
        _logger = logger;
        _enrollmentStore = enrollmentStore;
        _enrollmentClient = enrollmentClient;
        _controlCenterClient = controlCenterClient;
        _auth = auth;

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

        if (_auth is not null)
        {
            _auth.StateChanged += OnAuthStateChanged;
            _accountSubscribed = true;
        }

        RefreshAccountState();
        if (IsAccountSignedIn)
            _ = LoadAccountSitesAsync();

        // 已接入就直接把年级/班级读回来：用户打开设置页时应当看到当前绑定的班级，
        // 而不是先点一下"刷新"才出现。失败只提示，不影响其它卡片。
        if (IsEnrolled)
            _ = LoadTreeAsync();
    }

    /// <summary>本机是否允许被集控。**这是设备自己的闸，服务端只读它。**</summary>
    [ObservableProperty] private bool _remoteControlEnabled;

    /// <summary>
    ///     本机主界面是否显示"远程抽取"页。**默认关闭**，打开后侧栏立刻出现，关掉立刻消失（不需要重启）。
    /// </summary>
    [ObservableProperty] private bool _remoteDrawPageEnabled;

    /// <summary>
    ///     控制台里显示的名称。**由用户在这台机器上填写**；留空则上报 <see cref="HostName" />。
    /// </summary>
    [ObservableProperty] private string _displayName = string.Empty;

    /// <summary>留空时上报的回落值（主机名）。同时作为输入框水印，省得用户去猜。</summary>
    public string HostName { get; }

    /// <summary>输入框的上限，直接取协议上限。</summary>
    public int DisplayNameMaxLength => ControlNodeDisplayName.MaxLength;

    [ObservableProperty] private string _statusText = string.Empty;

    [ObservableProperty] private string _statusDetail = string.Empty;

    /// <summary>抽取是否被控制台的期望状态锁定。</summary>
    [ObservableProperty] private bool _drawLocked;

    /// <summary>集控平台按钮文案；端点文件被手工改成自建地址时仍要如实显示第三方。</summary>
    [ObservableProperty] private string _openControlPlaneLabel = string.Empty;

    // ── 集控接入 ────────────────────────────────────────────────────────────────

    /// <summary>是否已经持有本机设备凭据。</summary>
    [ObservableProperty] private bool _isEnrolled;

    /// <summary>
    ///     未接入时显示的接入方式。只属于当前设置页，不落盘；默认使用地点 ID + 配对码。
    /// </summary>
    [ObservableProperty] private bool _isAccountEnrollmentMode;

    /// <summary>地点 ID 输入框（形如 <c>SC-K7Q2M9</c>）。</summary>
    [ObservableProperty] private string _siteIdInput = string.Empty;

    /// <summary>配对码输入框（形如 <c>ABCD-7KQ2</c>）。</summary>
    [ObservableProperty] private string _pairingCodeInput = string.Empty;

    /// <summary>接入状态一句话（未接入 / 已接入到哪个地点 / 记录读不出来）。</summary>
    [ObservableProperty] private string _enrollmentStatusText = string.Empty;

    /// <summary>接入或班级保存的提示；成功或未操作时为空。</summary>
    [ObservableProperty] private string _enrollmentMessage = string.Empty;

    /// <summary>接入记录存在却读不出来（被改过、换了机器）。</summary>
    [ObservableProperty] private bool _isEnrollmentUnreadable;

    /// <summary>正在接入（按钮转圈/禁用，避免连点换出多份凭据）。</summary>
    [ObservableProperty] private bool _isEnrolling;

    /// <summary>已接入地点的一行"名称（公开 ID）"。</summary>
    [ObservableProperty] private string _enrolledSiteText = string.Empty;

    /// <summary>本机在集控里的名称。</summary>
    [ObservableProperty] private string _enrolledDeviceText = string.Empty;

    /// <summary>已绑定班级的一行文字。</summary>
    [ObservableProperty] private string _enrolledClassText = string.Empty;

    /// <summary>这条接入是怎么来的（账号 / 地点码+配对码）。</summary>
    [ObservableProperty] private string _enrollmentSourceText = string.Empty;

    // ── 账号接入（控制端：桌面 / 平板） ────────────────────────────────────────

    /// <summary>当前 SECTL 账号是否已登录——账号通道的唯一前提。</summary>
    [ObservableProperty] private bool _isAccountSignedIn;

    /// <summary>已登录账号的显示名。</summary>
    [ObservableProperty] private string _accountName = string.Empty;

    /// <summary>账号接入区的提示；只在账号卡里显示。</summary>
    [ObservableProperty] private string _accountMessage = string.Empty;

    /// <summary>账号区是否正在请求（登录跳转 / 拉地点年级班级 / 接入）。</summary>
    [ObservableProperty] private bool _isAccountBusy;

    [ObservableProperty] private CC.ControlCenterSite? _selectedAccountSite;

    [ObservableProperty] private CC.ControlCenterGrade? _selectedAccountGrade;

    [ObservableProperty] private CC.ControlCenterClass? _selectedAccountClass;

    public ObservableCollection<CC.ControlCenterSite> AccountSites { get; } = [];

    public ObservableCollection<CC.ControlCenterGrade> AccountGrades { get; } = [];

    public ObservableCollection<CC.ControlCenterClass> AccountClasses { get; } = [];

    public bool HasAccountSites => AccountSites.Count > 0;

    public bool HasAccountGrades => AccountGrades.Count > 0;

    public bool HasAccountClasses => AccountClasses.Count > 0;

    public bool ShowAccountMessage => !string.IsNullOrEmpty(AccountMessage);

    /// <summary>账号卡提示是否属于「成功」类。</summary>
    public bool AccountMessageIsSuccess => IsSuccessMessage(AccountMessage);

    public bool CanSignInAccount => _auth is not null && !IsAccountSignedIn && !IsAccountBusy;

    /// <summary>
    ///     账号接入：必须已登录，且地点、年级、班级三样都选好。
    ///     班级不是可选项——没有班级的接入记录既不能占班，也不会被控制台当成"上课中"，
    ///     所以"先接进来、以后再指定班级"这条路径已经关掉。
    /// </summary>
    public bool CanEnrollWithAccount => _enrollmentStore is not null
                                        && _controlCenterClient is not null
                                        && !IsEnrolled
                                        && !IsEnrolling
                                        && !IsAccountBusy
                                        && IsAccountSignedIn
                                        && SelectedAccountSite is not null
                                        && SelectedAccountGrade is not null
                                        && SelectedAccountClass is not null;

    /// <summary>年级列表。来自 <c>/control/api/device/tree</c>，与班级级联共用同一份树。</summary>
    public ObservableCollection<ControlGradeNode> GradeOptions { get; } = [];

    /// <summary>当前年级下的班级列表。</summary>
    public ObservableCollection<ControlClassNode> ClassOptions { get; } = [];

    [ObservableProperty] private ControlGradeNode? _selectedGrade;

    [ObservableProperty] private ControlClassNode? _selectedClass;

    /// <summary>正在读取年级/班级。</summary>
    [ObservableProperty] private bool _isTreeLoading;

    /// <summary>是否已经拿到可选的年级/班级（没接入或读取失败时为 false）。</summary>
    [ObservableProperty] private bool _hasTree;

    /// <summary>班级保存提示（与接入错误共用同一行，但文案分开）。</summary>
    [ObservableProperty] private string _classSaveMessage = string.Empty;

    /// <summary>接入设置卡表面显示当前方式，并提供切换到另一种方式的命令。</summary>
    public string EnrollmentModeLabel => IsAccountEnrollmentMode
        ? LR.S_AccountEnrollment
        : LR.S_NodeEnrollment;

    public string EnrollmentModeDescription => IsAccountEnrollmentMode
        ? LR.S_AccountEnrollment_D
        : LR.S_NodeEnrollment_D;

    public string SwitchEnrollmentModeLabel => IsAccountEnrollmentMode
        ? LR.C_SwitchToPairCode
        : LR.C_SwitchToAccount;

    public bool CanSwitchEnrollmentMode => !IsEnrolled && !IsEnrolling && !IsAccountBusy;

    public bool ShowEnrollmentStatus => IsEnrolled || IsEnrollmentUnreadable;

    public bool CanEnroll => !IsEnrolled && !IsEnrolling && !IsAccountBusy
                             && NodeEnrollmentClient.NormalizeSiteId(SiteIdInput) is not null
                             && NodeEnrollmentClient.NormalizePairingCode(PairingCodeInput) is not null;

    public bool CanClearEnrollment => _enrollmentStore is not null
                                      && !IsEnrolling
                                      && (IsEnrolled || IsEnrollmentUnreadable);

    /// <summary>本机当前绑定在某个班上：换班前必须先解除，不允许直接覆盖。</summary>
    public bool HasBoundClass => IsEnrolled && !string.IsNullOrEmpty(_enrollmentStore?.Status.ClassId);

    /// <summary>
    ///     保存班级：年级与班级**都必须选中**，且当前没有绑定别的班级。
    /// </summary>
    /// <remarks>
    ///     只选年级不选班级就等于"不分班"，设备会哪个班都不属于 —— 集控侧既不会显示它，
    ///     也不会把该班的抽取锁推给它。所以两个都选完才让按钮可点，不给这种半选状态。
    ///     已绑定班级时按钮也不可点：换班要先走 <see cref="CanClearClass" /> 那一步。
    /// </remarks>
    public bool CanSaveClass => IsEnrolled
                                && !IsEnrolling
                                && !IsTreeLoading
                                && HasTree
                                && !HasBoundClass
                                && SelectedGrade is not null
                                && SelectedClass is not null;

    /// <summary>解除已绑定的班级（先解除、再重新选年级和班级）。</summary>
    public bool CanClearClass => IsEnrolled
                                 && !IsEnrolling
                                 && HasBoundClass
                                 && _enrollmentClient is not null
                                 && _enrollmentStore is not null;

    /// <summary>年级/班级选择器是否可用：已绑定班级时先锁住，避免"看起来能改、其实被拒"。</summary>
    public bool CanPickClass => !HasBoundClass && !IsTreeLoading;

    /// <summary>地点 ID 输入上限：整句粘贴要先能收下整句（见 <see cref="NodeEnrollmentClient.TryParseEnrollmentText" />）。</summary>
    public int SiteIdMaxLength => NodeEnrollmentClient.MaxInputTextLength;

    /// <summary>配对码输入上限：同上，整句粘贴需要。</summary>
    public int PairingCodeMaxLength => NodeEnrollmentClient.MaxInputTextLength;

    public bool ShowEnrollmentMessage => !string.IsNullOrEmpty(EnrollmentMessage);

    /// <summary>接入提示是否属于「成功」类：成功用绿色 Toast，其余用红色。</summary>
    public bool EnrollmentMessageIsSuccess => IsSuccessMessage(EnrollmentMessage);

    /// <summary>班级保存提示是否属于「成功」类。</summary>
    public bool ClassSaveMessageIsSuccess => IsSuccessMessage(ClassSaveMessage);

    /// <summary>
    ///     成功类文案只有三种：接入成功（带地点名的格式化串）、班级已保存、班级清空。
    /// </summary>
    /// <remarks>
    ///     没有在每次赋值为空串/失败串的地方各加一个布尔标记：那样要改十几处赋值点，
    ///     只要漏一处就会把失败报成成功；反过来按文案识别，漏一处只是颜色不对，不会骗人。
    /// </remarks>
    private static bool IsSuccessMessage(string? message)
    {
        if (string.IsNullOrEmpty(message))
            return false;

        return MatchesTemplate(message, LR.M_Enroll_Success)
               || MatchesTemplate(message, LR.M_Account_Enrolled)
               || MatchesTemplate(message, LR.M_Enroll_Unenrolled)
               || MatchesTemplate(message, LR.M_Enroll_Unenroll_LocalOnly)
               || message == LR.M_Enroll_Class_Saved
               || message == LR.M_Enroll_Class_Cleared
               || message == LR.M_Enroll_Class_None;

        // 成功文案是带占位符的模板，只比较第一个占位符之前的前缀即可。
        static bool MatchesTemplate(string message, string template)
        {
            var placeholder = template.IndexOf("{0}", StringComparison.Ordinal);
            var prefix = placeholder < 0 ? template : template[..placeholder];
            return !string.IsNullOrEmpty(prefix) && message.StartsWith(prefix, StringComparison.Ordinal);
        }
    }

    partial void OnSiteIdInputChanged(string value)
    {
        OnPropertyChanged(nameof(CanEnroll));
        if (_suppressAutoParse)
            return;

        // 用户把控制台复制的一整句粘进任一输入框：两个都认出来时一次填好两个框。
        if (TryApplyPastedComposite(value))
            return;

        _suppressPairingFormat = true;
        // 边输边归一：用户贴进来的 "sc k7q2m9" 与手输的格式应当得到同一个值，不必让他自己排版。
        var normalized = NodeEnrollmentClient.NormalizeSiteId(value);
        if (normalized is not null && !string.Equals(normalized, value, StringComparison.Ordinal))
            SiteIdInput = normalized;
        _suppressPairingFormat = false;
    }

    partial void OnPairingCodeInputChanged(string value)
    {
        OnPropertyChanged(nameof(CanEnroll));
        if (_suppressAutoParse)
            return;

        if (TryApplyPastedComposite(value))
            return;

        if (_suppressPairingFormat)
            return;

        var normalized = NodeEnrollmentClient.NormalizePairingCode(value);
        var formatted = normalized is null ? value : NodeEnrollmentClient.FormatPairingCode(normalized);
        if (string.Equals(formatted, value, StringComparison.Ordinal))
            return;

        _suppressPairingFormat = true;
        PairingCodeInput = formatted;
        _suppressPairingFormat = false;
    }

    /// <summary>整句粘贴：同时认出地点 ID 与配对码时一次填好两个输入框并返回 true。</summary>
    private bool TryApplyPastedComposite(string? text)
    {
        if (!NodeEnrollmentClient.TryParseEnrollmentText(text, out var siteId, out var pairingCode)
            || siteId is null
            || pairingCode is null)
            return false;

        _suppressAutoParse = true;
        try
        {
            SiteIdInput = siteId;
            PairingCodeInput = NodeEnrollmentClient.FormatPairingCode(pairingCode);
        }
        finally
        {
            _suppressAutoParse = false;
        }

        OnPropertyChanged(nameof(CanEnroll));
        return true;
    }

    partial void OnIsEnrollingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEnroll));
        OnPropertyChanged(nameof(CanEnrollWithAccount));
        OnPropertyChanged(nameof(CanSwitchEnrollmentMode));
        OnPropertyChanged(nameof(CanSaveClass));
        OnPropertyChanged(nameof(CanClearClass));
        OnPropertyChanged(nameof(CanClearEnrollment));
    }

    partial void OnIsEnrolledChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEnroll));
        OnPropertyChanged(nameof(CanEnrollWithAccount));
        OnPropertyChanged(nameof(CanSwitchEnrollmentMode));
        OnPropertyChanged(nameof(ShowEnrollmentStatus));
        OnPropertyChanged(nameof(CanClearEnrollment));
        OnPropertyChanged(nameof(CanSaveClass));
        OnPropertyChanged(nameof(CanClearClass));
        OnPropertyChanged(nameof(HasBoundClass));
        OnPropertyChanged(nameof(CanPickClass));
    }

    partial void OnIsEnrollmentUnreadableChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowEnrollmentStatus));
        OnPropertyChanged(nameof(CanClearEnrollment));
    }

    partial void OnIsAccountEnrollmentModeChanged(bool value)
    {
        OnPropertyChanged(nameof(EnrollmentModeLabel));
        OnPropertyChanged(nameof(EnrollmentModeDescription));
        OnPropertyChanged(nameof(SwitchEnrollmentModeLabel));
    }

    partial void OnIsTreeLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSaveClass));
        OnPropertyChanged(nameof(CanPickClass));
    }

    partial void OnHasTreeChanged(bool value) => OnPropertyChanged(nameof(CanSaveClass));

    partial void OnEnrollmentMessageChanged(string value)
    {
        OnPropertyChanged(nameof(ShowEnrollmentMessage));
        OnPropertyChanged(nameof(EnrollmentMessageIsSuccess));
    }

    /// <summary>班级保存提示变化：Toast 需要区分成功与失败。</summary>
    partial void OnClassSaveMessageChanged(string value) => OnPropertyChanged(nameof(ClassSaveMessageIsSuccess));

    /// <summary>切换年级：班级列表换成这一年级的班，不重新请求服务端。</summary>
    partial void OnSelectedGradeChanged(ControlGradeNode? value)
    {
        ClassOptions.Clear();
        if (value is null)
            return;

        foreach (var item in value.Classes)
            ClassOptions.Add(item);

        // 已绑定班级落在当前年级下时选中它；切到别的年级则不预选，让用户明确选一个。
        var currentClassId = _enrollmentStore?.Status.ClassId;
        SelectedClass = value.Classes.FirstOrDefault(c =>
                            string.Equals(c.Id, currentClassId, StringComparison.Ordinal))
                        ?? (value.Classes.Count == 1 ? value.Classes[0] : null);

        OnPropertyChanged(nameof(CanSaveClass));
    }

    partial void OnSelectedClassChanged(ControlClassNode? value) => OnPropertyChanged(nameof(CanSaveClass));

    public void Dispose()
    {
        _stateStore.Changed -= OnStateStoreChanged;
        _client.LinkStateChanged -= OnLinkStateChanged;

        if (_enrollmentStore is not null)
            _enrollmentStore.Changed -= OnEnrollmentStoreChanged;

        if (_accountSubscribed && _auth is not null)
        {
            _auth.StateChanged -= OnAuthStateChanged;
            _accountSubscribed = false;
        }
    }

    [RelayCommand]
    private void Reconnect() => _client.Wake();

    [RelayCommand]
    private void SwitchEnrollmentMode()
    {
        if (!CanSwitchEnrollmentMode)
            return;

        IsAccountEnrollmentMode = !IsAccountEnrollmentMode;
    }

    [RelayCommand]
    private void OpenControlPlane()
    {
        // v4.1 起控制台是主站 SPA 的 /dashboard/control，直接打开基址会落到官网首页。
        var url = ControlPlaneDefaults.ConsoleUrl(_controlPlaneEndpointStore.Current);
        if (!_externalLauncher.TryOpenUri(url))
            _logger.LogWarning("打开集控平台失败：{Url}", url);
    }

    /// <summary>
    ///     用"地点 ID + 配对码"接入。成功后立刻把年级/班级树拉回来。
    /// </summary>
    /// <remarks>
    ///     失败只显示 **可操作的中文原因**，不回显配对码、不写日志：配对码是一次性凭据，
    ///     进了日志就等于泄漏。
    /// </remarks>
    [RelayCommand]
    private async Task EnrollAsync()
    {
        if (_enrollmentStore is null || _enrollmentClient is null || !CanEnroll)
            return;

        var siteId = NodeEnrollmentClient.NormalizeSiteId(SiteIdInput);
        var pairingCode = NodeEnrollmentClient.NormalizePairingCode(PairingCodeInput);
        if (siteId is null || pairingCode is null)
            return;

        IsEnrolling = true;
        EnrollmentMessage = string.Empty;
        ClassSaveMessage = string.Empty;
        try
        {
            // 重新接入同一地点时保留原来的显示名；跨地点时仍然用本机设置里的名字。
            var record = await _enrollmentClient.EnrollAsync(
                siteId,
                pairingCode,
                ControlNodeDisplayName.Resolve(_stateStore.Current.DisplayName, _options.HostName),
                CancellationToken.None).ConfigureAwait(true);

            if (!_enrollmentStore.Save(record))
            {
                EnrollmentMessage = string.Format(LR.M_Enroll_Detail, "save_failed");
                return;
            }

            ApplyEnrollmentIdentity(record);
            PairingCodeInput = string.Empty;
            EnrollmentMessage = string.Format(LR.M_Enroll_Success, record.SiteName);
            await LoadTreeAsync().ConfigureAwait(true);
        }
        catch (NodeEnrollmentException exception)
        {
            EnrollmentMessage = DescribeEnrollmentFailure(exception);
            _logger.LogWarning("接入集控失败：{Code}", exception.ServerCode ?? exception.Failure.ToString());
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "接入集控失败。");
            EnrollmentMessage = LR.M_Enroll_Err_Network;
        }
        finally
        {
            IsEnrolling = false;
        }
    }

    /// <summary>
    ///     清除接入：先让服务端删掉这一台设备，再清本机凭据；离线时保留本地记录。
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         只清本地是不够的：设备记录会留在原地点的列表里，网页上一直挂着一台"接了但没上线"
    ///         的机器，还得管理员去删。教室机前的人通常两个身份都没有，所以这一步必须和集控协商。
    ///     </para>
    ///     <para>
    ///         服务端已经不认识这台设备（401 <c>bad_secret</c> / <c>device_revoked</c> / 404
    ///         <c>device_not_found</c>）时，本地照清不误——目的已经达成；只有网络错误或服务端
    ///         5xx 才保留记录并提示重试，因为那时服务端可能还留着这台设备。
    ///     </para>
    /// </remarks>
    [RelayCommand]
    private async Task ClearEnrollmentAsync()
    {
        if (_enrollmentStore is null || IsEnrolling)
            return;

        var hasCredential = _enrollmentStore.TryGetCredential(out var credential) && credential is not null;
        if (hasCredential && !IsEnrollmentUnreadable && _enrollmentClient is not null)
        {
            IsEnrolling = true;
            EnrollmentMessage = string.Empty;
            try
            {
                await _enrollmentClient.UnenrollAsync(credential!, CancellationToken.None).ConfigureAwait(true);
                ClearLocalEnrollment();
                EnrollmentMessage = LR.M_Enroll_Unenrolled;
                return;
            }
            catch (NodeEnrollmentException exception) when (IsAlreadyDetached(exception))
            {
                // 服务端已经删掉了（或凭据早被吊销）：本地清掉就完成了，不报错。
                ClearLocalEnrollment();
                EnrollmentMessage = LR.M_Enroll_Unenroll_LocalOnly;
                return;
            }
            catch (NodeEnrollmentException exception)
            {
                _logger.LogWarning("清除集控接入失败：{Code}", exception.ServerCode ?? exception.Failure.ToString());
                EnrollmentMessage = LR.M_Enroll_Err_Unenroll;
                return;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "清除集控接入失败。");
                EnrollmentMessage = LR.M_Enroll_Err_Unenroll;
                return;
            }
            finally
            {
                IsEnrolling = false;
            }
        }

        // 没有可用凭据（或记录读不出来）：服务端那边没有能证明自己身份的东西，只能清本地。
        ClearLocalEnrollment();
        EnrollmentMessage = string.Empty;
    }

    private static bool IsAlreadyDetached(NodeEnrollmentException exception) =>
        exception.StatusCode == 404
        || exception.Failure is NodeEnrollmentFailure.CodeRevoked or NodeEnrollmentFailure.CodeNotFound;

    private void ClearLocalEnrollment()
    {
        _enrollmentStore!.Clear();
        ClassSaveMessage = string.Empty;
        IsEnrollmentUnreadable = false;
        HasTree = false;
        GradeOptions.Clear();
        ClassOptions.Clear();
        _client.Wake();
    }

    /// <summary>
    ///     把当前选中的「年级 + 班级」保存到控制面；两个都必须选中。
    /// </summary>
    /// <remarks>
    ///     已绑定班级时这里直接拒绝：换班必须先把旧班解除（<see cref="ClearClassAsync" />），
    ///     让服务端先看到一次明确的"离开"，再看到"进入"。直接覆盖会让控制台的班级页面
    ///     在两台设备之间反复跳，也让人分不清这台机器到底还在不在原班。
    /// </remarks>
    [RelayCommand]
    private async Task SaveClassAsync()
    {
        if (_enrollmentStore is null || _enrollmentClient is null || !IsEnrolled || IsEnrolling)
            return;

        if (HasBoundClass)
        {
            ClassSaveMessage = LR.S_Enrollment_Class_ClearFirst;
            return;
        }

        if (SelectedGrade is null || SelectedClass is null)
        {
            ClassSaveMessage = LR.M_Enroll_Class_NeedBoth;
            return;
        }

        if (!_enrollmentStore.TryGetCredential(out var credential) || credential is null)
        {
            ClassSaveMessage = LR.M_Enroll_Err_NoCredential;
            return;
        }

        var classroom = SelectedClass;
        IsEnrolling = true;
        ClassSaveMessage = string.Empty;
        try
        {
            await _enrollmentClient.AssignClassAsync(credential, classroom.Id, CancellationToken.None)
                .ConfigureAwait(true);

            _enrollmentStore.UpdateClass(classroom.Id, classroom.Name);
            _stateStore.Update(state => state with
            {
                ClassId = classroom.Id,
                SiteId = credential.SiteId,
                SiteName = credential.SiteName
            });

            ClassSaveMessage = LR.M_Enroll_Class_Saved;
        }
        catch (NodeEnrollmentException exception)
        {
            ClassSaveMessage = exception.Failure == NodeEnrollmentFailure.CodeRevoked
                ? LR.M_Enroll_Err_Revoked
                : LR.M_Enroll_Err_Assign;
            _logger.LogWarning("保存集控班级失败：{Code}", exception.ServerCode ?? exception.Failure.ToString());
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "保存集控班级失败。");
            ClassSaveMessage = LR.M_Enroll_Err_Network;
        }
        finally
        {
            IsEnrolling = false;
        }
    }

    /// <summary>解除本机已绑定的班级：服务端、本机记录与节点状态一起清空。</summary>
    /// <remarks>
    ///     服务端收到空 <c>classId</c> 会把设备的 <c>class_id</c> 清掉（§2.10.1）；本机把
    ///     <c>node-state.json</c> 的 <c>ClassId</c> 置空后，节点会话会自动发 <c>class.release</c>，
    ///     网页上这个班立刻显示为无人。之后再选年级+班级保存即可重新绑定。
    /// </remarks>
    [RelayCommand]
    private async Task ClearClassAsync()
    {
        if (_enrollmentStore is null || _enrollmentClient is null || !CanClearClass)
            return;

        if (!_enrollmentStore.TryGetCredential(out var credential) || credential is null)
        {
            ClassSaveMessage = LR.M_Enroll_Err_NoCredential;
            return;
        }

        IsEnrolling = true;
        ClassSaveMessage = string.Empty;
        try
        {
            await _enrollmentClient.AssignClassAsync(credential, null, CancellationToken.None)
                .ConfigureAwait(true);

            _enrollmentStore.UpdateClass(null, null);
            _stateStore.Update(state => state with
            {
                ClassId = null,
                SiteId = credential.SiteId,
                SiteName = credential.SiteName
            });
            SelectedClass = null;

            ClassSaveMessage = LR.M_Enroll_Class_Cleared;
        }
        catch (NodeEnrollmentException exception)
        {
            ClassSaveMessage = exception.Failure == NodeEnrollmentFailure.CodeRevoked
                ? LR.M_Enroll_Err_Revoked
                : LR.M_Enroll_Err_Assign;
            _logger.LogWarning("解除集控班级失败：{Code}", exception.ServerCode ?? exception.Failure.ToString());
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "解除集控班级失败。");
            ClassSaveMessage = LR.M_Enroll_Err_Network;
        }
        finally
        {
            IsEnrolling = false;
        }
    }

    /// <summary>
    ///     账号通道的入口：先登录（桌面走 loopback、平板同样走官方账号），
    ///     再把 <c>GET /control/api/me</c> 的地点列表拉回来。
    /// </summary>
    [RelayCommand]
    private async Task SignInAccountAsync()
    {
        if (_auth is null || IsAccountBusy)
            return;

        BeginAccountWork();
        AccountMessage = string.Empty;
        try
        {
            await _auth.SignInAsync().ConfigureAwait(true);
            RefreshAccountState();
            if (IsAccountSignedIn)
                await LoadAccountSitesAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "登录 SECTL 账号失败。");
            AccountMessage = LR.M_Account_Err_Network;
        }
        finally
        {
            EndAccountWork();
        }
    }

    /// <summary>重新拉账号能看到的地点（点错账号或刚在网页端被授权后刷新）。</summary>
    [RelayCommand]
    private async Task RefreshAccountAsync()
    {
        if (_auth is null || IsAccountBusy)
            return;

        RefreshAccountState();
        if (IsAccountSignedIn)
            await LoadAccountSitesAsync().ConfigureAwait(true);
    }

    /// <summary>
    ///     用当前账号把这台电脑接进所选地点。**这是账号通道的核心动作**：
    ///     服务端按 JWT 判成员角色，返回只出现一次的 device secret；本机把它写进
    ///     与配对码完全相同的接入记录（仅多记来源与账号），节点连接逻辑无需改动。
    /// </summary>
    [RelayCommand]
    private async Task EnrollWithAccountAsync()
    {
        if (_enrollmentStore is null || _controlCenterClient is null || !CanEnrollWithAccount)
            return;

        var site = SelectedAccountSite!;
        BeginAccountWork();
        AccountMessage = string.Empty;
        try
        {
            var result = await _controlCenterClient.EnrollAccountAsync(
                site.PublicId,
                ControlNodeDisplayName.Resolve(_stateStore.Current.DisplayName, _options.HostName),
                Environment.MachineName,
                _stateStore.Current.NodeId,
                SelectedAccountClass?.Id,
                CancellationToken.None).ConfigureAwait(true);

            var record = new NodeEnrollmentRecord
            {
                DeviceId = result.DeviceId,
                Secret = result.Secret,
                SiteId = result.SitePublicId,
                SiteName = result.SiteName,
                ClassId = result.ClassId,
                ClassName = result.ClassName,
                EnrollmentMethod = NodeEnrollmentMethods.Account,
                AccountUserId = _auth?.User?.ResolvedUserId,
                AccountName = _auth?.User?.ResolvedUserName,
                EnrolledAt = DateTimeOffset.UtcNow
            };

            if (!_enrollmentStore.Save(record))
            {
                AccountMessage = string.Format(LR.M_Enroll_Detail, "save_failed");
                return;
            }

            ApplyEnrollmentIdentity(record);
            AccountMessage = string.Format(LR.M_Account_Enrolled, result.SiteName ?? site.Name);
            await LoadTreeAsync().ConfigureAwait(true);
        }
        catch (ControlPlaneException exception)
        {
            AccountMessage = DescribeAccountFailure(exception);
            _logger.LogWarning("账号接入集控失败：{Code}", exception.Code);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "账号接入集控失败。");
            AccountMessage = LR.M_Account_Err_Network;
        }
        finally
        {
            EndAccountWork();
        }
    }

    private void OnAuthStateChanged(object? sender, EventArgs args) => RunOnUiThread(() =>
    {
        RefreshAccountState();
        if (IsAccountSignedIn)
            _ = LoadAccountSitesAsync();
        else
            ClearAccountSelection();
    });

    private void RefreshAccountState()
    {
        IsAccountSignedIn = _auth?.IsSignedIn ?? false;
        AccountName = _auth?.User?.ResolvedUserName ?? string.Empty;
        OnPropertyChanged(nameof(CanSignInAccount));
        OnPropertyChanged(nameof(CanEnrollWithAccount));
    }

    private void ClearAccountSelection()
    {
        SelectedAccountSite = null;
        SelectedAccountGrade = null;
        SelectedAccountClass = null;
        AccountSites.Clear();
        AccountGrades.Clear();
        AccountClasses.Clear();
        NotifyAccountCollections();
        AccountMessage = string.Empty;
    }

    private async Task LoadAccountSitesAsync()
    {
        if (_controlCenterClient is null || !IsAccountSignedIn)
            return;

        BeginAccountWork();
        try
        {
            var me = await _controlCenterClient.GetMeAsync(CancellationToken.None).ConfigureAwait(true);
            AccountSites.Clear();
            foreach (var site in me.Sites)
                AccountSites.Add(site);

            AccountGrades.Clear();
            AccountClasses.Clear();
            NotifyAccountCollections();

            // 已接入的地点优先选中；只有一个地点时直接选它，省掉一次点击。
            var currentSiteId = _enrollmentStore?.Status.SiteId;
            SelectedAccountSite = AccountSites.FirstOrDefault(s =>
                                    string.Equals(s.PublicId, currentSiteId, StringComparison.OrdinalIgnoreCase))
                                ?? (AccountSites.Count == 1 ? AccountSites[0] : null);

            if (SelectedAccountSite is null)
                AccountMessage = LR.M_Account_NoSite;
        }
        catch (ControlPlaneException exception)
        {
            AccountMessage = DescribeAccountFailure(exception);
            _logger.LogWarning("读取集控地点失败：{Code}", exception.Code);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "读取集控地点失败。");
            AccountMessage = LR.M_Account_Err_Network;
        }
        finally
        {
            EndAccountWork();
        }
    }

    private async Task LoadAccountGradesAsync(CC.ControlCenterSite site)
    {
        if (_controlCenterClient is null)
            return;

        BeginAccountWork();
        try
        {
            var grades = await _controlCenterClient.GetGradesAsync(site.PublicId, CancellationToken.None)
                .ConfigureAwait(true);

            // 用户在请求回来前又换了地点：这批结果已经过期。
            if (!string.Equals(SelectedAccountSite?.PublicId, site.PublicId, StringComparison.OrdinalIgnoreCase))
                return;

            AccountGrades.Clear();
            foreach (var grade in grades)
                AccountGrades.Add(grade);
            AccountClasses.Clear();
            NotifyAccountCollections();

            var gradeToSelect = AccountGrades.Count == 1 ? AccountGrades[0] : null;
            var enrollment = _enrollmentStore?.Status;
            var enrolledClassId = enrollment is { HasCredential: true }
                                  && string.Equals(enrollment.EnrollmentMethod, NodeEnrollmentMethods.Account,
                                      StringComparison.Ordinal)
                ? enrollment.ClassId
                : null;

            // 账号接入只保存班级 ID。多年级学校里按当前班级反查年级，清除后可以直接复用原选择。
            if (gradeToSelect is null && !string.IsNullOrWhiteSpace(enrolledClassId))
            {
                foreach (var grade in AccountGrades)
                {
                    if (!string.Equals(SelectedAccountSite?.PublicId, site.PublicId,
                            StringComparison.OrdinalIgnoreCase)
                        || SelectedAccountGrade is not null)
                        return;

                    var classes = await _controlCenterClient.GetClassesAsync(site.PublicId, grade.Id,
                        CancellationToken.None).ConfigureAwait(true);
                    if (classes.Any(item => string.Equals(item.Id, enrolledClassId, StringComparison.Ordinal)))
                    {
                        gradeToSelect = grade;
                        break;
                    }
                }
            }

            if (!string.Equals(SelectedAccountSite?.PublicId, site.PublicId,
                    StringComparison.OrdinalIgnoreCase)
                || SelectedAccountGrade is not null)
                return;

            SelectedAccountGrade = gradeToSelect;
        }
        catch (ControlPlaneException exception)
        {
            AccountMessage = DescribeAccountFailure(exception);
            _logger.LogWarning("读取集控年级失败：{Code}", exception.Code);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "读取集控年级失败。");
            AccountMessage = LR.M_Account_Err_Network;
        }
        finally
        {
            EndAccountWork();
        }
    }

    private async Task LoadAccountClassesAsync(CC.ControlCenterSite site, CC.ControlCenterGrade grade)
    {
        if (_controlCenterClient is null)
            return;

        BeginAccountWork();
        try
        {
            var classes = await _controlCenterClient.GetClassesAsync(site.PublicId, grade.Id, CancellationToken.None)
                .ConfigureAwait(true);

            if (!string.Equals(SelectedAccountSite?.PublicId, site.PublicId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(SelectedAccountGrade?.Id, grade.Id, StringComparison.Ordinal))
                return;

            AccountClasses.Clear();
            foreach (var item in classes)
                AccountClasses.Add(item);
            NotifyAccountCollections();

            // 已绑定的班级落在这一年级里就预选它；否则只有一个班时直接选。
            var currentClassId = _enrollmentStore?.Status.ClassId;
            SelectedAccountClass = AccountClasses.FirstOrDefault(c =>
                                       string.Equals(c.Id, currentClassId, StringComparison.Ordinal))
                                   ?? (AccountClasses.Count == 1 ? AccountClasses[0] : null);
        }
        catch (ControlPlaneException exception)
        {
            AccountMessage = DescribeAccountFailure(exception);
            _logger.LogWarning("读取集控班级失败：{Code}", exception.Code);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "读取集控班级失败。");
            AccountMessage = LR.M_Account_Err_Network;
        }
        finally
        {
            EndAccountWork();
        }
    }

    private void NotifyAccountCollections()
    {
        OnPropertyChanged(nameof(HasAccountSites));
        OnPropertyChanged(nameof(HasAccountGrades));
        OnPropertyChanged(nameof(HasAccountClasses));
        OnPropertyChanged(nameof(CanEnrollWithAccount));
    }

    private void BeginAccountWork()
    {
        _accountWork++;
        IsAccountBusy = true;
    }

    private void EndAccountWork()
    {
        if (--_accountWork <= 0)
        {
            _accountWork = 0;
            IsAccountBusy = false;
        }
    }

    /// <summary>把账号接入的失败码翻成可操作的中文；不回显令牌、不回显 secret。</summary>
    private string DescribeAccountFailure(ControlPlaneException exception) => exception.Code switch
    {
        "not_signed_in" or "unauthorized" or "not_authed" => LR.M_Account_NotSignedIn,
        "device_already_enrolled" => LR.M_Account_AlreadyEnrolled,
        "device_conflict" => LR.M_Account_DeviceConflict,
        "site_not_found" or "class_not_found" => LR.M_Account_NoSite,
        "too_many_requests" => LR.M_Account_RateLimited,
        "forbidden" => LR.M_Account_Forbidden,
        "network_error" => LR.M_Account_Err_Network,
        null or "" => LR.M_Account_Err_Generic,
        _ => string.Format(LR.M_Account_Err_Code, exception.Code)
    };

    partial void OnIsAccountSignedInChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSignInAccount));
        OnPropertyChanged(nameof(CanEnrollWithAccount));
    }

    partial void OnIsAccountBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEnroll));
        OnPropertyChanged(nameof(CanSwitchEnrollmentMode));
        OnPropertyChanged(nameof(CanSignInAccount));
        OnPropertyChanged(nameof(CanEnrollWithAccount));
    }

    partial void OnAccountMessageChanged(string value)
    {
        OnPropertyChanged(nameof(ShowAccountMessage));
        OnPropertyChanged(nameof(AccountMessageIsSuccess));
    }

    partial void OnSelectedAccountSiteChanged(CC.ControlCenterSite? value)
    {
        // 换地点必须把年级/班级一起作废：否则旧地点的年级会留在下拉里，接入时带着别人的班级提交。
        // 先清选中项再清集合，两个 partial 处理器各自负责下一级的清理。
        SelectedAccountGrade = null;
        SelectedAccountClass = null;
        AccountGrades.Clear();
        AccountClasses.Clear();
        NotifyAccountCollections();
        if (value is not null)
            _ = LoadAccountGradesAsync(value);
    }

    partial void OnSelectedAccountGradeChanged(CC.ControlCenterGrade? value)
    {
        SelectedAccountClass = null;
        AccountClasses.Clear();
        NotifyAccountCollections();
        if (value is not null && SelectedAccountSite is not null)
            _ = LoadAccountClassesAsync(SelectedAccountSite, value);
    }

    /// <summary>班级决定接入按钮是否可点，选中/清空都要重新广播一次。</summary>
    partial void OnSelectedAccountClassChanged(CC.ControlCenterClass? value) =>
        OnPropertyChanged(nameof(CanEnrollWithAccount));

    private void ApplyEnrollmentIdentity(NodeEnrollmentRecord record)
    {
        _suppressPersist = true;
        try
        {
            _stateStore.Update(state => state with
            {
                // 本机 ID 保持 UUID 不动：服务端签发的是 device_id（凭据里另存），拿它覆盖本机 ID
                // 会让「本机身份」跟着接入记录走——换一个地点接入，本机 ID 就变了，日志与本地
                // 状态也对不上。展示层要设备 ID 就去看凭据，不要污染本机标识。
                SiteId = record.SiteId,
                SiteName = record.SiteName,
                ClassId = record.ClassId
            });
        }
        finally
        {
            _suppressPersist = false;
        }

        NormalizeNodeEndpoint();
        _client.Wake();
        RefreshEnrollment();
    }

    private void OnEnrollmentStoreChanged(object? sender, NodeEnrollmentStatus status) =>
        RunOnUiThread(RefreshEnrollment);

    /// <summary>把接入状态翻译成一句话；**这里读不到凭据真值，所以不可能回显**。</summary>
    private void RefreshEnrollment()
    {
        var status = _enrollmentStore?.Status ?? NodeEnrollmentStatus.NotEnrolled;

        // 已有接入记录时沿用它的来源：清除后回到同一套接入表单，账号接入不会跳回地点码。
        if (status.HasCredential
            && string.Equals(status.EnrollmentMethod, NodeEnrollmentMethods.Account, StringComparison.Ordinal))
            IsAccountEnrollmentMode = true;

        IsEnrolled = status.HasCredential;
        IsEnrollmentUnreadable = status.IsUnreadable;
        EnrollmentStatusText = !status.HasCredential
            ? status.IsUnreadable ? LR.M_Enroll_Unreadable : LR.M_Enroll_None
            : string.Format(LR.M_Enroll_Active, status.SiteName ?? status.SiteId ?? status.DeviceId ?? string.Empty);

        EnrolledSiteText = status.HasCredential
            ? $"{status.SiteName ?? LR.M_Enroll_UnknownSite}（{status.SiteId ?? "-"}）"
            : string.Empty;
        EnrolledDeviceText = status.DeviceName ?? status.DeviceId ?? string.Empty;
        EnrolledClassText = status.HasCredential
            ? string.IsNullOrWhiteSpace(status.ClassName) && string.IsNullOrWhiteSpace(status.ClassId)
                ? LR.M_Enroll_Class_NotSelected
                : status.ClassName ?? status.ClassId ?? string.Empty
            : string.Empty;

        EnrollmentSourceText = !status.HasCredential
            ? string.Empty
            : string.Equals(status.EnrollmentMethod, NodeEnrollmentMethods.Account, StringComparison.Ordinal)
                ? string.Format(LR.M_Enroll_Source_Account, status.AccountName ?? LR.M_Enroll_UnknownAccount)
                : LR.M_Enroll_Source_PairCode;

        if (!status.HasCredential)
        {
            HasTree = false;
            GradeOptions.Clear();
            ClassOptions.Clear();
        }

        // 绑定/解绑都会改变这三项：按钮可用性、选择器锁定、以及"换班要先解除"的提示。
        OnPropertyChanged(nameof(HasBoundClass));
        OnPropertyChanged(nameof(CanSaveClass));
        OnPropertyChanged(nameof(CanClearClass));
        OnPropertyChanged(nameof(CanPickClass));
    }

    /// <summary>读取年级/班级树，并把当前绑定的班级预选出来。</summary>
    private async Task LoadTreeAsync()
    {
        if (_enrollmentClient is null || _enrollmentStore is null
            || !_enrollmentStore.TryGetCredential(out var credential) || credential is null)
            return;

        IsTreeLoading = true;
        try
        {
            var tree = await _enrollmentClient.GetDeviceTreeAsync(credential, CancellationToken.None)
                .ConfigureAwait(true);

            GradeOptions.Clear();
            foreach (var grade in tree.Grades)
                GradeOptions.Add(grade);

            HasTree = tree.Grades.Count > 0;
            EnrolledDeviceText = tree.DeviceName ?? EnrolledDeviceText;

            var classId = tree.ClassId ?? credential.ClassId;

            // 控制面的树是权威：本机记录跟着它走，否则"已绑定"的判断会和网页上看到的不一致。
            if (!string.Equals(classId, credential.ClassId, StringComparison.Ordinal))
            {
                var boundName = tree.EnumerateClasses()
                    .FirstOrDefault(c => string.Equals(c.ClassId, classId, StringComparison.Ordinal))
                    .ClassName;
                _enrollmentStore.UpdateClass(classId, boundName);
            }

            SelectedGrade = tree.Grades.FirstOrDefault(g =>
                                classId is not null && g.Classes.Any(c => c.Id == classId))
                            ?? tree.Grades.FirstOrDefault();
            if (SelectedGrade is not null)
            {
                SelectedClass = SelectedGrade.Classes.FirstOrDefault(c => c.Id == classId)
                                ?? (SelectedGrade.Classes.Count == 1 ? SelectedGrade.Classes[0] : null);
            }

            // 控制面的树是权威：把它同步回本机状态，重连时不必再拉一次。
            _stateStore.Update(state => state with
            {
                SiteId = tree.SiteId,
                SiteName = tree.SiteName,
                ClassId = classId
            });
        }
        catch (NodeEnrollmentException exception)
        {
            ClassSaveMessage = DescribeEnrollmentFailure(exception);
            _logger.LogWarning("读取集控年级/班级失败：{Code}", exception.ServerCode ?? exception.Failure.ToString());
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "读取集控年级/班级失败。");
            ClassSaveMessage = LR.M_Enroll_Err_Tree;
        }
        finally
        {
            IsTreeLoading = false;
        }
    }

    /// <summary>把服务端错误码翻译成中文。**每个码对应一个用户能采取的动作**，不只是"失败了"。</summary>
    private static string DescribeEnrollmentFailure(NodeEnrollmentException exception) => exception.Failure switch
    {
        NodeEnrollmentFailure.SiteNotFound => LR.M_Enroll_Err_SiteNotFound,
        NodeEnrollmentFailure.CodeNotFound => LR.M_Enroll_Err_CodeNotFound,
        NodeEnrollmentFailure.CodeSiteMismatch => LR.M_Enroll_Err_SiteMismatch,
        NodeEnrollmentFailure.CodeExpired => LR.M_Enroll_Err_CodeExpired,
        NodeEnrollmentFailure.CodeUsed => LR.M_Enroll_Err_CodeUsed,
        NodeEnrollmentFailure.CodeRevoked => LR.M_Enroll_Err_Revoked,
        NodeEnrollmentFailure.PlatformNotAllowed => LR.M_Enroll_Err_Platform,
        NodeEnrollmentFailure.DeviceConflict => string.Format(
            LR.M_Enroll_Err_Conflict, exception.ConflictSiteName ?? LR.M_Enroll_UnknownSite),
        NodeEnrollmentFailure.TooManyAttempts => LR.M_Enroll_Err_TooMany,
        NodeEnrollmentFailure.InvalidRequest => LR.M_Enroll_Err_Invalid,
        NodeEnrollmentFailure.Network => LR.M_Enroll_Err_Network,
        _ => string.Format(LR.M_Enroll_Detail, exception.ServerCode ?? exception.Failure.ToString())
    };

    partial void OnRemoteControlEnabledChanged(bool value)
    {
        if (_suppressPersist)
            return;

        _stateStore.Update(state => state with { RemoteControlEnabled = value });
        _client.Wake();
    }

    partial void OnRemoteDrawPageEnabledChanged(bool value)
    {
        if (_suppressPersist)
            return;

        _stateStore.Update(state => state with { RemoteDrawPageEnabled = value });
    }

    partial void OnDisplayNameChanged(string value)
    {
        if (_suppressPersist)
            return;

        _stateStore.Update(state => state with { DisplayName = value });
        _client.Wake();
    }

    /// <summary>
    ///     把节点通道地址对齐到控制面的实际协议（<c>http</c> → <c>ws</c>，<c>https</c> → <c>wss</c>）：
    ///     自建实例的明文端口绝不能配 <c>wss://</c>，否则 WebSocket 会拿明文端口做 TLS 握手，永远连不上。
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

    private void OnStateStoreChanged(object? sender, ControlNodeState state) =>
        RunOnUiThread(() => RefreshFromState(state));

    private void OnLinkStateChanged(object? sender, ControlNodeLinkState state) =>
        RunOnUiThread(() => ApplyLinkState(state));

    private void RefreshControlPlaneEndpoint()
    {
        // 地址不再有界面入口：这里只把按钮文案对齐到实际生效的基址，
        // 并把节点通道地址校正到控制面的协议，避免遗留的自建端点文件连不上。
        OpenControlPlaneLabel = _controlPlaneEndpointStore.IsCustom
            ? LR.C_OpenControlPlane_ThirdParty
            : LR.C_OpenControlPlane_Official;

        NormalizeNodeEndpoint();
    }

    private void RefreshFromState(ControlNodeState state)
    {
        _suppressPersist = true;
        try
        {
            RemoteControlEnabled = state.RemoteControlEnabled;
            RemoteDrawPageEnabled = state.RemoteDrawPageEnabled;
            DisplayName = state.DisplayName ?? string.Empty;
            DrawLocked = state.DrawLocked;
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

    private string DescribeStatus(ControlNodeLinkState state) => state.Status switch
    {
        ControlNodeLinkStatus.Disabled => LR.M_Status_Disabled,
        ControlNodeLinkStatus.Idle => LR.M_Status_Idle,
        ControlNodeLinkStatus.Connecting => LR.M_Status_Connecting,
        ControlNodeLinkStatus.Connected => LR.M_Status_Connected,
        ControlNodeLinkStatus.WaitingToRetry => LR.M_Status_WaitingToRetry,
        ControlNodeLinkStatus.Blocked => IsBlockedForCredential(state.Detail) && IsEnrolled
            ? LR.M_Status_ReenrollRequired
            : LR.M_Status_Blocked,
        _ => state.Status.ToString()
    };

    private static bool IsBlockedForCredential(string? detail) =>
        string.Equals(detail, ControlV4Protocol.ErrorCodes.BadSecret, StringComparison.Ordinal)
        || string.Equals(detail, ControlV4Protocol.ErrorCodes.DeviceRevoked, StringComparison.Ordinal)
        || string.Equals(detail, ControlErrorCodes.Unauthorized, StringComparison.Ordinal);

    /// <summary>把连接层错误码翻译成用户能看懂的原因。</summary>
    private string DescribeDetail(string? detail) => detail switch
    {
        null or "" or "stopped" => string.Empty,
        "not_configured" => LR.M_Detail_NotConfigured,
        "not_signed_in" => LR.M_Detail_NotEnrolled,
        "not_enrolled" or "enrollment_unreadable" => LR.M_Detail_NotEnrolled,
        ControlV4Protocol.ErrorCodes.BadSecret or ControlV4Protocol.ErrorCodes.DeviceRevoked =>
            LR.M_Detail_EnrollmentRejected,
        ControlV4Protocol.ErrorCodes.ProtocolTooOld => LR.M_Detail_ProtocolTooOld,
        ControlV4Protocol.ErrorCodes.BadRequest => LR.M_Detail_InvalidRequest,
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
            _logger.LogDebug(exception, "刷新集控设置页状态失败。");
        }
    }
}
