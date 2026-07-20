using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Attributes;
using SecRandom.Core.Views;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;
using SecRandom.ViewModels;

namespace SecRandom.Views.SettingsPages.Notification;

[PageInfo("settings.notification.voiceMusic", FluentIcons.PersonVoiceFilled, "settings.notification")]
public partial class VoiceSettingsPage : ViewBase, INotifyPropertyChanged
{
    private bool _isLoadingSystemVoices;
    private bool _isLoadingEdgeVoices;
    private bool _suppressVoiceSelectionSave;
    private bool _isSettingsSubscribed;
    private event PropertyChangedEventHandler? NotifyPropertyChanged;

    public VoiceSettingsPage()
    {
        Settings = ViewModel.Config.VoiceSettings;
        if (!OperatingSystem.IsWindows() && Settings.VoiceEngine != 1)
        {
            Settings.VoiceEngine = 1;
            ConfigHandler.Save();
        }

        DataContext = this;
        InitializeComponent();
        _ = RefreshVoicesAsync();
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public SecRandom.Core.Models.SubConfigs.VoiceSettingsConfig Settings { get; }
    public ObservableCollection<VoiceEngineOption> VoiceEngineOptions { get; } =
        OperatingSystem.IsWindows()
            ?
            [
                new(0, Langs.SettingsPages.Voice.Resources.O_VoiceEngine_System),
                new(1, Langs.SettingsPages.Voice.Resources.O_VoiceEngine_EdgeTts)
            ]
            : [new(1, Langs.SettingsPages.Voice.Resources.O_VoiceEngine_EdgeTts)];

    public ObservableCollection<VoiceOption> SystemTtsVoices { get; } = [];
    public ObservableCollection<VoiceOption> EdgeTtsVoices { get; } = [];
    public ObservableCollection<VoiceOption> CurrentVoiceOptions =>
        Settings.VoiceEngine == 0 ? SystemTtsVoices : EdgeTtsVoices;

    public bool IsLoadingCurrentVoices =>
        Settings.VoiceEngine == 0 ? IsLoadingSystemVoices : IsLoadingEdgeVoices;

    public string? SelectedVoiceId
    {
        get => Settings.VoiceEngine == 0 ? Settings.SystemTtsVoiceName : Settings.EdgeTtsVoiceName;
        set
        {
            if (_suppressVoiceSelectionSave || string.IsNullOrWhiteSpace(value) ||
                string.Equals(SelectedVoiceId, value, StringComparison.Ordinal))
                return;

            if (Settings.VoiceEngine == 0)
                Settings.SystemTtsVoiceName = value;
            else
                Settings.EdgeTtsVoiceName = value;

            ConfigHandler.Save();
            OnPropertyChanged();
        }
    }

    private MainConfigHandler ConfigHandler { get; } = IAppHost.GetService<MainConfigHandler>();
    private IVoiceAnnouncementService VoiceService { get; } = IAppHost.GetService<IVoiceAnnouncementService>();

    public bool IsLoadingSystemVoices
    {
        get => _isLoadingSystemVoices;
        set
        {
            if (_isLoadingSystemVoices == value)
                return;

            _isLoadingSystemVoices = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsLoadingCurrentVoices));
        }
    }

    public bool IsLoadingEdgeVoices
    {
        get => _isLoadingEdgeVoices;
        set
        {
            if (_isLoadingEdgeVoices == value)
                return;

            _isLoadingEdgeVoices = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsLoadingCurrentVoices));
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
        if (!_isSettingsSubscribed)
            return;

        Settings.PropertyChanged -= SettingsOnPropertyChanged;
        _isSettingsSubscribed = false;
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VoiceSettingsConfig.VoiceEngine))
        {
            RefreshCurrentVoiceBindings();
            OnPropertyChanged(nameof(SelectedVoiceId));
        }

        ConfigHandler.Save();
    }

    private async Task RefreshVoicesAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                IsLoadingSystemVoices = true;
                var systemVoices = await VoiceService.GetVoicesAsync(0);
                _suppressVoiceSelectionSave = true;
                try
                {
                    ReplaceVoices(SystemTtsVoices, systemVoices);
                }
                finally
                {
                    _suppressVoiceSelectionSave = false;
                }
            }
            finally
            {
                IsLoadingSystemVoices = false;
            }
        }

        try
        {
            IsLoadingEdgeVoices = true;
            var edgeVoices = await VoiceService.GetVoicesAsync(1);
            _suppressVoiceSelectionSave = true;
            try
            {
                ReplaceVoices(EdgeTtsVoices, edgeVoices);
            }
            finally
            {
                _suppressVoiceSelectionSave = false;
            }
        }
        finally
        {
            IsLoadingEdgeVoices = false;
        }

        RefreshCurrentVoiceBindings();
        OnPropertyChanged(nameof(SelectedVoiceId));
        ConfigHandler.Save();
    }

    private async void RefreshVoicesButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await RefreshVoicesAsync();
        this.ShowSuccessToast(Langs.SettingsPages.Voice.Resources.M_VoicesRefreshed);
    }

    private async void TestVoiceButton_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            await VoiceService.PreviewAsync(Langs.SettingsPages.Voice.Resources.C_TestVoiceText);
            this.ShowSuccessToast(Langs.SettingsPages.Voice.Resources.M_TestVoiceCompleted);
        }
        catch (Exception ex)
        {
            this.ShowErrorToast(Langs.SettingsPages.Voice.Resources.M_TestVoiceFailed, ex);
        }
    }

    private void RefreshCurrentVoiceBindings()
    {
        OnPropertyChanged(nameof(CurrentVoiceOptions));
        OnPropertyChanged(nameof(IsLoadingCurrentVoices));
        OnPropertyChanged(nameof(SelectedVoiceId));
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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
