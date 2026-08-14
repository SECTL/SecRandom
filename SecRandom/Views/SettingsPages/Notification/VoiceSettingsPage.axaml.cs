using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;
using SecRandom.Services.Voice;
using SecRandom.Shared.Models.Profile;
using SecRandom.ViewModels;
using MobileResources = SecRandom.Langs.Mobile.Resources;
using PluginResources = SecRandom.Langs.SettingsPages.Plugins.Overview.Resources;

namespace SecRandom.Views.SettingsPages.Notification;

[PageInfo("settings.notification.voiceMusic", FluentIcons.PersonVoiceFilled, "settings.notification")]
public partial class VoiceSettingsPage : UserControl, INotifyPropertyChanged
{
    private bool _isLoadingSystemVoices;
    private bool _isLoadingEdgeVoices;
    private bool _isBatchRunning;
    private BatchRosterOption? _selectedBatchRoster;
    private ClearRosterOption? _selectedClearRoster;
    private string _batchProgressText = string.Empty;
    private int _batchProgressRevision;
    private string _omniTtsApiKeyDraft = string.Empty;
    private string _omniTtsModelInput = string.Empty;
    private string _omniTtsVoiceInput = string.Empty;
    private string _testVoiceText = Langs.SettingsPages.Voice.Resources.C_TestVoiceText;
    private readonly Dictionary<OmniTtsProvider, OmniTtsProviderSelection> _omniTtsProviderSelections = [];
    private CancellationTokenSource? _batchCts;
    private bool _suppressSettingsSave;
    private bool _isSettingsSubscribed;
    private int _omniTtsProviderOptionsVersion;
    private event PropertyChangedEventHandler? NotifyPropertyChanged;

    private const int BatchSourceStudents = 1;
    private const int BatchSourcePrizes = 2;
    private const double OmniTtsDialogFieldWidth = 320;
    private const double OmniTtsDialogActionWidth = 112;

    public VoiceSettingsPage()
    {
        Settings = ViewModel.Config.VoiceSettings;
        RememberOmniTtsProviderSelection();
        if (!OperatingSystem.IsWindows() && Settings.VoiceEngine == 0)
        {
            Settings.VoiceEngine = EdgeTtsSpeechProvider.EdgeEngine;
            ConfigHandler.Save();
        }

        DataContext = this;
        InitializeComponent();
        RefreshBatchRosterOptions();
        RefreshClearRosterOptions();
        _ = RefreshVoicesAsync();
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public VoiceSettingsConfig Settings { get; }
    public ObservableCollection<VoiceEngineOption> VoiceEngineOptions { get; } =
        OperatingSystem.IsWindows()
            ?
            [
                new(0, Langs.SettingsPages.Voice.Resources.O_VoiceEngine_System),
                new(EdgeTtsSpeechProvider.EdgeEngine, Langs.SettingsPages.Voice.Resources.O_VoiceEngine_EdgeTts),
                new(OmniTtsSpeechProvider.OmniEngine, Langs.SettingsPages.Voice.Resources.O_VoiceEngine_OmniTts)
            ]
            :
            [
                new(EdgeTtsSpeechProvider.EdgeEngine, Langs.SettingsPages.Voice.Resources.O_VoiceEngine_EdgeTts),
                new(OmniTtsSpeechProvider.OmniEngine, Langs.SettingsPages.Voice.Resources.O_VoiceEngine_OmniTts)
            ];

    public ObservableCollection<VoiceOption> SystemTtsVoices { get; } = [];
    public ObservableCollection<VoiceOption> EdgeTtsVoices { get; } = [];
    public ObservableCollection<string> OmniTtsModelOptions { get; } = [];
    public ObservableCollection<string> OmniTtsVoiceOptions { get; } = [];
    public ObservableCollection<BatchRosterOption> BatchRosterOptions { get; } = [];
    public ObservableCollection<ClearRosterOption> ClearRosterOptions { get; } = [];

    public ObservableCollection<OmniTtsProviderOption> OmniTtsProviderOptions { get; } =
    [
        new(OmniTtsProvider.OpenAi, Langs.SettingsPages.Voice.Resources.O_OmniTtsProvider_OpenAi),
        new(OmniTtsProvider.FishAudio, Langs.SettingsPages.Voice.Resources.O_OmniTtsProvider_FishAudio),
        new(OmniTtsProvider.MiMo, Langs.SettingsPages.Voice.Resources.O_OmniTtsProvider_MiMo),
        new(OmniTtsProvider.Gemini, "Gemini"),
        new(OmniTtsProvider.Custom, Langs.SettingsPages.Voice.Resources.O_OmniTtsProvider_Custom)
    ];

    public bool IsSystemTtsSelected => OperatingSystem.IsWindows() && Settings.VoiceEngine == 0;
    public bool IsEdgeTtsSelected => Settings.VoiceEngine == EdgeTtsSpeechProvider.EdgeEngine;
    public bool IsOmniTtsSelected => Settings.VoiceEngine == OmniTtsSpeechProvider.OmniEngine;

    public bool IsMiMoVoiceCloneSelected => IsOmniTtsSelected &&
        Settings.OmniTtsProvider == OmniTtsProvider.MiMo &&
        OmniTtsSpeechProvider.IsMiMoVoiceCloneModel(Settings.OmniTtsModel);

    public bool IsMiMoVoiceDesignSelected => IsOmniTtsSelected &&
        Settings.OmniTtsProvider == OmniTtsProvider.MiMo &&
        OmniTtsSpeechProvider.IsMiMoVoiceDesignModel(Settings.OmniTtsModel);

    public bool IsMiMoSpecialVoiceSelected => IsMiMoVoiceCloneSelected || IsMiMoVoiceDesignSelected;

    public bool MiMoVoiceCloneReferenceConfigured => MiMoVoiceReferenceStore.HasReference();

    public string MiMoVoiceCloneStatus => MiMoVoiceCloneReferenceConfigured
        ? Langs.SettingsPages.Voice.MiMoResources.M_MiMoVoiceCloneConfigured
        : Langs.SettingsPages.Voice.MiMoResources.M_MiMoVoiceCloneNotConfigured;

    public bool CanRefreshOmniTtsVoices => Settings.OmniTtsProvider is
        OmniTtsProvider.FishAudio or OmniTtsProvider.Custom;

    public double OmniTtsVoiceSelectorWidth => CanRefreshOmniTtsVoices ? 200 : 320;

    public OmniTtsProvider SelectedOmniTtsProvider
    {
        get => Settings.OmniTtsProvider;
        set
        {
            if (Settings.OmniTtsProvider != value)
                ApplyOmniTtsProvider(value, OmniTtsSpeechProvider.GetDefaultBaseUrl(value));
        }
    }

    public string OmniTtsApiKeyDraft
    {
        get => _omniTtsApiKeyDraft;
        set
        {
            if (string.Equals(_omniTtsApiKeyDraft, value, StringComparison.Ordinal))
                return;

            _omniTtsApiKeyDraft = value;
            OnPropertyChanged();
        }
    }

    public string OmniTtsModelInput
    {
        get => _omniTtsModelInput;
        set
        {
            if (string.Equals(_omniTtsModelInput, value, StringComparison.Ordinal))
                return;

            _omniTtsModelInput = value;
            OnPropertyChanged();
        }
    }

    public string OmniTtsVoiceInput
    {
        get => _omniTtsVoiceInput;
        set
        {
            if (string.Equals(_omniTtsVoiceInput, value, StringComparison.Ordinal))
                return;

            _omniTtsVoiceInput = value;
            OnPropertyChanged();
        }
    }

    public string TestVoiceText
    {
        get => _testVoiceText;
        set
        {
            if (string.Equals(_testVoiceText, value, StringComparison.Ordinal))
                return;

            _testVoiceText = value;
            OnPropertyChanged();
        }
    }

    public string OmniTtsKeyStatus => CredentialStore.HasKey(SelectedOmniTtsProvider)
        ? Langs.SettingsPages.Voice.Resources.M_OmniTtsKeyConfigured
        : Langs.SettingsPages.Voice.Resources.M_OmniTtsKeyNotConfigured;

    public BatchRosterOption? SelectedBatchRoster
    {
        get => _selectedBatchRoster;
        set
        {
            if (ReferenceEquals(_selectedBatchRoster, value))
                return;

            _selectedBatchRoster = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanStartBatch));
        }
    }

    public ClearRosterOption? SelectedClearRoster
    {
        get => _selectedClearRoster;
        set
        {
            if (ReferenceEquals(_selectedClearRoster, value))
                return;

            _selectedClearRoster = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanClearCache));
        }
    }

    public bool IsBatchRunning
    {
        get => _isBatchRunning;
        private set
        {
            if (_isBatchRunning == value)
                return;

            _isBatchRunning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanStartBatch));
            OnPropertyChanged(nameof(CanSelectBatchRoster));
        }
    }

    public bool CanSelectBatchRoster => !IsBatchRunning;

    public bool CanStartBatch => !IsBatchRunning &&
                                 SelectedBatchRoster is not null &&
                                 (!IsOmniTtsSelected ||
                                  (IsMiMoVoiceCloneSelected
                                      ? MiMoVoiceCloneReferenceConfigured
                                      : IsMiMoVoiceDesignSelected
                                          ? !string.IsNullOrWhiteSpace(Settings.MiMoVoiceDesignPrompt)
                                          : !string.IsNullOrWhiteSpace(Settings.OmniTtsModel) &&
                                            !string.IsNullOrWhiteSpace(Settings.OmniTtsVoiceId)));

    public bool HasClearCacheOptions => ClearRosterOptions.Count > 0;

    public bool CanClearCache => SelectedClearRoster is not null;

    public string BatchProgressText
    {
        get => _batchProgressText;
        private set
        {
            if (string.Equals(_batchProgressText, value, StringComparison.Ordinal))
                return;

            _batchProgressText = value;
            OnPropertyChanged();
        }
    }

    private MainConfigHandler ConfigHandler { get; } = IAppHost.GetService<MainConfigHandler>();
    private VoiceAnnouncementService VoiceService { get; } = IAppHost.GetService<VoiceAnnouncementService>();
    private IOmniTtsCatalog OmniTtsCatalog { get; } = IAppHost.GetService<IOmniTtsCatalog>();
    private OmniTtsCredentialStore CredentialStore { get; } = IAppHost.GetService<OmniTtsCredentialStore>();
    private MiMoVoiceReferenceStore MiMoVoiceReferenceStore { get; } = IAppHost.GetService<MiMoVoiceReferenceStore>();
    private IProfileCatalogManager ProfileCatalogManager { get; } = IAppHost.GetService<IProfileCatalogManager>();

    public bool IsLoadingSystemVoices
    {
        get => _isLoadingSystemVoices;
        private set
        {
            if (_isLoadingSystemVoices == value)
                return;

            _isLoadingSystemVoices = value;
            OnPropertyChanged();
        }
    }

    public bool IsLoadingEdgeVoices
    {
        get => _isLoadingEdgeVoices;
        private set
        {
            if (_isLoadingEdgeVoices == value)
                return;

            _isLoadingEdgeVoices = value;
            OnPropertyChanged();
        }
    }

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => NotifyPropertyChanged += value;
        remove => NotifyPropertyChanged -= value;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_isSettingsSubscribed)
            return;

        Settings.PropertyChanged += SettingsOnPropertyChanged;
        _isSettingsSubscribed = true;
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        _batchCts?.Cancel();

        if (!_isSettingsSubscribed)
            return;

        Settings.PropertyChanged -= SettingsOnPropertyChanged;
        _isSettingsSubscribed = false;
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VoiceSettingsConfig.VoiceEngine))
        {
            OnPropertyChanged(nameof(IsSystemTtsSelected));
            OnPropertyChanged(nameof(IsEdgeTtsSelected));
            OnPropertyChanged(nameof(IsOmniTtsSelected));
            OnPropertyChanged(nameof(IsMiMoVoiceCloneSelected));
            OnPropertyChanged(nameof(IsMiMoVoiceDesignSelected));
            OnPropertyChanged(nameof(IsMiMoSpecialVoiceSelected));
            OnPropertyChanged(nameof(MiMoVoiceCloneStatus));
        }

        if (e.PropertyName is nameof(VoiceSettingsConfig.VoiceEngine) or
            nameof(VoiceSettingsConfig.OmniTtsModel) or
            nameof(VoiceSettingsConfig.OmniTtsVoiceId) or
            nameof(VoiceSettingsConfig.MiMoVoiceDesignPrompt) or
            nameof(VoiceSettingsConfig.MiMoVoiceCloneReferenceHash))
        {
            OnPropertyChanged(nameof(CanStartBatch));
            OnPropertyChanged(nameof(IsMiMoVoiceCloneSelected));
            OnPropertyChanged(nameof(IsMiMoVoiceDesignSelected));
            OnPropertyChanged(nameof(IsMiMoSpecialVoiceSelected));
        }

        if (e.PropertyName is nameof(VoiceSettingsConfig.VoiceEngine) or
            nameof(VoiceSettingsConfig.SystemTtsVoiceName) or
            nameof(VoiceSettingsConfig.EdgeTtsVoiceName) or
            nameof(VoiceSettingsConfig.OmniTtsVoiceId))
            RefreshClearRosterOptions();

        if (!_suppressSettingsSave)
            ConfigHandler.Save();
    }

    private void ApplyOmniTtsProvider(OmniTtsProvider provider, string baseUrl)
    {
        var providerChanged = Settings.OmniTtsProvider != provider;
        if (providerChanged)
            RememberOmniTtsProviderSelection();

        var optionsVersion = ++_omniTtsProviderOptionsVersion;
        SaveOmniTtsSettings(() =>
        {
            Settings.OmniTtsProvider = provider;
            Settings.OmniTtsApiBaseUrl = baseUrl;
            if (providerChanged)
                RestoreOmniTtsProviderSelection(provider);
        });

        OnPropertyChanged(nameof(SelectedOmniTtsProvider));
        OnPropertyChanged(nameof(OmniTtsKeyStatus));
        OnPropertyChanged(nameof(CanRefreshOmniTtsVoices));
        OnPropertyChanged(nameof(OmniTtsVoiceSelectorWidth));
        OnPropertyChanged(nameof(IsMiMoVoiceCloneSelected));
        OnPropertyChanged(nameof(IsMiMoVoiceDesignSelected));
        OnPropertyChanged(nameof(IsMiMoSpecialVoiceSelected));
        OnPropertyChanged(nameof(CanStartBatch));
        _ = RefreshOmniTtsProviderOptionsAsync(provider, optionsVersion);
    }

    private void RememberOmniTtsProviderSelection()
    {
        _omniTtsProviderSelections[Settings.OmniTtsProvider] = new OmniTtsProviderSelection(
            Settings.OmniTtsModel,
            Settings.OmniTtsVoiceId);
    }

    private void RestoreOmniTtsProviderSelection(OmniTtsProvider provider)
    {
        if (_omniTtsProviderSelections.TryGetValue(provider, out var selection))
        {
            Settings.OmniTtsModel = selection.Model;
            Settings.OmniTtsVoiceId = selection.VoiceId;
            return;
        }

        Settings.OmniTtsModel = string.Empty;
        Settings.OmniTtsVoiceId = string.Empty;
    }

    private async Task RefreshOmniTtsProviderOptionsAsync(
        OmniTtsProvider provider,
        int optionsVersion)
    {
        await RefreshOmniTtsModelOptionsAsync(
            selectFirstWhenMissing: true,
            expectedProvider: provider,
            expectedOptionsVersion: optionsVersion);
        await RefreshOmniTtsVoiceOptionsAsync(
            selectFirstWhenMissing: true,
            expectedProvider: provider,
            expectedOptionsVersion: optionsVersion);
    }

    private async Task<int> RefreshOmniTtsModelOptionsAsync(
        bool throwOnFailure = false,
        bool selectFirstWhenMissing = false,
        OmniTtsProvider? expectedProvider = null,
        int? expectedOptionsVersion = null)
    {
        IReadOnlyList<string> fetchedModels;
        try
        {
            fetchedModels = await OmniTtsCatalog.GetModelsAsync();
        }
        catch
        {
            if (throwOnFailure)
                throw;

            fetchedModels = [];
        }

        if (!IsCurrentOmniTtsProvider(expectedProvider, expectedOptionsVersion))
            return 0;

        var models = fetchedModels
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var selectedModel = Settings.OmniTtsModel;

        OmniTtsModelOptions.Clear();
        foreach (var model in models)
            OmniTtsModelOptions.Add(model);

        if (models.Count > 0 &&
            selectFirstWhenMissing &&
            !models.Contains(selectedModel, StringComparer.Ordinal))
        {
            Settings.OmniTtsModel = models[0];
        }
        else if (models.Count == 0)
        {
            EnsureOption(OmniTtsModelOptions, selectedModel);
        }

        return models.Count;
    }

    private bool IsCurrentOmniTtsProvider(
        OmniTtsProvider? expectedProvider,
        int? expectedOptionsVersion) =>
        (!expectedProvider.HasValue || Settings.OmniTtsProvider == expectedProvider.Value) &&
        (!expectedOptionsVersion.HasValue || _omniTtsProviderOptionsVersion == expectedOptionsVersion.Value);

    private async Task RefreshVoicesAsync()
    {
        var omniTtsProvider = Settings.OmniTtsProvider;
        var optionsVersion = _omniTtsProviderOptionsVersion;

        if (OperatingSystem.IsWindows())
        {
            try
            {
                IsLoadingSystemVoices = true;
                ReplaceVoices(SystemTtsVoices, await VoiceService.GetVoicesAsync(0));
            }
            finally
            {
                IsLoadingSystemVoices = false;
            }
        }

        try
        {
            IsLoadingEdgeVoices = true;
            ReplaceVoices(EdgeTtsVoices, await VoiceService.GetVoicesAsync(EdgeTtsSpeechProvider.EdgeEngine));
        }
        finally
        {
            IsLoadingEdgeVoices = false;
        }

        await RefreshOmniTtsModelOptionsAsync(
            selectFirstWhenMissing: true,
            expectedProvider: omniTtsProvider,
            expectedOptionsVersion: optionsVersion);
        await RefreshOmniTtsVoiceOptionsAsync(
            selectFirstWhenMissing: true,
            expectedProvider: omniTtsProvider,
            expectedOptionsVersion: optionsVersion);
    }

    private async Task<int> RefreshOmniTtsVoiceOptionsAsync(
        bool throwOnFailure = false,
        bool selectFirstWhenMissing = false,
        OmniTtsProvider? expectedProvider = null,
        int? expectedOptionsVersion = null)
    {
        IReadOnlyList<VoiceOption> voices;
        try
        {
            voices = await VoiceService.GetVoicesAsync(OmniTtsSpeechProvider.OmniEngine);
        }
        catch
        {
            if (throwOnFailure)
                throw;

            voices = [];
        }

        if (!IsCurrentOmniTtsProvider(expectedProvider, expectedOptionsVersion))
            return 0;

        var voiceIds = voices
            .Select(voice => voice.Id)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var selectedVoiceId = Settings.OmniTtsVoiceId;
        if (selectFirstWhenMissing &&
            voiceIds.Count > 0 &&
            !voiceIds.Contains(selectedVoiceId, StringComparer.Ordinal))
        {
            Settings.OmniTtsVoiceId = voiceIds[0];
        }
        else if (!string.IsNullOrWhiteSpace(selectedVoiceId) &&
                 !voiceIds.Contains(selectedVoiceId, StringComparer.Ordinal))
        {
            voiceIds.Add(selectedVoiceId);
        }

        OmniTtsVoiceOptions.Clear();
        foreach (var voiceId in voiceIds)
            OmniTtsVoiceOptions.Add(voiceId);

        return voices.Count;
    }

    private void RefreshBatchRosterOptions()
    {
        var hasPreferredSelection = SelectedBatchRoster is not null;
        var preferredSource = SelectedBatchRoster?.Source;
        var preferredName = SelectedBatchRoster?.RosterName;

        BatchRosterOptions.Clear();
        foreach (var name in ProfileCatalogManager.GetStudentListNames())
        {
            BatchRosterOptions.Add(new BatchRosterOption(
                BatchSourceStudents,
                name,
                $"{Langs.SettingsPages.Voice.Resources.O_OmniTtsBatchSource_Students} - {name}"));
        }

        foreach (var name in ProfileCatalogManager.GetPrizeListNames())
        {
            BatchRosterOptions.Add(new BatchRosterOption(
                BatchSourcePrizes,
                name,
                $"{Langs.SettingsPages.Voice.Resources.O_OmniTtsBatchSource_Prizes} - {name}"));
        }

        SelectedBatchRoster = (hasPreferredSelection
                ? BatchRosterOptions.FirstOrDefault(option =>
                    option.Source == preferredSource && option.RosterName == preferredName)
                : null)
            ?? BatchRosterOptions.FirstOrDefault(option =>
                option.Source == BatchSourceStudents &&
                option.RosterName == ViewModel.Config.RollCallSettings.DefaultClass)
            ?? BatchRosterOptions.FirstOrDefault(option =>
                option.Source == BatchSourcePrizes &&
                option.RosterName == ViewModel.Config.LotterySettings.DefaultPool)
            ?? BatchRosterOptions.FirstOrDefault();
    }

    private void RefreshClearRosterOptions()
    {
        var hasPreferredSelection = SelectedClearRoster is not null;
        var preferredSource = SelectedClearRoster?.Source;
        var preferredName = SelectedClearRoster?.RosterName;
        var cachedStudentNames = ProfileCatalogManager.GetStudentListNames()
            .Where(name => VoiceService.HasStudentsCache(ProfileCatalogManager.LoadStudentList(name)?.Students ?? []))
            .ToList();
        var cachedPrizeNames = ProfileCatalogManager.GetPrizeListNames()
            .Where(name => VoiceService.HasPrizesCache(ProfileCatalogManager.LoadPrizeList(name)?.Prizes ?? []))
            .ToList();

        ClearRosterOptions.Clear();
        if (cachedStudentNames.Count + cachedPrizeNames.Count > 0)
            ClearRosterOptions.Add(new ClearRosterOption(null, null, Langs.SettingsPages.Voice.Resources.O_OmniTtsClearScope_All));
        foreach (var name in cachedStudentNames)
        {
            ClearRosterOptions.Add(new ClearRosterOption(
                BatchSourceStudents,
                name,
                $"{Langs.SettingsPages.Voice.Resources.O_OmniTtsBatchSource_Students} - {name}"));
        }

        foreach (var name in cachedPrizeNames)
        {
            ClearRosterOptions.Add(new ClearRosterOption(
                BatchSourcePrizes,
                name,
                $"{Langs.SettingsPages.Voice.Resources.O_OmniTtsBatchSource_Prizes} - {name}"));
        }

        SelectedClearRoster = (hasPreferredSelection
                ? ClearRosterOptions.FirstOrDefault(option =>
                    option.Source == preferredSource && option.RosterName == preferredName)
                : null)
            ?? ClearRosterOptions.FirstOrDefault(option =>
                option.Source == BatchSourceStudents &&
                option.RosterName == ViewModel.Config.RollCallSettings.DefaultClass)
            ?? ClearRosterOptions.FirstOrDefault(option =>
                option.Source == BatchSourcePrizes &&
                option.RosterName == ViewModel.Config.LotterySettings.DefaultPool)
            ?? ClearRosterOptions.FirstOrDefault();
        OnPropertyChanged(nameof(HasClearCacheOptions));
    }

    private async void RefreshVoicesButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await RefreshVoicesAsync();
        this.ShowSuccessToast(Langs.SettingsPages.Voice.Resources.M_VoicesRefreshed);
    }

    private void TestVoiceTextBox_OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TestVoiceText))
            TestVoiceText = Langs.SettingsPages.Voice.Resources.C_TestVoiceText;
    }

    private void ResetTestVoiceTextButton_OnClick(object? sender, RoutedEventArgs e)
    {
        TestVoiceText = Langs.SettingsPages.Voice.Resources.C_TestVoiceText;
    }

    private async void TestVoiceButton_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(TestVoiceText))
                TestVoiceText = Langs.SettingsPages.Voice.Resources.C_TestVoiceText;

            await VoiceService.PreviewAsync(TestVoiceText);
            this.ShowSuccessToast(Langs.SettingsPages.Voice.Resources.M_TestVoiceCompleted);
        }
        catch (Exception ex)
        {
            this.ShowErrorToast(Langs.SettingsPages.Voice.Resources.M_TestVoiceFailed, ex);
        }
    }

    private void SaveOmniTtsKeyButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(OmniTtsApiKeyDraft))
        {
            this.ShowWarningToast(Langs.SettingsPages.Voice.Resources.M_OmniTtsKeyEmpty);
            return;
        }

        CredentialStore.SetKey(SelectedOmniTtsProvider, OmniTtsApiKeyDraft);
        OmniTtsApiKeyDraft = string.Empty;
        OnPropertyChanged(nameof(OmniTtsKeyStatus));
        this.ShowSuccessToast(Langs.SettingsPages.Voice.Resources.M_OmniTtsKeySaved);
    }

    private void ClearOmniTtsKeyButton_OnClick(object? sender, RoutedEventArgs e)
    {
        CredentialStore.ClearKey(SelectedOmniTtsProvider);
        OmniTtsApiKeyDraft = string.Empty;
        OnPropertyChanged(nameof(OmniTtsKeyStatus));
        this.ShowSuccessToast(Langs.SettingsPages.Voice.Resources.M_OmniTtsKeyCleared);
    }

    private void AddOmniTtsModelButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var model = OmniTtsModelInput.Trim();
        if (string.IsNullOrWhiteSpace(model))
            return;

        EnsureOption(OmniTtsModelOptions, model);
        Settings.OmniTtsModel = model;
        OmniTtsModelInput = string.Empty;
    }

    private async void RefreshOmniTtsModelsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var provider = Settings.OmniTtsProvider;
            var optionsVersion = _omniTtsProviderOptionsVersion;
            var modelCount = await RefreshOmniTtsModelOptionsAsync(
                throwOnFailure: true,
                selectFirstWhenMissing: true,
                expectedProvider: provider,
                expectedOptionsVersion: optionsVersion);
            if (!IsCurrentOmniTtsProvider(provider, optionsVersion))
                return;

            if (modelCount == 0)
                this.ShowWarningToast(Langs.SettingsPages.Voice.Resources.M_OmniTtsModelsEmpty);
            else
                this.ShowSuccessToast(Langs.SettingsPages.Voice.Resources.M_OmniTtsModelsRefreshed);
        }
        catch (Exception)
        {
            this.ShowErrorToast(Langs.SettingsPages.Voice.Resources.M_OmniTtsModelsRefreshFailed);
        }
    }

    private void AddOmniTtsVoiceButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var voice = OmniTtsVoiceInput.Trim();
        if (string.IsNullOrWhiteSpace(voice))
            return;

        EnsureOption(OmniTtsVoiceOptions, voice);
        Settings.OmniTtsVoiceId = voice;
        OmniTtsVoiceInput = string.Empty;
    }

    private async void SelectMiMoVoiceCloneButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var consent = await new FAContentDialog
        {
            Title = Langs.SettingsPages.Voice.MiMoResources.M_MiMoVoiceCloneConsentTitle,
            Content = Langs.SettingsPages.Voice.MiMoResources.M_MiMoVoiceCloneConsentContent,
            PrimaryButtonText = Langs.SettingsPages.Voice.MiMoResources.C_MiMoVoiceCloneConsent,
            CloseButtonText = Langs.SettingsPages.Voice.MiMoResources.C_MiMoVoiceCloneCancel,
            DefaultButton = FAContentDialogButton.Close
        }.ShowAsync(TopLevel.GetTopLevel(this));
        if (consent != FAContentDialogResult.Primary)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Langs.SettingsPages.Voice.MiMoResources.C_MiMoVoiceCloneSelect,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("WAV") { Patterns = ["*.wav"] }]
        });
        var file = files.FirstOrDefault();
        if (file is null)
            return;

        try
        {
            await using var stream = await file.OpenReadAsync();
            var hash = await MiMoVoiceReferenceStore.ReplaceAsync(stream);
            SaveOmniTtsSettings(() => Settings.MiMoVoiceCloneReferenceHash = hash);
            OnPropertyChanged(nameof(MiMoVoiceCloneReferenceConfigured));
            OnPropertyChanged(nameof(MiMoVoiceCloneStatus));
            OnPropertyChanged(nameof(CanStartBatch));
            this.ShowSuccessToast(Langs.SettingsPages.Voice.MiMoResources.M_MiMoVoiceCloneConfigured);
        }
        catch (Exception exception)
        {
            this.ShowErrorToast(Langs.SettingsPages.Voice.MiMoResources.M_MiMoVoiceCloneImportFailed, exception);
        }
    }

    private void ClearMiMoVoiceCloneButton_OnClick(object? sender, RoutedEventArgs e)
    {
        MiMoVoiceReferenceStore.Clear();
        SaveOmniTtsSettings(() => Settings.MiMoVoiceCloneReferenceHash = string.Empty);
        OnPropertyChanged(nameof(MiMoVoiceCloneReferenceConfigured));
        OnPropertyChanged(nameof(MiMoVoiceCloneStatus));
        OnPropertyChanged(nameof(CanStartBatch));
        this.ShowSuccessToast(Langs.SettingsPages.Voice.MiMoResources.M_MiMoVoiceCloneCleared);
    }

    private async void RefreshOmniTtsVoicesButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button refreshButton)
            return;

        try
        {
            refreshButton.IsEnabled = false;
            var provider = Settings.OmniTtsProvider;
            var optionsVersion = _omniTtsProviderOptionsVersion;
            var voiceCount = await RefreshOmniTtsVoiceOptionsAsync(
                throwOnFailure: true,
                selectFirstWhenMissing: true,
                expectedProvider: provider,
                expectedOptionsVersion: optionsVersion);
            if (!IsCurrentOmniTtsProvider(provider, optionsVersion))
                return;

            if (voiceCount == 0)
                this.ShowWarningToast(Langs.SettingsPages.Voice.Resources.M_OmniTtsVoicesEmpty);
            else
                this.ShowSuccessToast(Langs.SettingsPages.Voice.Resources.M_OmniTtsVoicesRefreshed);
        }
        catch (Exception)
        {
            this.ShowErrorToast(Langs.SettingsPages.Voice.Resources.M_OmniTtsVoicesRefreshFailed);
        }
        finally
        {
            refreshButton.IsEnabled = true;
        }
    }

    private async void OpenOmniTtsProviderButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var providerPicker = new ComboBox
        {
            Width = 440,
            ItemsSource = OmniTtsProviderOptions,
            SelectedItem = OmniTtsProviderOptions.FirstOrDefault(option => option.Provider == Settings.OmniTtsProvider)
        };
        providerPicker.ItemTemplate = new FuncDataTemplate<OmniTtsProviderOption>(
            (option, _) => new TextBlock { Text = option.DisplayName });
        var baseUrlBox = new TextBox { MinWidth = 360, Text = Settings.OmniTtsApiBaseUrl };
        var apiKeyBox = new TextBox { MinWidth = 360, PasswordChar = '*' };
        var keyStatus = new TextBlock { Opacity = 0.7, FontSize = 12 };

        void RefreshKeyStatus()
        {
            keyStatus.Text = CredentialStore.HasKey(GetSelectedProvider(providerPicker))
                ? Langs.SettingsPages.Voice.Resources.M_OmniTtsKeyConfigured
                : Langs.SettingsPages.Voice.Resources.M_OmniTtsKeyNotConfigured;
        }

        providerPicker.SelectionChanged += (_, _) =>
        {
            baseUrlBox.Text = OmniTtsSpeechProvider.GetDefaultBaseUrl(GetSelectedProvider(providerPicker));
            apiKeyBox.Text = string.Empty;
            RefreshKeyStatus();
        };
        RefreshKeyStatus();

        var saveKeyButton = new Button { Content = Langs.SettingsPages.Voice.Resources.C_OmniTtsSaveKey };
        saveKeyButton.Click += (_, _) =>
        {
            var apiKey = apiKeyBox.Text;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                this.ShowWarningToast(Langs.SettingsPages.Voice.Resources.M_OmniTtsKeyEmpty);
                return;
            }

            CredentialStore.SetKey(GetSelectedProvider(providerPicker), apiKey);
            apiKeyBox.Text = string.Empty;
            RefreshKeyStatus();
            this.ShowSuccessToast(Langs.SettingsPages.Voice.Resources.M_OmniTtsKeySaved);
        };
        var clearKeyButton = new Button { Content = Langs.SettingsPages.Voice.Resources.C_OmniTtsClearKey };
        clearKeyButton.Click += (_, _) =>
        {
            CredentialStore.ClearKey(GetSelectedProvider(providerPicker));
            apiKeyBox.Text = string.Empty;
            RefreshKeyStatus();
            this.ShowSuccessToast(Langs.SettingsPages.Voice.Resources.M_OmniTtsKeyCleared);
        };

        var content = CreateDialogContent(
            CreateFormContent(
                Langs.SettingsPages.Voice.Resources.S_OmniTts_Provider_D,
                providerPicker),
            CreateFormField(
                Langs.SettingsPages.Voice.Resources.S_OmniTts_ApiBaseUrl,
                Langs.SettingsPages.Voice.Resources.S_OmniTts_ApiBaseUrl_D,
                baseUrlBox),
            CreateFormField(
                Langs.SettingsPages.Voice.Resources.S_OmniTts_ApiKey,
                Langs.SettingsPages.Voice.Resources.S_OmniTts_ApiKey_D,
                new StackPanel
                {
                    Spacing = 6,
                    Children =
                    {
                        apiKeyBox,
                        new StackPanel
                        {
                            Orientation = Avalonia.Layout.Orientation.Horizontal,
                            Spacing = 8,
                            Children = { saveKeyButton, clearKeyButton }
                        },
                        keyStatus
                    }
                }));

        var result = await ShowSettingsDialogAsync(Langs.SettingsPages.Voice.Resources.S_OmniTts_Provider, content);
        if (result != FAContentDialogResult.Primary)
            return;

        ApplyOmniTtsProvider(
            GetSelectedProvider(providerPicker),
            baseUrlBox.Text?.Trim() ?? string.Empty);
    }

    private async void OpenOmniTtsModelButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var models = new ObservableCollection<string>(OmniTtsModelOptions);
        EnsureOption(models, Settings.OmniTtsModel);
        var modelPicker = new ComboBox
        {
            Width = OmniTtsDialogFieldWidth,
            ItemsSource = models,
            SelectedItem = Settings.OmniTtsModel
        };
        var modelInput = new TextBox
        {
            Width = OmniTtsDialogFieldWidth
        };
        var addModelButton = new Button { Content = PluginResources.C_Add, Width = OmniTtsDialogActionWidth };
        addModelButton.Click += (_, _) =>
        {
            var model = modelInput.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(model))
                return;

            EnsureOption(models, model);
            EnsureOption(OmniTtsModelOptions, model);
            modelPicker.SelectedItem = model;
            modelInput.Text = string.Empty;
        };
        var refreshModelsButton = new Button
        {
            Content = Langs.SettingsPages.Voice.Resources.C_OmniTtsRefreshModels,
            Width = OmniTtsDialogActionWidth
        };
        refreshModelsButton.Click += async (_, _) =>
        {
            try
            {
                refreshModelsButton.IsEnabled = false;
                var fetchedModels = await OmniTtsCatalog.GetModelsAsync();
                models.Clear();
                OmniTtsModelOptions.Clear();
                foreach (var model in fetchedModels.Distinct(StringComparer.Ordinal))
                {
                    models.Add(model);
                    OmniTtsModelOptions.Add(model);
                }

                EnsureOption(models, Settings.OmniTtsModel);
                EnsureOption(OmniTtsModelOptions, Settings.OmniTtsModel);
                modelPicker.SelectedItem = models.Contains(Settings.OmniTtsModel)
                    ? Settings.OmniTtsModel
                    : models.FirstOrDefault();
                if (fetchedModels.Count == 0)
                    this.ShowWarningToast(Langs.SettingsPages.Voice.Resources.M_OmniTtsModelsEmpty);
                else
                    this.ShowSuccessToast(Langs.SettingsPages.Voice.Resources.M_OmniTtsModelsRefreshed);
            }
            catch (Exception)
            {
                this.ShowErrorToast(Langs.SettingsPages.Voice.Resources.M_OmniTtsModelsRefreshFailed);
            }
            finally
            {
                refreshModelsButton.IsEnabled = true;
            }
        };

        var content = CreateDialogContent(
            CreateFormContent(
                Langs.SettingsPages.Voice.Resources.S_OmniTts_Model_D,
                new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new StackPanel
                        {
                            Orientation = Avalonia.Layout.Orientation.Horizontal,
                            Spacing = 8,
                            Children = { modelInput, addModelButton }
                        },
                        new StackPanel
                        {
                            Orientation = Avalonia.Layout.Orientation.Horizontal,
                            Spacing = 8,
                            Children = { modelPicker, refreshModelsButton }
                        }
                    }
                }));

        var result = await ShowSettingsDialogAsync(Langs.SettingsPages.Voice.Resources.S_OmniTts_Model, content);
        if (result == FAContentDialogResult.Primary && modelPicker.SelectedItem is string model)
            SaveOmniTtsSettings(() => Settings.OmniTtsModel = model);
    }

    private async void OpenOmniTtsVoiceButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await RefreshOmniTtsVoiceOptionsAsync();

        var voices = new ObservableCollection<string>(OmniTtsVoiceOptions);
        EnsureOption(voices, Settings.OmniTtsVoiceId);
        var voicePicker = new ComboBox
        {
            Width = OmniTtsDialogFieldWidth,
            ItemsSource = voices,
            SelectedItem = Settings.OmniTtsVoiceId
        };
        var voiceInput = new TextBox
        {
            Width = OmniTtsDialogFieldWidth
        };
        var addVoiceButton = new Button { Content = PluginResources.C_Add, Width = OmniTtsDialogActionWidth };
        addVoiceButton.Click += (_, _) =>
        {
            var voice = voiceInput.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(voice))
                return;

            EnsureOption(voices, voice);
            EnsureOption(OmniTtsVoiceOptions, voice);
            voicePicker.SelectedItem = voice;
            voiceInput.Text = string.Empty;
        };
        var instructionsBox = new TextBox
        {
            Width = OmniTtsDialogFieldWidth,
            MinHeight = 96,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Text = Settings.OmniTtsInstructions
        };

        var content = CreateDialogContent(
            CreateFormContent(
                Langs.SettingsPages.Voice.Resources.S_OmniTts_Voice_D,
                new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new StackPanel
                        {
                            Orientation = Avalonia.Layout.Orientation.Horizontal,
                            Spacing = 8,
                            Children = { voiceInput, addVoiceButton }
                        },
                        voicePicker
                    }
                }),
            CreateFormField(
                Langs.SettingsPages.Voice.Resources.S_OmniTts_Instructions,
                Langs.SettingsPages.Voice.Resources.S_OmniTts_Instructions_D,
                instructionsBox));

        var result = await ShowSettingsDialogAsync(Langs.SettingsPages.Voice.Resources.S_OmniTts_Voice, content);
        if (result == FAContentDialogResult.Primary)
        {
            SaveOmniTtsSettings(() =>
            {
                if (voicePicker.SelectedItem is string voice)
                    Settings.OmniTtsVoiceId = voice;
                Settings.OmniTtsInstructions = instructionsBox.Text?.Trim() ?? string.Empty;
            });
        }
    }

    private async void StartBatchButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var batchRoster = SelectedBatchRoster;
        if (IsBatchRunning || batchRoster is null)
            return;

        try
        {
            IsBatchRunning = true;
            BatchProgressText = string.Empty;
            _batchCts = new CancellationTokenSource();
            var progressRevision = ++_batchProgressRevision;
            var progress = new Progress<VoiceBatchProgress>(item =>
            {
                if (progressRevision != _batchProgressRevision)
                    return;

                BatchProgressText = string.Format(
                    Langs.SettingsPages.Voice.Resources.M_OmniTtsBatchProgress,
                    item.Completed,
                    item.Total);
            });

            VoiceBatchResult result = batchRoster.Source == BatchSourceStudents
                ? await VoiceService.GenerateStudentsCacheAsync(
                    ProfileCatalogManager.LoadStudentList(batchRoster.RosterName)?.Students ?? [], progress, _batchCts.Token)
                : await VoiceService.GeneratePrizesCacheAsync(
                    ProfileCatalogManager.LoadPrizeList(batchRoster.RosterName)?.Prizes ?? [], progress, _batchCts.Token);

            var total = result.Generated + result.Skipped + result.Failed;
            BatchProgressText = string.Format(
                Langs.SettingsPages.Voice.Resources.M_OmniTtsBatchProgress,
                total,
                total);
            this.ShowSuccessToast(Langs.SettingsPages.Voice.Resources.M_OmniTtsBatchCompletedToast);
            RefreshClearRosterOptions();
        }
        catch (OperationCanceledException)
        {
            this.ShowWarningToast(Langs.SettingsPages.Voice.Resources.M_OmniTtsBatchCancelled);
        }
        catch (Exception ex)
        {
            this.ShowErrorToast(Langs.SettingsPages.Voice.Resources.M_OmniTtsBatchFailed, ex);
        }
        finally
        {
            _batchProgressRevision++;
            _batchCts?.Dispose();
            _batchCts = null;
            IsBatchRunning = false;
        }
    }

    private void CancelBatchButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _batchCts?.Cancel();
    }

    private void ClearOmniTtsCacheButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var clearRoster = SelectedClearRoster;
        if (clearRoster is null)
            return;

        var removed = clearRoster.Source switch
        {
            BatchSourceStudents => VoiceService.ClearStudentsCache(GetStudentsForClear(clearRoster)),
            BatchSourcePrizes => VoiceService.ClearPrizesCache(GetPrizesForClear(clearRoster)),
            null => VoiceService.ClearStudentsCache(GetStudentsForClear(clearRoster)) +
                    VoiceService.ClearPrizesCache(GetPrizesForClear(clearRoster)),
            _ => 0
        };
        this.ShowSuccessToast(string.Format(
            Langs.SettingsPages.Voice.Resources.M_OmniTtsCacheCleared,
            removed));
        RefreshClearRosterOptions();
    }

    private IEnumerable<Student> GetStudentsForClear(ClearRosterOption clearRoster)
    {
        if (clearRoster.RosterName is { } rosterName)
            return ProfileCatalogManager.LoadStudentList(rosterName)?.Students ?? [];

        return ProfileCatalogManager.GetStudentListNames()
            .SelectMany(name => ProfileCatalogManager.LoadStudentList(name)?.Students ?? []);
    }

    private IEnumerable<Prize> GetPrizesForClear(ClearRosterOption clearRoster)
    {
        if (clearRoster.RosterName is { } rosterName)
            return ProfileCatalogManager.LoadPrizeList(rosterName)?.Prizes ?? [];

        return ProfileCatalogManager.GetPrizeListNames()
            .SelectMany(name => ProfileCatalogManager.LoadPrizeList(name)?.Prizes ?? []);
    }

    private async Task<FAContentDialogResult> ShowSettingsDialogAsync(string title, Control content)
    {
        return await new FAContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = MobileResources.C_Save,
            CloseButtonText = Langs.SettingsPages.Voice.Resources.C_OmniTtsCancelBatch,
            DefaultButton = FAContentDialogButton.Primary
        }.ShowAsync(TopLevel.GetTopLevel(this));
    }

    private static ScrollViewer CreateDialogContent(params Control[] content)
    {
        var panel = new StackPanel
        {
            Width = 440,
            Spacing = 16
        };
        foreach (var control in content)
            panel.Children.Add(control);

        return new ScrollViewer
        {
            MaxHeight = 560,
            Content = panel
        };
    }

    private static StackPanel CreateFormField(string title, string description, Control input)
    {
        return new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = title, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = description, Opacity = 0.7, FontSize = 12, TextWrapping = TextWrapping.Wrap },
                input
            }
        };
    }

    private static StackPanel CreateFormContent(string description, Control input)
    {
        return new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = description, Opacity = 0.7, FontSize = 12, TextWrapping = TextWrapping.Wrap },
                input
            }
        };
    }

    private static OmniTtsProvider GetSelectedProvider(ComboBox picker) =>
        (picker.SelectedItem as OmniTtsProviderOption)?.Provider ?? OmniTtsProvider.OpenAi;

    private static void EnsureOption(ObservableCollection<string> options, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !options.Contains(value))
            options.Add(value);
    }

    private void SaveOmniTtsSettings(Action apply)
    {
        _suppressSettingsSave = true;
        try
        {
            apply();
        }
        finally
        {
            _suppressSettingsSave = false;
        }

        ConfigHandler.Save();
    }

    private static void ReplaceVoices(
        ObservableCollection<VoiceOption> target,
        IReadOnlyList<VoiceOption> source)
    {
        target.Clear();
        foreach (var item in source)
            target.Add(item);
    }

    public sealed record VoiceEngineOption(int Id, string DisplayName);
    public sealed record OmniTtsProviderOption(OmniTtsProvider Provider, string DisplayName);
    private sealed record OmniTtsProviderSelection(string Model, string VoiceId);
    public sealed record BatchRosterOption(int Source, string RosterName, string DisplayName);
    public sealed record ClearRosterOption(int? Source, string? RosterName, string DisplayName);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
