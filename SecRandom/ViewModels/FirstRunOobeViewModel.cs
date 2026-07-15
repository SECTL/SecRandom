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

    [ObservableProperty] private int _selectedStep;
    [ObservableProperty] private bool _acceptedUserAgreement;
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
    }

    public AppearanceSettingsConfig Appearance => _configHandler.Data.Appearance;
    public BasicSettingsConfig Basic => _configHandler.Data.General.Basic;
    public FloatingWindowSettingsConfig FloatingWindow => _configHandler.Data.FloatingWindowSettings;
    public MoreSettingsConfig MoreSettings => _configHandler.Data.MoreSettings;
    public ObservableCollection<string> StudentListNames { get; } = [];
    public ObservableCollection<string> PrizeListNames { get; } = [];
    public bool IsWelcomeStep => SelectedStep == 0;
    public bool HasPrevious => SelectedStep > 0;
    public bool IsFinalStep => SelectedStep == StepCount - 1;
    public string NextButtonText => IsWelcomeStep ? LR.C_Start : IsFinalStep ? LR.C_Finish : LR.C_Next;
    public string StepProgress => string.Format(LR.M_StepProgress, SelectedStep, StepCount - 1);
    public bool CanContinue => SelectedStep != 1 || (AcceptedUserAgreement && AcceptedGpl);
    public int StepCount => 7;

    public bool SetLanguage(LanguageMode language)
    {
        if (Basic.Language == language)
            return false;

        Basic.Language = language;
        _configHandler.Save();
        StatusMessage = string.Empty;
        OnPropertyChanged(nameof(NextButtonText));
        OnPropertyChanged(nameof(StepProgress));
        return true;
    }

    public void RefreshLocalizedText()
    {
        StatusMessage = string.Empty;
        OnPropertyChanged(nameof(NextButtonText));
        OnPropertyChanged(nameof(StepProgress));
    }

    partial void OnSelectedStepChanged(int value)
    {
        OnPropertyChanged(nameof(IsWelcomeStep));
        OnPropertyChanged(nameof(HasPrevious));
        OnPropertyChanged(nameof(IsFinalStep));
        OnPropertyChanged(nameof(NextButtonText));
        OnPropertyChanged(nameof(StepProgress));
        OnPropertyChanged(nameof(CanContinue));
    }

    partial void OnAcceptedUserAgreementChanged(bool value) => OnPropertyChanged(nameof(CanContinue));
    partial void OnAcceptedGplChanged(bool value) => OnPropertyChanged(nameof(CanContinue));

    partial void OnSelectedStudentListNameChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        _profileService.LoadStudentProfile(value, saveCurrent: false);
        _configHandler.Data.RollCallSettings.DefaultClass = value;
        _configHandler.Save();
    }

    partial void OnSelectedPrizeListNameChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        _profileService.LoadPrizeProfile(value, saveCurrent: false);
        _configHandler.Data.LotterySettings.DefaultPool = value;
        _configHandler.Save();
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
        if (!AcceptedUserAgreement || !AcceptedGpl)
        {
            SelectedStep = 1;
            StatusMessage = LR.M_CompletionAgreementRequired;
            return false;
        }

        if (!ApplyDesktopIntegration())
            StatusMessage = LR.M_DesktopIntegrationFailed;

        _oobeService.Complete();
        await Task.CompletedTask;
        return true;
    }

    public void ImportStudents(IReadOnlyList<Student> students)
    {
        _dataSetupService.SaveStudentList(SelectedStudentListName, students);
        RefreshListSelectors();
        StatusMessage = string.Format(LR.M_StudentsImported, students.Count);
    }

    public void ImportPrizes(IReadOnlyList<Prize> prizes)
    {
        _dataSetupService.SavePrizeList(SelectedPrizeListName, prizes);
        RefreshListSelectors();
        StatusMessage = string.Format(LR.M_PrizesImported, prizes.Count);
    }

    public void RefreshFromConfig()
    {
        if (_appearanceSettings is not null)
            _appearanceSettings.PropertyChanged -= RefreshAppearance;
        _appearanceSettings = _configHandler.Data.Appearance;
        Autostart = _configHandler.Data.General.Basic.Autostart;
        ExternalIntegration = _configHandler.Data.General.Basic.UrlProtocol;
        RefreshListSelectors();
        OnPropertyChanged(nameof(Appearance));
        OnPropertyChanged(nameof(FloatingWindow));
        OnPropertyChanged(nameof(MoreSettings));
        _appearanceSettings.PropertyChanged += RefreshAppearance;
    }

    private void RefreshListSelectors()
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
            _appearanceSettings.PropertyChanged -= RefreshAppearance;
    }
}
