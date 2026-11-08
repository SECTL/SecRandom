using System.Collections.ObjectModel;
using System.Linq;
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
using SecRandom.Shared.Models.ControlNode.V4;
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

    [ObservableProperty] private string _serverUrl = string.Empty;

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

    /// <summary>地址校验失败：仅提示，**无效地址绝不落盘**。</summary>
    [ObservableProperty] private bool _hasEndpointError;

    /// <summary>节点通道地址当前是否不是默认值（"恢复默认"按钮据此启用）。</summary>
    [ObservableProperty] private bool _isServerUrlCustom;

    /// <summary>控制台访问集控平台所用的基址。留空即回到线上默认。</summary>
    [ObservableProperty] private string _controlPlaneEndpoint = string.Empty;

    [ObservableProperty] private bool _hasControlPlaneEndpointError;

    [ObservableProperty] private bool _isControlPlaneEndpointCustom;

    [ObservableProperty] private string _openControlPlaneLabel = string.Empty;

    public string ControlPlaneEndpointDefault => ControlPlaneClient.DefaultBaseUrl;

    // ── 集控接入 ────────────────────────────────────────────────────────────────

    /// <summary>是否已经持有本机设备凭据。</summary>
    [ObservableProperty] private bool _isEnrolled;

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

    public bool CanEnroll => !IsEnrolling
                             && NodeEnrollmentClient.NormalizeSiteId(SiteIdInput) is not null
                             && NodeEnrollmentClient.NormalizePairingCode(PairingCodeInput) is not null;

    public bool CanClearEnrollment => _enrollmentStore is not null && (IsEnrolled || IsEnrollmentUnreadable);

    public bool CanSaveClass => IsEnrolled && !IsTreeLoading && HasTree;

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

        var successTemplate = LR.M_Enroll_Success;
        var placeholder = successTemplate.IndexOf("{0}", StringComparison.Ordinal);
        var successPrefix = placeholder < 0 ? successTemplate : successTemplate[..placeholder];
        return (!string.IsNullOrEmpty(successPrefix) && message.StartsWith(successPrefix, StringComparison.Ordinal))
               || message == LR.M_Enroll_Class_Saved
               || message == LR.M_Enroll_Class_None;
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

    partial void OnIsEnrollingChanged(bool value) => OnPropertyChanged(nameof(CanEnroll));

    partial void OnIsEnrolledChanged(bool value)
    {
        OnPropertyChanged(nameof(CanClearEnrollment));
        OnPropertyChanged(nameof(CanSaveClass));
    }

    partial void OnIsEnrollmentUnreadableChanged(bool value) =>
        OnPropertyChanged(nameof(CanClearEnrollment));

    partial void OnIsTreeLoadingChanged(bool value) => OnPropertyChanged(nameof(CanSaveClass));

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
    }

    public void Dispose()
    {
        _stateStore.Changed -= OnStateStoreChanged;
        _client.LinkStateChanged -= OnLinkStateChanged;

        if (_enrollmentStore is not null)
            _enrollmentStore.Changed -= OnEnrollmentStoreChanged;
    }

    [RelayCommand]
    private void Reconnect() => _client.Wake();

    [RelayCommand]
    private void ResetControlPlaneEndpoint()
    {
        _controlPlaneEndpointStore.ResetToDefault();
        RefreshControlPlaneEndpoint();
    }

    [RelayCommand]
    private void ResetServerUrl()
    {
        _stateStore.Update(state => state with { ServerUrl = ControlNodeClientOptions.DefaultEndpoint });
        _client.Wake();
    }

    [RelayCommand]
    private void OpenControlPlane()
    {
        // v4.1 起控制台是主站 SPA 的 /dashboard/control，直接打开基址会落到官网首页。
        var url = ControlPlaneClient.ConsoleUrl(_controlPlaneEndpointStore.Current);
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
        if (_enrollmentStore is null || _enrollmentClient is null || IsEnrolling)
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

    /// <summary>清除本机设备凭据并立刻断开节点连接。</summary>
    [RelayCommand]
    private void ClearEnrollment()
    {
        if (_enrollmentStore is null)
            return;

        _enrollmentStore.Clear();
        EnrollmentMessage = string.Empty;
        ClassSaveMessage = string.Empty;
        IsEnrollmentUnreadable = false;
        HasTree = false;
        GradeOptions.Clear();
        ClassOptions.Clear();
        _client.Wake();
    }

    /// <summary>把当前选中的班级保存到控制面；允许"不选班级"。</summary>
    [RelayCommand]
    private async Task SaveClassAsync()
    {
        if (_enrollmentStore is null || _enrollmentClient is null || !IsEnrolled)
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
            var classId = SelectedClass?.Id;
            await _enrollmentClient.AssignClassAsync(credential, classId, CancellationToken.None)
                .ConfigureAwait(true);

            _enrollmentStore.UpdateClass(classId, SelectedClass?.Name);
            _stateStore.Update(state => state with
            {
                ClassId = classId,
                SiteId = credential.SiteId,
                SiteName = credential.SiteName
            });

            ClassSaveMessage = classId is null ? LR.M_Enroll_Class_None : LR.M_Enroll_Class_Saved;
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

        if (!status.HasCredential)
        {
            HasTree = false;
            GradeOptions.Clear();
            ClassOptions.Clear();
        }
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

    partial void OnControlPlaneEndpointChanged(string value)
    {
        if (_suppressPersist)
            return;

        var endpoint = value?.Trim() ?? string.Empty;
        if (endpoint.Length == 0)
        {
            _controlPlaneEndpointStore.ResetToDefault();
            HasControlPlaneEndpointError = false;
            RefreshControlPlaneEndpoint();
            return;
        }

        if (!_controlPlaneEndpointStore.TryUpdate(endpoint, out _))
        {
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

    partial void OnServerUrlChanged(string value)
    {
        if (_suppressPersist)
            return;

        var endpoint = value?.Trim() ?? string.Empty;
        if (!ControlEndpointPolicy.TryValidate(endpoint, out _, out _))
        {
            HasEndpointError = true;
            RefreshServerUrlCustomFlag();
            return;
        }

        HasEndpointError = false;
        _stateStore.Update(state => state with { ServerUrl = endpoint });
        _client.Wake();
        RefreshServerUrlCustomFlag();
    }

    private void RefreshServerUrlCustomFlag() =>
        IsServerUrlCustom = !string.Equals(
            (ServerUrl ?? string.Empty).Trim(),
            ControlNodeClientOptions.DefaultEndpoint,
            StringComparison.OrdinalIgnoreCase);

    private void OnStateStoreChanged(object? sender, ControlNodeState state) =>
        RunOnUiThread(() => RefreshFromState(state));

    private void OnLinkStateChanged(object? sender, ControlNodeLinkState state) =>
        RunOnUiThread(() => ApplyLinkState(state));

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

        NormalizeNodeEndpoint();
    }

    private void RefreshFromState(ControlNodeState state)
    {
        _suppressPersist = true;
        try
        {
            RemoteControlEnabled = state.RemoteControlEnabled;
            RemoteDrawPageEnabled = state.RemoteDrawPageEnabled;
            ServerUrl = state.ServerUrl;
            RefreshServerUrlCustomFlag();
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