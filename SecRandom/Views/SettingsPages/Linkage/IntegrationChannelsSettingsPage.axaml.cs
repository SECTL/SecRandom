using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Icons;
using SecRandom.Services.Integration;
using LR = SecRandom.Langs.SettingsPages.Linkage.Resources;

namespace SecRandom.Views.SettingsPages.Linkage;

[PageInfo("settings.linkage.integrationChannels", FluentIcons.PlugConnectedFilled, "settings.linkage")]
public partial class IntegrationChannelsSettingsPage : UserControl
{
    private bool _isSubscribed;
    public static readonly StyledProperty<bool> HasIntegrationChannelsProperty =
        AvaloniaProperty.Register<IntegrationChannelsSettingsPage, bool>(nameof(HasIntegrationChannels));
    public static readonly StyledProperty<bool> NoIntegrationChannelsProperty =
        AvaloniaProperty.Register<IntegrationChannelsSettingsPage, bool>(nameof(NoIntegrationChannels));

    public IntegrationChannelsSettingsPage()
    {
        DataContext = this;
        InitializeComponent();
        RefreshIntegrationChannels();
    }

    private IntegrationChannelService? IntegrationService { get; } = IAppHost.TryGetService<IntegrationChannelService>();
    public ObservableCollection<IntegrationChannelRow> IntegrationChannels { get; } = [];
    public bool HasIntegrationChannels
    {
        get => GetValue(HasIntegrationChannelsProperty);
        private set => SetValue(HasIntegrationChannelsProperty, value);
    }
    public bool NoIntegrationChannels
    {
        get => GetValue(NoIntegrationChannelsProperty);
        private set => SetValue(NoIntegrationChannelsProperty, value);
    }
    public string IntegrationChannelsTitle => GetLinkageText("S_IntegrationChannels", "Third-party linkage");
    public string IntegrationChannelsDescription => GetLinkageText("S_IntegrationChannels_D", "Manage third-party SDK channels");
    public string IntegrationChannelsEmptyText => GetLinkageText("M_IntegrationNoChannels", "No saved third-party channels");
    public string IntegrationChannelsScanLabel => GetLinkageText("C_IntegrationScan", "Scan now");

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (!_isSubscribed && IntegrationService is not null)
        {
            IntegrationService.Changed += IntegrationServiceOnChanged;
            _isSubscribed = true;
        }
        RefreshIntegrationChannels();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (_isSubscribed && IntegrationService is not null)
            IntegrationService.Changed -= IntegrationServiceOnChanged;
        _isSubscribed = false;
    }

    private void IntegrationServiceOnChanged() => Dispatcher.UIThread.Post(RefreshIntegrationChannels);

    private void RefreshIntegrationChannels()
    {
        IntegrationChannels.Clear();
        foreach (var channel in IntegrationService?.Channels ?? [])
        {
            var online = IntegrationService?.IsOnline(channel) == true;
            IntegrationChannels.Add(new IntegrationChannelRow(
                channel,
                IntegrationService!,
                GetLinkageText("C_IntegrationRemove", "Remove"),
                GetLinkageText("C_IntegrationRename", "Rename"),
                online
                    ? GetLinkageText("M_IntegrationOnline", "Online")
                    : GetLinkageText("M_IntegrationOffline", "Offline"),
                GetLinkageText("M_IntegrationDelivery", "Result delivery"),
                GetLinkageText("M_IntegrationLinkage", "Floating-window linkage"),
                GetLinkageText("M_IntegrationEnabled", "Result delivery enabled"),
                GetLinkageText("M_IntegrationDisabled", "Result delivery disabled"),
                GetLinkageText("M_IntegrationLinkageEnabled", "Floating-window linkage enabled"),
                GetLinkageText("M_IntegrationLinkageDisabled", "Floating-window linkage disabled")));
        }
        HasIntegrationChannels = IntegrationChannels.Count > 0;
        NoIntegrationChannels = !HasIntegrationChannels;
    }

    private async void DiscoverIntegrationChannels_OnClick(object? sender, RoutedEventArgs e)
    {
        if (IntegrationService is null)
            return;

        await Task.Run(IntegrationService.DiscoverNow);
        RefreshIntegrationChannels();
        this.ShowSuccessToast(GetLinkageText("M_IntegrationScanned", "Notification channels scanned"));
    }

    private async void RenameIntegrationChannel_OnClick(object? sender, RoutedEventArgs e)
    {
        if (IntegrationService is null || (sender as Control)?.DataContext is not IntegrationChannelRow row)
            return;

        var editor = new TextBox
        {
            Text = row.Channel.CustomName ?? string.Empty,
            PlaceholderText = GetLinkageText("M_IntegrationRenamePlaceholder", "Leave empty to use the official name"),
            MaxLength = 64
        };
        var result = await new FAContentDialog
        {
            Title = GetLinkageText("M_IntegrationRenameTitle", "Rename notification channel"),
            Content = editor,
            PrimaryButtonText = LR.C_Confirm,
            CloseButtonText = LR.C_Cancel,
            DefaultButton = FAContentDialogButton.Primary
        }.ShowAsync(TopLevel.GetTopLevel(this));
        if (result == FAContentDialogResult.Primary && IntegrationService.SetCustomName(row.Channel.AppId, editor.Text))
            this.ShowSuccessToast(GetLinkageText("M_IntegrationRenamed", "Notification channel renamed"));
    }

    private async void RemoveIntegrationChannel_OnClick(object? sender, RoutedEventArgs e)
    {
        if (IntegrationService is null || (sender as Control)?.DataContext is not IntegrationChannelRow row)
            return;

        var content = string.Format(CultureInfo.CurrentCulture,
            GetLinkageText("M_IntegrationRemoveContent", "SecRandom will stop sending results to {0}."),
            row.Channel.EffectiveName);
        var result = await new FAContentDialog
        {
            Title = GetLinkageText("M_IntegrationRemoveTitle", "Remove notification channel"),
            Content = content,
            PrimaryButtonText = LR.C_Confirm,
            CloseButtonText = LR.C_Cancel,
            DefaultButton = FAContentDialogButton.Close
        }.ShowAsync(TopLevel.GetTopLevel(this));
        if (result == FAContentDialogResult.Primary && IntegrationService.Remove(row.Channel.AppId))
            this.ShowSuccessToast(GetLinkageText("M_IntegrationRemoved", "Notification channel removed"));
    }

    private static string GetLinkageText(string key, string fallback) =>
        LR.ResourceManager.GetString(key, LR.Culture) ?? fallback;
}

public sealed class IntegrationChannelRow : ObservableObject
{
    private readonly IntegrationChannelService _service;
    private IntegrationChannel _channel;

    public IntegrationChannelRow(
        IntegrationChannel channel,
        IntegrationChannelService service,
        string removeLabel,
        string renameLabel,
        string statusLabel,
        string deliveryLabel,
        string linkageLabel,
        string enabledLabel,
        string disabledLabel,
        string linkageEnabledLabel,
        string linkageDisabledLabel)
    {
        _channel = channel;
        _service = service;
        RemoveLabel = removeLabel;
        RenameLabel = renameLabel;
        StatusLabel = statusLabel;
        DeliveryLabel = deliveryLabel;
        LinkageLabel = linkageLabel;
        EnabledLabel = enabledLabel;
        DisabledLabel = disabledLabel;
        LinkageEnabledLabel = linkageEnabledLabel;
        LinkageDisabledLabel = linkageDisabledLabel;
    }

    public IntegrationChannel Channel => _channel;
    public string RemoveLabel { get; }
    public string RenameLabel { get; }
    public string StatusLabel { get; }
    public string DeliveryLabel { get; }
    public string LinkageLabel { get; }
    public string EnabledLabel { get; }
    public string DisabledLabel { get; }
    public string LinkageEnabledLabel { get; }
    public string LinkageDisabledLabel { get; }
    public string Name => Channel.EffectiveName;
    public string Details => Channel.SdkVersion;
    public bool IsEnabled
    {
        get => Channel.Enabled;
        set => UpdateChannel(Channel with { Enabled = value }, () => _service.SetEnabled(Channel.AppId, value));
    }
    public bool IsLinkageEnabled
    {
        get => Channel.LinkageEnabled;
        set => UpdateChannel(Channel with { LinkageEnabled = value }, () => _service.SetLinkageEnabled(Channel.AppId, value));
    }
    public string EnabledTooltip => Channel.Enabled ? EnabledLabel : DisabledLabel;
    public string LinkageTooltip => Channel.LinkageEnabled ? LinkageEnabledLabel : LinkageDisabledLabel;

    private void UpdateChannel(IntegrationChannel updated, Func<bool> update)
    {
        if (updated == Channel)
            return;

        if (!update())
            return;

        _channel = updated;
        OnPropertyChanged(nameof(Channel));
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(IsLinkageEnabled));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Details));
        OnPropertyChanged(nameof(EnabledTooltip));
        OnPropertyChanged(nameof(LinkageTooltip));
    }
}
