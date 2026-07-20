using Avalonia.Controls;
using Avalonia.Media;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Icons;
using SecRandom.Mobile.Controls;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;

namespace SecRandom.Mobile.Views.Settings;

/// <summary>
/// 更新页：保留 <see cref="MobileUpdateService"/> 的检查/下载/安装逻辑。
/// 安装能力投影来自 DI 的 <see cref="IMobileUpdateInstaller"/>（平台头注入；
/// iOS 与中性构建为 <see cref="UnsupportedMobileUpdateInstaller"/>）。不支持应用内更新时
/// 显示空态风格说明，而不是误导性的「已是最新版本」。
/// </summary>
public sealed partial class MobileUpdateSettingsPage : MobileSettingsPageBase
{
    private readonly MobileUpdateService _updateService;
    private readonly bool _installerSupported;

    public MobileUpdateSettingsPage(MobileUpdateService updateService)
    {
        _updateService = updateService;
        // 构造函数签名保持稳定；安装器投影经 IAppHost 解析，Host 未就绪时按不支持处理。
        _installerSupported = IAppHost.TryGetService<IMobileUpdateInstaller>()?.IsSupported ?? false;
        InitializeComponent();
        Render();
    }

    private void Render()
    {
        if (!_installerSupported)
        {
            RenderPage([
                new MobileEmptyState(
                    FluentIcons.InfoFilled,
                    LR.M_InAppUpdateUnsupported,
                    LR.M_IosUpdateDeferred)
            ]);
            return;
        }

        var status = new TextBlock
        {
            Text = string.IsNullOrEmpty(_updateService.Status)
                ? LR.M_UpdateSecurityNote
                : _updateService.Status,
            TextWrapping = TextWrapping.Wrap
        };
        MobileTheme.BindBrush(status, TextBlock.ForegroundProperty, MobileTheme.Keys.MutedText);

        RenderPage([
            MobileUi.CreateSecondaryButton(LR.C_CheckUpdates, async () =>
            {
                await _updateService.CheckAsync();
                Render();
            }),
            status,
            MobileUi.CreatePrimaryButton(LR.C_InstallUpdate, _updateService.IsUpdateAvailable && !_updateService.IsBusy, async () =>
            {
                await _updateService.DownloadAndInstallAsync();
                Render();
            })
        ]);
    }
}
