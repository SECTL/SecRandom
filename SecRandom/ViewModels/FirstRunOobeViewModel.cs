using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Models.SubConfigs.Personalized;
using SecRandom.Core.Services.Config;
using SecRandom.Services.Desktop;
using SecRandom.Services.FirstRun;
using SecRandom.Shared.Models.Profile;
using LR = SecRandom.Langs.FirstRunOobe.Resources;

namespace SecRandom.ViewModels;

public sealed partial class FirstRunOobeViewModel : ViewModelBase, IDisposable
{
    private readonly MainConfigHandler _configHandler;
    private readonly FirstRunOobeService _oobeService;
    private readonly OobeDataSetupService _dataSetupService;
    private readonly DesktopIntegrationService _desktopIntegration;
    private AppearanceSettingsConfig? _appearanceSettings;

    [ObservableProperty] private int _selectedStep;
    [ObservableProperty] private bool _acceptedUserAgreement;
    [ObservableProperty] private bool _acceptedGpl;
    [ObservableProperty] private string _className = LR.C_DefaultClassName;
    [ObservableProperty] private string _prizePoolName = LR.C_DefaultPrizePoolName;
    [ObservableProperty] private bool _autostart;
    [ObservableProperty] private bool _externalIntegration;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public FirstRunOobeViewModel(
        MainConfigHandler configHandler,
        FirstRunOobeService oobeService,
        OobeDataSetupService dataSetupService,
        DesktopIntegrationService desktopIntegration) : base(configHandler)
    {
        _configHandler = configHandler;
        _oobeService = oobeService;
        _dataSetupService = dataSetupService;
        _desktopIntegration = desktopIntegration;
        RefreshFromConfig();
    }

    public AppearanceSettingsConfig Appearance => _configHandler.Data.Appearance;
    public FloatingWindowSettingsConfig FloatingWindow => _configHandler.Data.FloatingWindowSettings;
    public MoreSettingsConfig MoreSettings => _configHandler.Data.MoreSettings;
    public bool HasPrevious => SelectedStep > 0;
    public bool IsFinalStep => SelectedStep == StepCount - 1;
    public string NextButtonText => IsFinalStep ? LR.C_Finish : LR.C_Next;
    public string StepProgress => string.Format(LR.M_StepProgress, SelectedStep + 1, StepCount);
    public bool CanContinue => SelectedStep != 1 || (AcceptedUserAgreement && AcceptedGpl);
    public int StepCount => 7;

    partial void OnSelectedStepChanged(int value)
    {
        OnPropertyChanged(nameof(HasPrevious));
        OnPropertyChanged(nameof(IsFinalStep));
        OnPropertyChanged(nameof(NextButtonText));
        OnPropertyChanged(nameof(StepProgress));
        OnPropertyChanged(nameof(CanContinue));
    }

    partial void OnAcceptedUserAgreementChanged(bool value) => OnPropertyChanged(nameof(CanContinue));
    partial void OnAcceptedGplChanged(bool value) => OnPropertyChanged(nameof(CanContinue));

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
        _dataSetupService.SaveStudentList(ClassName, students);
        StatusMessage = string.Format(LR.M_StudentsImported, students.Count);
    }

    public void ImportPrizes(IReadOnlyList<Prize> prizes)
    {
        _dataSetupService.SavePrizeList(PrizePoolName, prizes);
        StatusMessage = string.Format(LR.M_PrizesImported, prizes.Count);
    }

    public void RefreshFromConfig()
    {
        if (_appearanceSettings is not null)
            _appearanceSettings.PropertyChanged -= RefreshAppearance;
        _appearanceSettings = _configHandler.Data.Appearance;
        Autostart = _configHandler.Data.General.Basic.Autostart;
        ExternalIntegration = _configHandler.Data.General.Basic.UrlProtocol;
        OnPropertyChanged(nameof(Appearance));
        OnPropertyChanged(nameof(FloatingWindow));
        OnPropertyChanged(nameof(MoreSettings));
        _appearanceSettings.PropertyChanged += RefreshAppearance;
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
