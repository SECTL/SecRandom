using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Attributes;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;
using SecRandom.ViewModels;

namespace SecRandom.Views.SettingsPages;

[PageInfo("settings.notification.voiceMusic", FluentIcons.PersonVoiceRegular, "settings.notification")]
public partial class VoiceSettingsPage : UserControl, INotifyPropertyChanged
{
    private VoiceOption? _selectedSystemTtsVoice;
    private VoiceOption? _selectedEdgeTtsVoice;
    private bool _isLoadingSystemVoices;
    private bool _isLoadingEdgeVoices;
    private event PropertyChangedEventHandler? NotifyPropertyChanged;

    public VoiceSettingsPage()
    {
        Settings = ViewModel.Config.VoiceSettings;
        MoreSettings = ViewModel.Config.MoreSettings;
        DataContext = this;
        InitializeComponent();
        Settings.PropertyChanged += SettingsOnPropertyChanged;
        MoreSettings.PropertyChanged += SettingsOnPropertyChanged;
        _ = RefreshVoicesAsync();
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public VoiceSettingsConfig Settings { get; }
    public MoreSettingsConfig MoreSettings { get; }
    public ObservableCollection<string> VoiceEngineOptions { get; } =
    [
        Langs.SettingsPages.Voice.Resources.O_VoiceEngine_System,
        Langs.SettingsPages.Voice.Resources.O_VoiceEngine_EdgeTts
    ];

    public ObservableCollection<VoiceOption> SystemTtsVoices { get; } = [];
    public ObservableCollection<VoiceOption> EdgeTtsVoices { get; } = [];

    public ObservableCollection<VoiceOption> CurrentVoiceOptions =>
        Settings.VoiceEngine == 0 ? SystemTtsVoices : EdgeTtsVoices;

    public bool IsLoadingCurrentVoices =>
        Settings.VoiceEngine == 0 ? IsLoadingSystemVoices : IsLoadingEdgeVoices;

    public VoiceOption? SelectedVoice
    {
        get => Settings.VoiceEngine == 0 ? SelectedSystemTtsVoice : SelectedEdgeTtsVoice;
        set
        {
            if (Settings.VoiceEngine == 0)
                SelectedSystemTtsVoice = value;
            else
                SelectedEdgeTtsVoice = value;

            OnPropertyChanged();
        }
    }

    private MainConfigHandler ConfigHandler { get; } = IAppHost.GetService<MainConfigHandler>();
    private IVoiceAnnouncementService VoiceService { get; } = IAppHost.GetService<IVoiceAnnouncementService>();

    public VoiceOption? SelectedSystemTtsVoice
    {
        get => _selectedSystemTtsVoice;
        set
        {
            if (_selectedSystemTtsVoice == value)
                return;

            _selectedSystemTtsVoice = value;
            if (value != null)
                Settings.SystemTtsVoiceName = value.Id;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedVoice));
        }
    }

    public VoiceOption? SelectedEdgeTtsVoice
    {
        get => _selectedEdgeTtsVoice;
        set
        {
            if (_selectedEdgeTtsVoice == value)
                return;

            _selectedEdgeTtsVoice = value;
            if (value != null)
                Settings.EdgeTtsVoiceName = value.Id;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedVoice));
        }
    }

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

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        Settings.PropertyChanged -= SettingsOnPropertyChanged;
        MoreSettings.PropertyChanged -= SettingsOnPropertyChanged;
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VoiceSettingsConfig.VoiceEngine))
            RefreshCurrentVoiceBindings();

        ConfigHandler.Save();
    }

    private async Task RefreshVoicesAsync()
    {
        try
        {
            IsLoadingSystemVoices = true;
            var systemVoices = await VoiceService.GetVoicesAsync(0);
            ReplaceVoices(SystemTtsVoices, systemVoices);
            SelectedSystemTtsVoice = SelectVoice(SystemTtsVoices, Settings.SystemTtsVoiceName);
        }
        finally
        {
            IsLoadingSystemVoices = false;
        }

        try
        {
            IsLoadingEdgeVoices = true;
            var edgeVoices = await VoiceService.GetVoicesAsync(1);
            ReplaceVoices(EdgeTtsVoices, edgeVoices);
            SelectedEdgeTtsVoice = SelectVoice(EdgeTtsVoices, Settings.EdgeTtsVoiceName);
        }
        finally
        {
            IsLoadingEdgeVoices = false;
        }

        RefreshCurrentVoiceBindings();
    }

    private async void RefreshVoicesButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await RefreshVoicesAsync();
        this.ShowSuccessToast("音色列表已刷新。");
    }

    private async void TestVoiceButton_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            await VoiceService.SpeakAsync("这是一条语音播报测试。", true);
            this.ShowSuccessToast("试听已完成。");
        }
        catch (Exception ex)
        {
            this.ShowErrorToast("试听失败", ex);
        }
    }

    private void RefreshCurrentVoiceBindings()
    {
        OnPropertyChanged(nameof(CurrentVoiceOptions));
        OnPropertyChanged(nameof(IsLoadingCurrentVoices));
        OnPropertyChanged(nameof(SelectedVoice));
    }

    private static void ReplaceVoices(
        ObservableCollection<VoiceOption> target,
        IReadOnlyList<VoiceOption> source)
    {
        target.Clear();
        foreach (var item in source)
            target.Add(item);
    }

    private static VoiceOption? SelectVoice(
        ObservableCollection<VoiceOption> source,
        string selectedId)
    {
        return source.FirstOrDefault(voice => voice.Id == selectedId) ?? source.FirstOrDefault();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
