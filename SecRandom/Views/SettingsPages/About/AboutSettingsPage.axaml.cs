using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Core.Extensions.Registry;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Icons;
using SecRandom.Core;
using SecRandom.Controls.AttachedSettings;
using SecRandom.Services;
using SecRandom.Services.Desktop;
using SecRandom.Views;
using LR = SecRandom.Langs.SettingsPages.About.Resources;

namespace SecRandom.Views.SettingsPages.About;

[PageInfo("settings.about", FluentIcons.InfoFilled, location: PageLocation.Bottom, hidePageTitle: true)]
public partial class AboutSettingsPage : UserControl, INotifyPropertyChanged
{
    private const string ContributorsEndpoint = "https://api.github.com/repos/SECTL/SecRandom/contributors?per_page=30";
    private const int InternalSettingsActivationClickCount = 10;
    private static readonly TimeSpan InternalSettingsActivationClickInterval = TimeSpan.FromMilliseconds(300);
    private bool _isRefreshingContributors;
    private int _bannerClickCount;
    private DateTimeOffset _lastBannerClickAt;

    private OnlineStatusService OnlineStatusService { get; } = IAppHost.Host!.Services
        .GetServices<IHostedService>().OfType<OnlineStatusService>().First();
    private IExternalLauncher ExternalLauncher { get; } = IAppHost.GetService<IExternalLauncher>();
    public int OnlineUsersCount => OnlineStatusService.CachedOnlineCount;
    public ObservableCollection<GitHubContributor> Contributors { get; } = [];

    public bool IsRefreshingContributors
    {
        get => _isRefreshingContributors;
        private set
        {
            if (_isRefreshingContributors == value)
                return;

            _isRefreshingContributors = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanRefreshContributors));
        }
    }

    public bool CanRefreshContributors => !IsRefreshingContributors;

    private event PropertyChangedEventHandler? NotifyPropertyChanged;

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => NotifyPropertyChanged += value;
        remove => NotifyPropertyChanged -= value;
    }
    
    public AboutSettingsPage()
    {
        DataContext = this;
        InitializeComponent();
    }

    private async void Banner_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var now = DateTimeOffset.UtcNow;
        _bannerClickCount = now - _lastBannerClickAt <= InternalSettingsActivationClickInterval
            ? _bannerClickCount + 1
            : 1;
        _lastBannerClickAt = now;

        if (_bannerClickCount < InternalSettingsActivationClickCount)
            return;

        _bannerClickCount = 0;
        _lastBannerClickAt = default;
        if (!RegisterInternalSettingsControl())
            return;

        await new FAContentDialog
        {
            Title = LR.C_InternalSettingsActivatedTitle,
            Content = CreateInternalSettingsDialogContent(),
            CloseButtonText = LR.C_Close,
            DefaultButton = FAContentDialogButton.Close
        }.ShowAsync(TopLevel.GetTopLevel(this));
    }

    private static StackPanel CreateInternalSettingsDialogContent()
    {
        var paragraphs = LR.M_InternalSettingsActivated.Split("\n\n", StringSplitOptions.None);
        var content = new StackPanel { Spacing = 12 };
        foreach (var paragraph in paragraphs)
        {
            content.Children.Add(new TextBlock
            {
                Text = paragraph,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            });
        }

        return content;
    }

    private static bool RegisterInternalSettingsControl()
    {
        return AttachedSettingsRegistryExtensions.RegisterAttachedSettingsControl<BehindSceneAttachedSettingsControl>(
            SecRandom.Langs.Common.Resources.AttachedSettings_BehindScene);
    }

    private void OpenLink(string url)
    {
        ExternalLauncher.TryOpenUri(url);
    }

    private void UriNavigationCommands_OnClick(object sender, RoutedEventArgs e)
    {
        var url = e.Source switch
        {
            FASettingsExpanderItem s => s.CommandParameter?.ToString(),
            Button s => s.CommandParameter?.ToString(),
            _ => null
        };

        if (url != null) OpenLink(url);
    }

    private async void Contributors_OnClick(object? sender, RoutedEventArgs e)
    {
        if (Resources["ContributorsDrawer"] is Grid drawer)
        {
            drawer.DataContext = this;
            SettingsView.Current?.OpenDrawer(drawer);
        }

        await RefreshContributorsAsync();
    }

    private async void RefreshContributors_OnClick(object? sender, RoutedEventArgs e)
    {
        await RefreshContributorsAsync();
    }

    private void OpenContributorProfile_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.CommandParameter is string profileUrl)
            OpenLink(profileUrl);
    }

    private async Task RefreshContributorsAsync()
    {
        if (IsRefreshingContributors)
            return;

        IsRefreshingContributors = true;
        try
        {
            var client = IAppHost.GetService<IHttpClientFactory>().CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"SecRandom/{GlobalConstants.Version}");

            GitHubContributorResponse[] response;
            try
            {
                response = await client.GetFromJsonAsync<GitHubContributorResponse[]>(ContributorsEndpoint) ?? [];
            }
            catch (HttpRequestException)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
                response = await client.GetFromJsonAsync<GitHubContributorResponse[]>(ContributorsEndpoint) ?? [];
            }
            catch (TaskCanceledException)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
                response = await client.GetFromJsonAsync<GitHubContributorResponse[]>(ContributorsEndpoint) ?? [];
            }
            var contributors = response
                .Where(contributor => !string.IsNullOrWhiteSpace(contributor.Login) &&
                                      !string.IsNullOrWhiteSpace(contributor.HtmlUrl) &&
                                      string.Equals(contributor.Type, "User", StringComparison.OrdinalIgnoreCase))
                .Select(contributor => new GitHubContributor(
                    contributor.Login!,
                    contributor.HtmlUrl!,
                    contributor.AvatarUrl,
                    contributor.Contributions))
                .ToList();

            Contributors.Clear();
            foreach (var contributor in contributors)
                Contributors.Add(contributor);

            await Task.WhenAll(contributors.Select(contributor => contributor.LoadAvatarAsync(client)));
        }
        catch (Exception exception)
        {
            this.ShowErrorToast(string.Format(LR.M_ContributorsLoadFailed, exception.Message));
        }
        finally
        {
            IsRefreshingContributors = false;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class GitHubContributor(
    string login,
    string profileUrl,
    string? avatarUrl,
    int contributions) : INotifyPropertyChanged
{
    private Bitmap? _avatar;

    public string Login { get; } = login;
    public string ProfileUrl { get; } = profileUrl;
    public string? AvatarUrl { get; } = avatarUrl;
    public int Contributions { get; } = contributions;
    public string ContributionText => string.Format(LR.S_Ack_Contributors_ContributionCount, Contributions);

    public Bitmap? Avatar
    {
        get => _avatar;
        private set
        {
            if (ReferenceEquals(_avatar, value))
                return;

            _avatar?.Dispose();
            _avatar = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Avatar)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task LoadAvatarAsync(HttpClient client)
    {
        if (string.IsNullOrWhiteSpace(AvatarUrl))
            return;

        try
        {
            await using var stream = await client.GetStreamAsync(AvatarUrl);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            buffer.Position = 0;
            Avatar = new Bitmap(buffer);
        }
        catch (Exception)
        {
        }
    }
}

public sealed class GitHubContributorResponse
{
    [JsonPropertyName("login")]
    public string? Login { get; init; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; init; }

    [JsonPropertyName("avatar_url")]
    public string? AvatarUrl { get; init; }

    [JsonPropertyName("contributions")]
    public int Contributions { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }
}
