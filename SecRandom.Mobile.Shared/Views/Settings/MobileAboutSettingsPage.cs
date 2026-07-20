using System.Reflection;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SecRandom.Core.Icons;
using SecRandom.Mobile.Controls;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;

namespace SecRandom.Mobile.Views.Settings;

/// <summary>
/// 关于页：居中的品牌卡片（Logo + 名称 + 版本 + GPLv3 许可证说明）+ 项目链接。
/// 版本取入口程序集的 InformationalVersion（GitInfo 版本特性生成在 Android/iOS 头程序集上）。
/// 链接经 TopLevel Launcher 打开系统浏览器；Launcher 不可用或打开失败时给状态提示。
/// </summary>
public sealed partial class MobileAboutSettingsPage : MobileSettingsPageBase
{
    private static readonly Uri ProjectUri = new("https://github.com/SECTL/SecRandom");

    private readonly TextBlock _statusText;

    public MobileAboutSettingsPage()
    {
        InitializeComponent();
        var version = (Assembly.GetEntryAssembly() ?? typeof(MobileAboutSettingsPage).Assembly)
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
            ?? "0.0.0";

        var logo = new Image
        {
            Source = new Bitmap(AssetLoader.Open(new Uri("avares://SecRandom.Mobile/Assets/AppLogo.png"))),
            Width = 64,
            Height = 64,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var name = new TextBlock
        {
            Text = "SecRandom",
            FontSize = MobileResources.FindDouble("MobileFontSizeSection", 20),
            FontWeight = FontWeight.SemiBold,
            TextAlignment = TextAlignment.Center
        };
        MobileResources.BindBrush(name, TextBlock.ForegroundProperty, MobileResources.Keys.Text);

        var versionText = CreateCaption($"{LR.S_Version} {version}", centered: true);
        var license = CreateCaption(LR.M_AboutLicense, centered: true);

        var brandCard = new MobileCard
        {
            Content = new StackPanel
            {
                Spacing = MobileResources.FindDouble("MobileSpacingSm", 8),
                HorizontalAlignment = HorizontalAlignment.Center,
                Children = { logo, name, versionText, license }
            }
        };

        _statusText = CreateCaption(string.Empty, centered: true);

        RenderPage([
            brandCard,
            MobileSettingRow.Navigation(LR.C_ViewOnGitHub, "github.com/SECTL/SecRandom", () => _ = OpenProjectLinkAsync()),
            _statusText
        ]);
    }

    private static TextBlock CreateCaption(string text, bool centered)
    {
        var caption = new TextBlock
        {
            Text = text,
            FontSize = MobileResources.FindDouble("MobileFontSizeCaption", 12),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = centered ? TextAlignment.Center : TextAlignment.Left
        };
        MobileResources.BindBrush(caption, TextBlock.ForegroundProperty, MobileResources.Keys.MutedText);
        return caption;
    }

    private async Task OpenProjectLinkAsync()
    {
        var launcher = TopLevel.GetTopLevel(this)?.Launcher;
        if (launcher is null)
        {
            _statusText.Text = LR.M_BrowserUnavailable;
            return;
        }

        if (!await launcher.LaunchUriAsync(ProjectUri))
            _statusText.Text = LR.M_OpenBrowserFailed;
    }
}
