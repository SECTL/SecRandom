using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Models.SubConfigs.General;
using SecRandom.Core.Models.SubConfigs.Personalized;
using SecRandom.Core.Services.Config;
using SecRandom.Services.Desktop;
using SecRandom.Services.FirstRun;
using SecRandom.Shared;
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
    private AppearanceSettingsConfig? _appearanceSettings;
    private BasicSettingsConfig? _basicSettings;
    private PrivacySettingsConfig? _privacySettings;
    private FloatingWindowSettingsConfig? _floatingWindowSettings;
    private MoreSettingsConfig? _moreSettings;

    [ObservableProperty] private int _selectedStep;
    [ObservableProperty] private bool _acceptedPrivacyPolicy;
    [ObservableProperty] private bool _acceptedGpl;
    [ObservableProperty] private string _selectedStudentListName = string.Empty;
    [ObservableProperty] private string _selectedPrizeListName = string.Empty;
    [ObservableProperty] private bool _autostart;
    [ObservableProperty] private bool _externalIntegration;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public FirstRunOobeViewModel(
        MainConfigHandler configHandler,
        FirstRunOobeService oobeService,
        OobeDataSetupService dataSetupService,
        DesktopIntegrationService desktopIntegration,
        IProfileService profileService) : base(configHandler)
    {
        _configHandler = configHandler;
        _oobeService = oobeService;
        _dataSetupService = dataSetupService;
        _desktopIntegration = desktopIntegration;
        _profileService = profileService;
        RefreshFromConfig();
        SelectedStep = IsPrivacyPolicyOnly ? 1 : 0;
    }

    public AppearanceSettingsConfig Appearance => _configHandler.Data.Appearance;
    public BasicSettingsConfig Basic => _configHandler.Data.General.Basic;
    public PrivacySettingsConfig PrivacySettings => _configHandler.Data.General.PrivacySettings;
    public FloatingWindowSettingsConfig FloatingWindow => _configHandler.Data.FloatingWindowSettings;
    public MoreSettingsConfig MoreSettings => _configHandler.Data.MoreSettings;
    public ObservableCollection<string> StudentListNames { get; } = [];
    public ObservableCollection<string> PrizeListNames { get; } = [];
    public int SelectedStudentListCount => _profileService.CurrentStudentList?.Students.Count ?? 0;
    public int SelectedPrizeListCount => _profileService.CurrentPrizeList?.Prizes.Count ?? 0;
    public bool IsPrivacyPolicyOnly => _oobeService.IsPrivacyPolicyOnlyRequired();
    public bool IsFullSetup => !IsPrivacyPolicyOnly;
    public bool IsWelcomeStep => !IsPrivacyPolicyOnly && SelectedStep == 0;
    public bool HasPrevious => !IsPrivacyPolicyOnly && SelectedStep > 0;
    public bool IsStatusVisible => HasPrevious || IsPrivacyPolicyOnly;
    public bool IsCompletionActionVisible => HasPrevious || IsPrivacyPolicyOnly;
    public bool IsFinalStep => IsPrivacyPolicyOnly || SelectedStep == StepCount - 1;
    public string NextButtonText => IsWelcomeStep ? LR.C_Start : IsFinalStep ? LR.C_Finish : LR.C_Next;
    public string StepProgress => IsPrivacyPolicyOnly
        ? LR.C_LegalTitle
        : string.Format(LR.M_StepProgress, SelectedStep, StepCount - 1);
    public bool CanContinue => !IsPrivacyPolicyStep || (AcceptedPrivacyPolicy && AcceptedGpl);
    public bool IsPrivacyPolicyStep => IsPrivacyPolicyOnly || SelectedStep == 1;
    public int StepCount => 8;
    public string PageTitle => IsPrivacyPolicyOnly ? LR.C_LegalTitle : LR.C_Title;
    public string IntroText => IsPrivacyPolicyOnly ? LR.C_LegalDescription : LR.C_Intro;

    public bool SetLanguage(LanguageMode language)
    {
        if (Basic.Language == language)
            return false;

        Basic.Language = language;
        _configHandler.Save();
        StatusMessage = string.Empty;
        OnPropertyChanged(nameof(IsPrivacyPolicyOnly));
        OnPropertyChanged(nameof(IsFullSetup));
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
        if (!AcceptedPrivacyPolicy || !AcceptedGpl)
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
        if (IsPrivacyPolicyOnly)
            SelectedStep = 1;
        OnPropertyChanged(nameof(Basic));
        OnPropertyChanged(nameof(PrivacySettings));
        OnPropertyChanged(nameof(IsPrivacyPolicyOnly));
        OnPropertyChanged(nameof(IsFullSetup));
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

    public void RefreshListSelectors()
    {
        RefreshListSelector(
            StudentListNames,
            Utils.GetDirectoryPath("list", "roll_call_list"),
            _configHandler.Data.RollCallSettings.DefaultClass,
            name => new StudentListConfig(name).Save(),
            name => SelectedStudentListName = name);
        RefreshListSelector(
            PrizeListNames,
            Utils.GetDirectoryPath("list", "lottery_list"),
            _configHandler.Data.LotterySettings.DefaultPool,
            name => new PrizeListConfig(name).Save(),
            name => SelectedPrizeListName = name);
    }

    private static void RefreshListSelector(
        ObservableCollection<string> names,
        string directory,
        string preferredName,
        Action<string> createDefault,
        Action<string> select)
    {
        names.Clear();
        foreach (var file in Directory.GetFiles(directory, "*.json").OrderBy(Path.GetFileName))
            names.Add(Path.GetFileNameWithoutExtension(file));

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
