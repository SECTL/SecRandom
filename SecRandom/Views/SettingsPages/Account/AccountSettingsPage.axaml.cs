using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using SecRandom.Core;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Icons;
using SecRandom.Services.Auth;

namespace SecRandom.Views.SettingsPages.Account;

[PageInfo("settings.account", FluentIcons.PersonFilled, location: PageLocation.Bottom)]
public partial class AccountSettingsPage : UserControl, INotifyPropertyChanged
{
    private readonly SectlAuthService _auth = IAppHost.GetService<SectlAuthService>();
    private bool _busy;

    public bool IsSignedIn => _auth.IsSignedIn;
    public string AccountName => _auth.User?.UserName ?? _auth.User?.Email ?? _auth.Token?.UserId ?? "未登录";
    public bool IsBusy { get => _busy; private set { _busy = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsBusy))); } }
    public event PropertyChangedEventHandler? PropertyChanged;

    public AccountSettingsPage()
    {
        DataContext = this;
        InitializeComponent();
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        await _auth.InitializeAsync();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSignedIn)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AccountName)));
    }

    private async void SignIn_OnClick(object? sender, RoutedEventArgs e)
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await _auth.SignInAsync(); }
        catch (Exception ex) { this.ShowErrorToast(ex.Message); }
        finally { IsBusy = false; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSignedIn))); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AccountName))); }
    }

    private async void SignOut_OnClick(object? sender, RoutedEventArgs e)
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await _auth.SignOutAsync(); }
        finally { IsBusy = false; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSignedIn))); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AccountName))); }
    }
}
