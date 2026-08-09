using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Selection;
using Avalonia.Controls.Templates;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using QRCoder;
using SecRandom.Core.Controls;
using SecRandom.Core.Icons;
using SR = SecRandom.Langs.SettingsPages.Security.Resources;

namespace SecRandom.Services.Security;

internal sealed record PasswordEditorResult(string CurrentPassword, string NewPassword);
internal sealed record UsbBindingResult(string? DeviceId, string? UnbindId);

internal static class SecuritySetupDialogs
{
    private const double UsbBindingDialogContentWidth = 480;
    private const double UsbBindingDialogContentHeight = 340;

    public static async Task<PasswordEditorResult?> ShowPasswordEditorAsync(TopLevel xamlRoot, bool hasPassword)
    {
        var current = CreatePasswordInput(SR.C_CurrentPasswordPlaceholder);
        var password = CreatePasswordInput(SR.C_NewPasswordPlaceholder);
        var confirmation = CreatePasswordInput(SR.C_ConfirmPasswordPlaceholder);
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = SR.S_Password_D, TextWrapping = TextWrapping.Wrap });
        if (hasPassword)
        {
            panel.Children.Add(new TextBlock { Text = SR.C_CurrentPassword });
            panel.Children.Add(current);
        }
        panel.Children.Add(new TextBlock { Text = SR.C_NewPassword });
        panel.Children.Add(password);
        panel.Children.Add(new TextBlock { Text = SR.C_ConfirmPassword });
        panel.Children.Add(confirmation);

        var dialog = CreateDialog(xamlRoot, hasPassword ? SR.M_PasswordDialogTitle : SR.M_SetPasswordDialogTitle, panel);
        dialog.Buttons.Add(new FATaskDialogButton(SR.C_Cancel, "cancel"));
        dialog.Buttons.Add(new FATaskDialogButton(SR.C_Save, "save") { IsDefault = true });
        dialog.Closing += (_, args) =>
        {
            if (Equals(args.Result, "save") && !string.Equals(password.Text, confirmation.Text, StringComparison.Ordinal))
                args.Cancel = true;
        };

        return await dialog.ShowAsync() switch
        {
            "save" => new PasswordEditorResult(current.Text ?? string.Empty, password.Text ?? string.Empty),
            _ => null
        };
    }

    public static async Task<string?> ShowPasswordRemovalAsync(TopLevel xamlRoot)
    {
        var current = CreatePasswordInput(SR.C_CurrentPasswordPlaceholder);
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = SR.S_Password_D, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = SR.C_CurrentPassword });
        panel.Children.Add(current);

        var dialog = CreateDialog(xamlRoot, SR.M_PasswordDialogTitle, panel);
        dialog.Buttons.Add(new FATaskDialogButton(SR.C_Cancel, "cancel"));
        dialog.Buttons.Add(new FATaskDialogButton(SR.C_RemovePassword, "remove"));
        return Equals(await dialog.ShowAsync(), "remove") ? current.Text : null;
    }

    public static async Task<string?> ShowTotpSetupAsync(TopLevel xamlRoot, string secret)
    {
        var code = new TextBox
        {
            MaxLength = 6,
            PlaceholderText = SR.C_TotpPlaceholder,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            FontSize = 20
        };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = SR.M_TotpSetupDescription, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new Image
        {
            Source = BuildQrCode(TotpService.GetProvisioningUri(secret)),
            Width = 216,
            Height = 216,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        panel.Children.Add(new TextBlock { Text = SR.M_TotpManualKey });
        var keyPanel = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        keyPanel.Children.Add(new TextBox { Text = secret, IsReadOnly = true, FontFamily = FontFamily.Default });
        var copy = new Button { Content = new FluentIcon(FluentIcons.CopyFilled) };
        ToolTip.SetTip(copy, SR.C_Copy);
        copy.Click += async (_, _) => await (xamlRoot.Clipboard?.SetTextAsync(secret) ?? Task.CompletedTask);
        Grid.SetColumn(copy, 1);
        keyPanel.Children.Add(copy);
        panel.Children.Add(keyPanel);
        panel.Children.Add(new TextBlock { Text = SR.M_TotpCode });
        panel.Children.Add(code);

        var dialog = CreateDialog(xamlRoot, SR.M_TotpDialogTitle, panel);
        dialog.Buttons.Add(new FATaskDialogButton(SR.C_Cancel, "cancel"));
        dialog.Buttons.Add(new FATaskDialogButton(SR.C_VerifyAndSave, "save") { IsDefault = true });
        return Equals(await dialog.ShowAsync(), "save") ? code.Text : null;
    }

    public static async Task<UsbBindingResult?> ShowUsbBindingAsync(TopLevel xamlRoot, IReadOnlyList<UsbDeviceInfo> devices)
    {
        var bindableDevices = devices.Where(device => !device.IsBound).ToList();
        var boundDevices = devices.Where(device => device.IsBound).ToList();
        var bindList = CreateUsbDeviceList(bindableDevices);
        var unbindList = CreateUsbDeviceList(boundDevices);
        var tabs = new TabControl
        {
            Classes = { "compact" },
            Padding = new Thickness(0),
            SelectedIndex = 0
        };
        tabs.Items.Add(new TabItem
        {
            Header = SR.C_Bind,
            Content = CreateUsbTabContent(bindableDevices, bindList, SR.M_NoBindableUsb)
        });
        tabs.Items.Add(new TabItem
        {
            Header = SR.C_Unbind,
            Content = CreateUsbTabContent(boundDevices, unbindList, SR.M_NoBoundUsb)
        });

        var panel = new Grid
        {
            Width = UsbBindingDialogContentWidth,
            Height = UsbBindingDialogContentHeight,
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 12
        };
        panel.Children.Add(new TextBlock { Text = SR.M_UsbShortDescription, TextWrapping = TextWrapping.Wrap });
        Grid.SetRow(tabs, 1);
        panel.Children.Add(tabs);

        var dialog = CreateDialog(xamlRoot, SR.M_UsbDialogTitle, panel);
        var cancel = new FATaskDialogButton(SR.C_Cancel, "cancel");
        var action = new FATaskDialogButton(SR.C_Bind, "bind") { IsDefault = true };
        void UpdateActions()
        {
            var isBinding = tabs.SelectedIndex == 0;
            action.Text = isBinding ? SR.C_Bind : SR.C_Unbind;
            action.DialogResult = isBinding ? "bind" : "unbind";
            action.IsEnabled = isBinding
                ? bindList.SelectedItem is UsbDeviceInfo
                : unbindList.SelectedItem is UsbDeviceInfo;
        }

        tabs.SelectionChanged += (_, _) => UpdateActions();
        bindList.SelectionChanged += (_, _) => UpdateActions();
        unbindList.SelectionChanged += (_, _) => UpdateActions();
        dialog.Buttons.Add(cancel);
        dialog.Buttons.Add(action);
        UpdateActions();
        dialog.Closing += (_, args) =>
        {
            if (Equals(args.Result, "bind") && bindList.SelectedItem is not UsbDeviceInfo)
                args.Cancel = true;
            if (Equals(args.Result, "unbind") && unbindList.SelectedItem is not UsbDeviceInfo)
                args.Cancel = true;
        };

        return await dialog.ShowAsync() switch
        {
            "unbind" when unbindList.SelectedItem is UsbDeviceInfo { BindingId: not null } device => new UsbBindingResult(null, device.BindingId),
            "bind" when bindList.SelectedItem is UsbDeviceInfo device => new UsbBindingResult(device.DeviceId, null),
            _ => null
        };
    }

    private static Control CreateUsbTabContent(
        IReadOnlyList<UsbDeviceInfo> devices,
        ListBox list,
        string emptyMessage)
    {
        var panel = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            RowSpacing = 8,
            Margin = new Thickness(0, 8, 0, 0)
        };
        if (devices.Count == 0)
            panel.Children.Add(new TextBlock { Text = emptyMessage, TextWrapping = TextWrapping.Wrap });
        Grid.SetRow(list, 1);
        panel.Children.Add(list);
        return panel;
    }

    private static ListBox CreateUsbDeviceList(IReadOnlyList<UsbDeviceInfo> devices)
    {
        var list = new ListBox
        {
            ItemsSource = devices,
            SelectionMode = SelectionMode.Single,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ItemTemplate = new FuncDataTemplate<UsbDeviceInfo>((device, _) =>
            {
                if (device is null)
                    return new TextBlock();

                var fields = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("96,*"),
                    RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
                    ColumnSpacing = 8,
                    RowSpacing = 3
                };
                AddUsbField(fields, 0, SR.C_UsbDriveLetter, device.DriveLetter);
                AddUsbField(fields, 1, SR.C_UsbDiskName, device.DisplayName);
                var deviceIdentifier = string.IsNullOrWhiteSpace(device.HardwareName)
                    ? FormatUsbDeviceId(device.DeviceId)
                    : device.HardwareName;
                AddUsbField(
                    fields,
                    2,
                    SR.C_UsbDeviceId,
                    deviceIdentifier,
                    device.DeviceId);
                return new Border
                {
                    Padding = new Thickness(8),
                    Child = fields
                };
            })
        };
        ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Auto);
        return list;
    }

    internal static string FormatUsbDeviceId(string deviceId)
    {
        var value = deviceId.Trim();
        const string volumeGuidPrefix = "volume-guid:";
        if (value.StartsWith(volumeGuidPrefix, StringComparison.OrdinalIgnoreCase))
        {
            value = value[volumeGuidPrefix.Length..].Trim();
            const string windowsVolumePathPrefix = @"\\?\Volume{";
            if (value.StartsWith(windowsVolumePathPrefix, StringComparison.OrdinalIgnoreCase))
                value = value[windowsVolumePathPrefix.Length..].TrimEnd('\\').TrimEnd('}');
            else
                value = value.Trim('\\', '{', '}');
        }
        else
        {
            var separator = value.IndexOf(':');
            if (separator >= 0 && separator < value.Length - 1)
                value = value[(separator + 1)..];
        }

        if (value.Length <= 16)
            return value;

        return $"{value[..8]}...{value[^4..]}";
    }

    private static void AddUsbField(
        Grid grid,
        int row,
        string label,
        string value,
        string? fullValue = null)
    {
        var labelBlock = new TextBlock { Text = label, FontWeight = FontWeight.SemiBold };
        var valueBlock = new TextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        if (!string.IsNullOrWhiteSpace(fullValue) && !string.Equals(value, fullValue, StringComparison.Ordinal))
            ToolTip.SetTip(valueBlock, fullValue);
        Grid.SetRow(labelBlock, row);
        Grid.SetRow(valueBlock, row);
        Grid.SetColumn(valueBlock, 1);
        grid.Children.Add(labelBlock);
        grid.Children.Add(valueBlock);
    }

    private static FATaskDialog CreateDialog(TopLevel xamlRoot, string title, Control content) => new()
    {
        XamlRoot = xamlRoot,
        Title = title,
        Header = title,
        Content = content
    };

    private static TextBox CreatePasswordInput(string placeholderText) => new()
    {
        PasswordChar = '●',
        PlaceholderText = placeholderText
    };

    private static IImage BuildQrCode(string content)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data).GetGraphic(8);
        return new Avalonia.Media.Imaging.Bitmap(new MemoryStream(png));
    }
}
