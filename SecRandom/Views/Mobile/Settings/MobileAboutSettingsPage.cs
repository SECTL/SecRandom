using System.Reflection;
using Avalonia.Controls;
using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;
using LR = SecRandom.Langs.Mobile.Resources;

namespace SecRandom.Views.Mobile.Settings;

/// <summary>
/// About metadata is static AXAML chrome; code-behind only resolves the entry version
/// and delegates the external URL to the platform launcher.
/// </summary>
[PageInfo(MobilePageIds.About, FluentIcons.InfoFilled, "settings.mobile.application")]
public sealed partial class MobileAboutSettingsPage : MobileSettingsPageBase
{
    private static readonly Uri ProjectUri = new("https://github.com/SECTL/SecRandom");

    public MobileAboutSettingsPage(IMobileCapabilities capabilities)
        : base(capabilities)
    {
        InitializeComponent();
        var version = (Assembly.GetEntryAssembly() ?? typeof(MobileAboutSettingsPage).Assembly)
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
            ?? "0.0.0";
        VersionText.Text = $"{LR.S_Version} {version}";
    }

    private async void ProjectLinkButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var launcher = TopLevel.GetTopLevel(this)?.Launcher;
        if (launcher is null)
        {
            AboutStatusText.Text = LR.M_BrowserUnavailable;
            return;
        }

        if (!await launcher.LaunchUriAsync(ProjectUri))
            AboutStatusText.Text = LR.M_OpenBrowserFailed;
    }
}
