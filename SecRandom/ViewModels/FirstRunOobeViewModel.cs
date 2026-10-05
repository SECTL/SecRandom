using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Models.SubConfigs.General;
using SecRandom.Core.Models.SubConfigs.Personalized;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Services.ControlPlane;
using SecRandom.Services.Desktop;
using SecRandom.Services.FirstRun;
using SecRandom.Services.Voice;
using SecRandom.Shared.Models.Profile;
using LR = SecRandom.Langs.FirstRunOobe.Resources;

namespace SecRandom.ViewModels;

public sealed partial class FirstRunOobeViewModel : ViewModelBase, IDisposable
{
    private readonly MainConfigHandler _configHandler;
    private readonly FirstRunOobeService _oobeService;
    private readonly OobeDataSetupService _dataSetupService;
    private readonly DesktopIntegrationService _desktopIntegration;
    private readonly IProfileService _profileService;
    private readonly IProfileCatalogManager _catalogManager;
    private readonly IControlNodeStateStore _controlNodeStateStore;
    private readonly ControlNodeClientOptions _controlNodeOptions;
    private readonly IControlPlaneEndpointStore _controlPlaneEndpointStore;
    private readonly IExternalLauncher _externalLauncher;
    private AppearanceSettingsConfig? _appearanceSettings;
    private BasicSettingsConfig? _basicSettings;
    private PrivacySettingsConfig? _privacySettings;
    private FloatingWindowSettingsConfig? _floatingWindowSettings;
    private MoreSettingsConfig? _moreSettings;
    private bool _suppressControlPersist;

    [ObservableProperty] private int _selectedStep;
    [ObservableProperty] private bool _acceptedVerificationNotice;
    [ObservableProperty] private bool _acceptedPrivacyPolicy;
    [ObservableProperty] private bool _acceptedGpl;
    [ObservableProperty] private string _selectedStudentListName = string.Empty;
    [ObservableProperty] private string _selectedPrizeListName = string.Empty;
    [ObservableProperty] private bool _autostart;
    [ObservableProperty] private bool _externalIntegration;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _controlRemoteEnabled;
    [ObservableProperty] private string _controlDisplayName = string.Empty;
    [ObservableProperty] private string _controlGroupId = string.Empty;
    [ObservableProperty] private string _controlNodeId = string.Empty;
    [ObservableProperty] private string _openControlPlaneLabel = string.Empty;

    public FirstRunOobeViewModel(
        MainConfigHandler configHandler,
        FirstRunOobeService oobeService,
        OobeDataSetupService dataSetupService,
        DesktopIntegrationService desktopIntegration,
        IProfileService profileService,
        IProfileCatalogManager catalogManager,
        IControlNodeStateStore controlNodeStateStore,
        ControlNodeClientOptions controlNodeOptions,
        IControlPlaneEndpointStore controlPlaneEndpointStore,
        IExternalLauncher externalLauncher) : base(configHandler)
    {
        _configHandler = configHandler;
        _oobeService = oobeService;
        _dataSetupService = dataSetupService;
        _desktopIntegration = desktopIntegration;
        _profileService = profileService;
        _catalogManager = catalogManager;
        _controlNodeStateStore = controlNodeStateStore;
        _controlNodeOptions = controlNodeOptions;
        _controlPlaneEndpointStore = controlPlaneEndpointStore;
        _externalLauncher = externalLauncher;
        RefreshFromConfig();
        SelectedStep = IsPrivacyPolicyOnly ? 1 : 0;
    }

    public AppearanceSettingsConfig Appearance => _configHandler.Data.Appearance;
    public BasicSettingsConfig Basic => _configHandler.Data.General.Basic;
    public PrivacySettingsConfig PrivacySettings => _configHandler.Data.General.PrivacySettings;
    public FloatingWindowSettingsConfig FloatingWindow => _configHandler.Data.FloatingWindowSettings;
    public MoreSettingsConfig MoreSettings => _configHandler.Data.MoreSettings;

    /// <summary>集控节点显示名留空时上报的回落值（主机名），同时作为输入框水印。</summary>
    public string ControlHostName => _controlNodeOptions.HostName;

    /// <summary>显示名输入框的上限，直接取协议上限，避免这里再写一个字面量。</summary>
    public int ControlDisplayNameMaxLength => ControlNodeDisplayName.MaxLength;

    public ObservableCollection<string> StudentListNames { get; } = [];
    public ObservableCollection<string> PrizeListNames { get; } = [];
    public int SelectedStudentListCount => _profileService.CurrentStudentList?.Students.Count ?? 0;
    public int SelectedPrizeListCount => _profileService.CurrentPrizeList?.Prizes.Count ?? 0;
    public bool IsPrivacyPolicyOnly => _oobeService.IsPrivacyPolicyOnlyRequired();
    public bool IsFullSetup => !IsPrivacyPolicyOnly;
    public bool IsVerificationNoticeRequired => !IsPrivacyPolicyOnly ||
                                                Basic.AcceptedVerificationNoticeVersion < FirstRunOobeService.CurrentVerificationNoticeVersion;
    public bool IsWelcomeStep => !IsPrivacyPolicyOnly && SelectedStep == 0;
    public bool HasPrevious => !IsPrivacyPolicyOnly && SelectedStep > 0;
    public bool IsStatusVisible => HasPrevious || IsPrivacyPolicyOnly;
    public bool IsCompletionActionVisible => HasPrevious || IsPrivacyPolicyOnly;
    public bool IsFinalStep => IsPrivacyPolicyOnly || SelectedStep == StepCount - 1;
    public string NextButtonText => IsWelcomeStep ? LR.C_Start : IsFinalStep ? LR.C_Finish : LR.C_Next;
    public string StepProgress => IsPrivacyPolicyOnly
        ? LR.C_LegalTitle
        : string.Format(LR.M_StepProgress, SelectedStep, StepCount - 1);
    public bool CanContinue => !IsPrivacyPolicyStep ||
                               (AcceptedPrivacyPolicy && AcceptedGpl &&
                                (!IsVerificationNoticeRequired || AcceptedVerificationNotice));
    public bool IsPrivacyPolicyStep => IsPrivacyPolicyOnly || SelectedStep == 1;

    /// <summary>
    ///     轮播的步数（含欢迎页与完成页）。进度分母取 <c>StepCount - 1</c>，因为序号从 0 开始。
    /// </summary>
    /// <remarks>
    ///     顺序固定：欢迎 → 法律 → 名单 → 抽奖 → 外观 → 桌面集成 → 集控 → 隐私 → 完成。
    ///     集控排在这两页之间是有理由的：它和桌面集成同为"这台机器怎么接入"的本机设置
    ///     （两者都不写 <c>settings.json</c>），而隐私是"本机往外发什么"，必须留在完成页之前
    ///     作为最后一项需要用户表态的内容。
    /// </remarks>
    public int StepCount => 9;
    public string PageTitle => IsPrivacyPolicyOnly ? LR.C_LegalTitle : LR.C_Title;
    public string IntroText => IsPrivacyPolicyOnly ? LR.C_LegalDescription : LR.C_Intro;

    public bool SetLanguage(LanguageMode language)
    {
        if (Basic.Language == language)
            return false;

        Basic.Language = language;
        _configHandler.Data.VoiceSettings.VoiceEngine = EdgeTtsSpeechProvider.EdgeEngine;
        _configHandler.Data.VoiceSettings.EdgeTtsVoiceName = VoiceSettingsConfig.GetDefaultEdgeTtsVoiceName(language);
        _configHandler.Save();
        StatusMessage = string.Empty;
        OnPropertyChanged(nameof(IsPrivacyPolicyOnly));
        OnPropertyChanged(nameof(IsFullSetup));
        OnPropertyChanged(nameof(IsVerificationNoticeRequired));
        OnPropertyChanged(nameof(IsPrivacyPolicyStep));
        OnPropertyChanged(nameof(IsStatusVisible));
        OnPropertyChanged(nameof(IsCompletionActionVisible));
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(IntroText));
        OnPropertyChanged(nameof(NextButtonText));
        OnPropertyChanged(nameof(StepProgress));
        return true;
    }

    public void RefreshLocalizedText()
    {
        StatusMessage = string.Empty;
        RefreshOpenControlPlaneLabel();
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(IntroText));
        OnPropertyChanged(nameof(NextButtonText));
        OnPropertyChanged(nameof(StepProgress));
    }

    partial void OnSelectedStepChanged(int value)
    {
        OnPropertyChanged(nameof(IsWelcomeStep));
        OnPropertyChanged(nameof(HasPrevious));
        OnPropertyChanged(nameof(IsFinalStep));
        OnPropertyChanged(nameof(IsStatusVisible));
        OnPropertyChanged(nameof(IsCompletionActionVisible));
        OnPropertyChanged(nameof(NextButtonText));
        OnPropertyChanged(nameof(StepProgress));
        OnPropertyChanged(nameof(CanContinue));
    }

    partial void OnAcceptedPrivacyPolicyChanged(bool value) => OnPropertyChanged(nameof(CanContinue));
    partial void OnAcceptedGplChanged(bool value) => OnPropertyChanged(nameof(CanContinue));
    partial void OnAcceptedVerificationNoticeChanged(bool value) => OnPropertyChanged(nameof(CanContinue));

    partial void OnSelectedStudentListNameChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        _profileService.LoadStudentProfile(value, saveCurrent: false);
        _configHandler.Data.RollCallSettings.DefaultClass = value;
        _configHandler.Save();
        OnPropertyChanged(nameof(SelectedStudentListCount));
    }

    partial void OnSelectedPrizeListNameChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        _profileService.LoadPrizeProfile(value, saveCurrent: false);
        _configHandler.Data.LotterySettings.DefaultPool = value;
        _configHandler.Save();
        OnPropertyChanged(nameof(SelectedPrizeListCount));
    }

    /// <summary>
    ///     集控开关与身份**只写本机状态文件**（<c>data/config/control/node-state.json</c>），不碰 settings。
    /// </summary>
    /// <remarks>
    ///     这里是引导流程里唯一一处"改设置"却不经过 <see cref="MainConfigHandler" /> 的地方，而且是刻意的：
    ///     "这台机器是否允许被远控"要是能被一次设置导入打开，这道闸门就等于不存在。
    ///     节点此刻还没启连（HostedService 要等引导结束、Host 启动后才跑），所以这里只落盘、不唤醒客户端，
    ///     连接状态由集控设置页在启动后显示。
    /// </remarks>
    partial void OnControlRemoteEnabledChanged(bool value)
    {
        if (_suppressControlPersist)
            return;

        _controlNodeStateStore.Update(state => state with { RemoteControlEnabled = value });
    }

    /// <summary>
    ///     显示名保留用户输入的原样（含首尾空白），规范化只发生在落盘与上报两个边界。
    /// </summary>
    /// <remarks>每次按键都 Trim 会跟输入光标打架——想在词中间打空格都做不到。</remarks>
    partial void OnControlDisplayNameChanged(string value)
    {
        if (_suppressControlPersist)
            return;

        _controlNodeStateStore.Update(state => state with { DisplayName = value });
    }

    /// <summary>组 ID 两端空白一律去掉：它在协议里是精确匹配的键，多一个空格就连不上。</summary>
    partial void OnControlGroupIdChanged(string value)
    {
        if (_suppressControlPersist)
            return;

        _controlNodeStateStore.Update(state => state with { GroupId = value?.Trim() ?? string.Empty });
    }

    /// <summary>在浏览器里打开集控平台网页控制台（配了第三方地址就打开那一家）。</summary>
    [RelayCommand]
    private void OpenControlPlane()
    {
        if (_externalLauncher.TryOpenUri(_controlPlaneEndpointStore.Current))
            return;

        StatusMessage = LR.M_ControlPlatformOpenFailed;
    }

    public void Previous()
    {
        if (HasPrevious)
            SelectedStep--;
    }

    public bool Next()
    {
        if (!CanContinue)
        {
            StatusMessage = LR.M_AgreementRequired;
            return false;
        }

        StatusMessage = string.Empty;
        if (!IsFinalStep)
            SelectedStep++;
        return true;
    }

    public async Task<bool> FinishAsync()
    {
        if (!AcceptedPrivacyPolicy || !AcceptedGpl || (IsVerificationNoticeRequired && !AcceptedVerificationNotice))
        {
            if (!IsPrivacyPolicyOnly)
                SelectedStep = 1;
            StatusMessage = LR.M_CompletionAgreementRequired;
            return false;
        }

        if (!IsPrivacyPolicyOnly && !ApplyDesktopIntegration())
            StatusMessage = LR.M_DesktopIntegrationFailed;

        _oobeService.Complete();
        await Task.CompletedTask;
        return true;
    }

    public void ImportStudents(IReadOnlyList<Student> students)
    {
        _dataSetupService.SaveStudentList(SelectedStudentListName, students);
        RefreshListSelectors();
        OnPropertyChanged(nameof(SelectedStudentListCount));
        StatusMessage = string.Format(LR.M_StudentsImported, students.Count);
    }

    public void ImportPrizes(IReadOnlyList<Prize> prizes)
    {
        _dataSetupService.SavePrizeList(SelectedPrizeListName, prizes);
        RefreshListSelectors();
        OnPropertyChanged(nameof(SelectedPrizeListCount));
        StatusMessage = string.Format(LR.M_PrizesImported, prizes.Count);
    }

    public void RefreshFromConfig()
    {
        if (_appearanceSettings is not null)
            _appearanceSettings.PropertyChanged -= RefreshAppearance;
        if (_basicSettings is not null)
            _basicSettings.PropertyChanged -= PersistSettingsOnPropertyChanged;
        if (_privacySettings is not null)
            _privacySettings.PropertyChanged -= PersistSettingsOnPropertyChanged;
        if (_floatingWindowSettings is not null)
            _floatingWindowSettings.PropertyChanged -= PersistSettingsOnPropertyChanged;
        if (_moreSettings is not null)
            _moreSettings.PropertyChanged -= PersistSettingsOnPropertyChanged;
        _appearanceSettings = _configHandler.Data.Appearance;
        _basicSettings = _configHandler.Data.General.Basic;
        _privacySettings = _configHandler.Data.General.PrivacySettings;
        _floatingWindowSettings = _configHandler.Data.FloatingWindowSettings;
        _moreSettings = _configHandler.Data.MoreSettings;
        Autostart = _basicSettings.Autostart;
        ExternalIntegration = _basicSettings.UrlProtocol;
        RefreshListSelectors();
        RefreshControlNodeState();
        if (IsPrivacyPolicyOnly)
            SelectedStep = 1;
        OnPropertyChanged(nameof(Basic));
        OnPropertyChanged(nameof(PrivacySettings));
        OnPropertyChanged(nameof(IsPrivacyPolicyOnly));
        OnPropertyChanged(nameof(IsFullSetup));
        OnPropertyChanged(nameof(IsVerificationNoticeRequired));
        OnPropertyChanged(nameof(IsPrivacyPolicyStep));
        OnPropertyChanged(nameof(IsStatusVisible));
        OnPropertyChanged(nameof(IsCompletionActionVisible));
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(IntroText));
        OnPropertyChanged(nameof(Appearance));
        OnPropertyChanged(nameof(FloatingWindow));
        OnPropertyChanged(nameof(MoreSettings));
        _appearanceSettings.PropertyChanged += RefreshAppearance;
        _appearanceSettings.PropertyChanged += PersistSettingsOnPropertyChanged;
        _basicSettings.PropertyChanged += PersistSettingsOnPropertyChanged;
        _privacySettings.PropertyChanged += PersistSettingsOnPropertyChanged;
        _floatingWindowSettings.PropertyChanged += PersistSettingsOnPropertyChanged;
        _moreSettings.PropertyChanged += PersistSettingsOnPropertyChanged;
    }

    private void PersistSettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _configHandler.Save();
    }

    /// <summary>把本机集控状态投影到界面（写入过程要被抑制，否则会回环写一遍）。</summary>
    private void RefreshControlNodeState()
    {
        var state = _controlNodeStateStore.Current;
        _suppressControlPersist = true;
        try
        {
            ControlRemoteEnabled = state.RemoteControlEnabled;
            ControlDisplayName = state.DisplayName ?? string.Empty;
            ControlGroupId = state.GroupId;
            ControlNodeId = state.NodeId;
        }
        finally
        {
            _suppressControlPersist = false;
        }

        RefreshOpenControlPlaneLabel();
    }

    /// <summary>
    ///     "打开集控平台"按钮文案：配了第三方地址就明说打开的是哪一家。
    /// </summary>
    /// <remarks>
    ///     按钮写着官方却跳到别人的服务器，是页面上最容易被误点的一处；语言切换也要重算一次，
    ///     因为它是一段在 ViewModel 里定格的本地化文案，不像 <c>x:Static</c> 会随新窗口重新求值。
    /// </remarks>
    private void RefreshOpenControlPlaneLabel() =>
        OpenControlPlaneLabel = _controlPlaneEndpointStore.IsCustom
            ? LR.C_OpenControlPlane_ThirdParty
            : LR.C_OpenControlPlane_Official;

    public void RefreshListSelectors()
    {
        RefreshListSelector(
            StudentListNames,
            _catalogManager.GetStudentListNames(),
            _configHandler.Data.RollCallSettings.DefaultClass,
            name => _catalogManager.CreateStudentList(name),
            name => SelectedStudentListName = name);
        RefreshListSelector(
            PrizeListNames,
            _catalogManager.GetPrizeListNames(),
            _configHandler.Data.LotterySettings.DefaultPool,
            name => _catalogManager.CreatePrizeList(name),
            name => SelectedPrizeListName = name);
    }

    private static void RefreshListSelector(
        ObservableCollection<string> names,
        IReadOnlyList<string> existingNames,
        string preferredName,
        Action<string> createDefault,
        Action<string> select)
    {
        names.Clear();
        foreach (var name in existingNames)
            names.Add(name);

        if (names.Count == 0)
        {
            const string defaultName = "default";
            createDefault(defaultName);
            names.Add(defaultName);
        }

        select(names.Contains(preferredName) ? preferredName : names[0]);
    }

    public void CreateStudentList(string name)
    {
        _dataSetupService.CreateStudentList(name);
        RefreshListSelectors();
        SelectedStudentListName = name;
    }

    public void CreatePrizeList(string name)
    {
        _dataSetupService.CreatePrizeList(name);
        RefreshListSelectors();
        SelectedPrizeListName = name;
    }

    public void RenameStudentList(string newName)
    {
        _dataSetupService.RenameStudentList(SelectedStudentListName, newName);
        RefreshListSelectors();
    }

    public void RenamePrizeList(string newName)
    {
        _dataSetupService.RenamePrizeList(SelectedPrizeListName, newName);
        RefreshListSelectors();
    }

    public void DeleteStudentList()
    {
        _dataSetupService.DeleteStudentList(SelectedStudentListName);
        RefreshListSelectors();
    }

    public void DeletePrizeList()
    {
        _dataSetupService.DeletePrizeList(SelectedPrizeListName);
        RefreshListSelectors();
    }

    public void RefreshAppearance(object? sender = null, PropertyChangedEventArgs? e = null)
    {
        App.Current.RefreshPersonalizedSettings();
    }

    private bool ApplyDesktopIntegration()
    {
        var basic = _configHandler.Data.General.Basic;
        var succeeded = true;
        if (Autostart != basic.Autostart)
        {
            if (_desktopIntegration.TrySetAutostart(Autostart, out _))
                basic.Autostart = Autostart;
            else
            {
                Autostart = false;
                basic.Autostart = false;
                succeeded = false;
            }
        }

        if (ExternalIntegration != basic.UrlProtocol)
        {
            if (_desktopIntegration.TrySetUrlProtocol(ExternalIntegration, out _))
                basic.UrlProtocol = ExternalIntegration;
            else
            {
                ExternalIntegration = false;
                basic.UrlProtocol = false;
                succeeded = false;
            }
        }

        _configHandler.Save();
        return succeeded;
    }

    public void Dispose()
    {
        if (_appearanceSettings is not null)
        {
            _appearanceSettings.PropertyChanged -= RefreshAppearance;
            _appearanceSettings.PropertyChanged -= PersistSettingsOnPropertyChanged;
        }
        if (_basicSettings is not null)
            _basicSettings.PropertyChanged -= PersistSettingsOnPropertyChanged;
        if (_privacySettings is not null)
            _privacySettings.PropertyChanged -= PersistSettingsOnPropertyChanged;
        if (_floatingWindowSettings is not null)
            _floatingWindowSettings.PropertyChanged -= PersistSettingsOnPropertyChanged;
        if (_moreSettings is not null)
            _moreSettings.PropertyChanged -= PersistSettingsOnPropertyChanged;
    }
}
