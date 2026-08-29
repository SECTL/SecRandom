using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Core.Icons;
using SecRandom.Services.Announcements;
using LR = SecRandom.Langs.SettingsPages.Announcements.Resources;

namespace SecRandom.Views.SettingsPages.Announcements;

[PageInfo("settings.announcements", FluentIcons.MegaphoneFilled, location: PageLocation.Bottom, isHide: true, hidePageTitle: true)]
public partial class AnnouncementsSettingsPage : UserControl, INotifyPropertyChanged
{
    private readonly AnnouncementService _service = IAppHost.GetService<AnnouncementService>();
    private bool _loaded;
    private bool _isLoading;
    private string? _errorMessage;

    public AnnouncementsSettingsPage()
    {
        DataContext = this;
        InitializeComponent();
    }

    public ObservableCollection<AnnouncementItem> Announcements { get; } = [];
    public bool IsLoading { get => _isLoading; private set => SetField(ref _isLoading, value); }
    public string? ErrorMessage { get => _errorMessage; private set => SetField(ref _errorMessage, value); }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool IsEmpty => !IsLoading && !HasError && Announcements.Count == 0;

    public new event PropertyChangedEventHandler? PropertyChanged;

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_loaded)
            return;

        _loaded = true;
        await RefreshAsync();
    }

    private async void RefreshButton_OnClick(object? sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        NotifyState();
        try
        {
            IReadOnlyList<AnnouncementItem> items = await _service.GetAsync();
            Announcements.Clear();
            foreach (AnnouncementItem item in items)
                Announcements.Add(item);
        }
        catch (Exception exception)
        {
            ErrorMessage = exception is HttpRequestException ? LR.M_NetworkError : LR.M_LoadError;
        }
        finally
        {
            IsLoading = false;
            NotifyState();
        }
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
