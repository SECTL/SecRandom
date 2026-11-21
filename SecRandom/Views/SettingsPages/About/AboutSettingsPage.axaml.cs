using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Icons;
using SecRandom.Core;
using SecRandom.Core.Services.Verification;
using SecRandom.Services;
using SecRandom.Services.Desktop;
using SecRandom.Shared;
using SecRandom.Views;
using LR = SecRandom.Langs.SettingsPages.About.Resources;
using DebugResources = SecRandom.Langs.SettingsPages.Debug.DebugStrings;

namespace SecRandom.Views.SettingsPages.About;

[PageInfo("settings.about", FluentIcons.InfoFilled, location: PageLocation.Bottom, hidePageTitle: true)]
public partial class AboutSettingsPage : UserControl, INotifyPropertyChanged
{
    private const string ContributorsEndpoint = "https://api.github.com/repos/SECTL/SecRandom/contributors?per_page=30";
    private const int InternalSettingsActivationClickCount = 20;
    private static readonly TimeSpan InternalSettingsActivationClickInterval = TimeSpan.FromMilliseconds(200);
    private bool _isRefreshingContributors;
    private int _bannerClickCount;
    private DateTimeOffset _lastBannerClickAt;

    private OnlineStatusService OnlineStatusService { get; } = IAppHost.Host!.Services
        .GetServices<IHostedService>().OfType<OnlineStatusService>().First();
    private IExternalLauncher ExternalLauncher { get; } = IAppHost.GetService<IExternalLauncher>();
    public int OnlineUsersCount => OnlineStatusService.CachedOnlineCount;
    public Bitmap BannerSource { get; } = LoadBanner();
    public ObservableCollection<GitHubContributor> Contributors { get; } = [];

    // 本机信息与模式版本在页面构造时读取一次：进程运行期间这些值不会变化，
    // 也不需要在每次语言切换后重新求值（文案随界面刷新重新构造页面）。
    public string MachineHostName => Environment.MachineName;
    public string MachineOperatingSystem => RuntimeInformation.OSDescription;
    public string MachineArchitecture => string.Format(
        LR.S_Machine_Architecture_Value,
        RuntimeInformation.OSArchitecture,
        RuntimeInformation.ProcessArchitecture);
    public string MachineRuntime => RuntimeInformation.FrameworkDescription;
    public string MachineProcessorCount => string.Format(
        LR.S_Machine_Processor_Value,
        Environment.ProcessorCount);
    public string MachineVersion => GlobalConstants.VersionLong;
    public string MachineAppDirectory => Utils.PackageRoot;
    public string MachineDataDirectory => Utils.DataRoot;

    public string EngineVersionText => string.Format(
        LR.S_DrawModes_Engine, VerificationWireCodec.AlgorithmEngineVersion);

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

    private static Bitmap LoadBanner()
    {
        var fileName = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch
        {
            "zh" => "secrandom-banner-cn.png",
            "ja" => "secrandom-banner-ja.png",
            _ => "secrandom-banner-en.png"
        };

        return new Bitmap(AssetLoader.Open(new Uri($"avares://SecRandom/Assets/Banners/{fileName}")));
    }

    private void OrganizationIcon_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control || e.GetPosition(control).X > 48)
            return;

        e.Handled = true;
        var now = DateTimeOffset.UtcNow;
        _bannerClickCount = now - _lastBannerClickAt <= InternalSettingsActivationClickInterval
            ? _bannerClickCount + 1
            : 1;
        _lastBannerClickAt = now;

        if (_bannerClickCount < InternalSettingsActivationClickCount)
            return;

        _bannerClickCount = 0;
        _lastBannerClickAt = default;
        SettingsView.Current?.ShowDebugNavigationItem();
        this.ShowToast(DebugResources.Get("M_DebugShown"));
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

    private async void CopyMachineInfo_OnClick(object? sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
            return;

        var lines = new[]
        {
            $"{LR.S_Machine_Version}: {MachineVersion}",
            $"{LR.S_Machine_Host}: {MachineHostName}",
            $"{LR.S_Machine_System}: {MachineOperatingSystem}",
            $"{LR.S_Machine_Architecture}: {MachineArchitecture}",
            $"{LR.S_Machine_Runtime}: {MachineRuntime}",
            $"{LR.S_Machine_Processor}: {MachineProcessorCount}",
            $"{LR.S_Machine_AppDirectory}: {MachineAppDirectory}",
            $"{LR.S_Machine_DataDirectory}: {MachineDataDirectory}"
        };

        try
        {
            await clipboard.SetTextAsync(string.Join(Environment.NewLine, lines));
            this.ShowToast(LR.M_MachineCopied);
        }
        catch (Exception)
        {
            this.ShowErrorToast(LR.M_MachineCopyFailed);
        }
    }

    private async void Contributors_OnClick(object? sender, RoutedEventArgs e)
    {
        if (Resources["ContributorsDrawer"] is Control drawer)
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

    private void CloseDrawer_OnClick(object? sender, RoutedEventArgs e)
    {
        SettingsView.Current?.CloseDrawer();
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
