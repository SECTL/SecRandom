using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using FluentAvalonia.UI.Controls;
using QRCoder;
using SecRandom.Core.Controls;
using SecRandom.Core.Icons;
using SR = SecRandom.Langs.SettingsPages.Security.Resources;

namespace SecRandom.Services.Security;

internal sealed record PasswordEditorResult(string CurrentPassword, string NewPassword, bool Remove);
internal sealed record UsbBindingResult(string? RootPath, string? UnbindId);

internal static class SecuritySetupDialogs
{
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
        if (hasPassword)
            dialog.Buttons.Add(new FATaskDialogButton(SR.C_RemovePassword, "remove"));
        dialog.Buttons.Add(new FATaskDialogButton(SR.C_Cancel, "cancel"));
        dialog.Buttons.Add(new FATaskDialogButton(SR.C_Save, "save") { IsDefault = true });
        dialog.Closing += (_, args) =>
        {
            if (Equals(args.Result, "save") && !string.Equals(password.Text, confirmation.Text, StringComparison.Ordinal))
                args.Cancel = true;
        };

        return await dialog.ShowAsync() switch
        {
            "remove" => new PasswordEditorResult(current.Text ?? string.Empty, string.Empty, true),
            "save" => new PasswordEditorResult(current.Text ?? string.Empty, password.Text ?? string.Empty, false),
            _ => null
        };
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

    public static async Task<UsbBindingResult?> ShowUsbBindingAsync(TopLevel xamlRoot, IReadOnlyList<UsbBindingInfo> bindings)
    {
        var selectedUsb = new TextBlock { Text = SR.M_SelectUsbFirst, TextWrapping = TextWrapping.Wrap };
        var bindingsBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        string? selectedPath = null;
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = SR.M_UsbShortDescription, TextWrapping = TextWrapping.Wrap });
        var selection = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        selection.Children.Add(selectedUsb);
        var select = new Button { Content = SR.C_SelectUsb };
        select.Click += async (_, _) =>
        {
            var folders = await xamlRoot.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = SR.M_UsbPickerTitle,
                AllowMultiple = false
            });
            var folder = folders.FirstOrDefault();
            if (folder is null)
                return;
            selectedPath = folder.TryGetLocalPath();
            selectedUsb.Text = selectedPath ?? folder.Name;
        };
        Grid.SetColumn(select, 1);
        selection.Children.Add(select);
        panel.Children.Add(selection);
        if (bindings.Count > 0)
        {
            panel.Children.Add(new TextBlock { Text = SR.C_BoundDevices });
            bindingsBox.ItemsSource = bindings;
            bindingsBox.ItemTemplate = new FuncDataTemplate<UsbBindingInfo>((binding, _) =>
            {
                if (binding is null) return new TextBlock();
                var displayName = string.IsNullOrWhiteSpace(binding.DisplayName) ? binding.Id : binding.DisplayName;
                var state = binding.IsPresent ? SR.C_UsbConnected : SR.C_UsbDisconnected;
                return new TextBlock { Text = $"{displayName} ({state})", TextTrimming = TextTrimming.CharacterEllipsis };
            });
            panel.Children.Add(bindingsBox);
        }

        var dialog = CreateDialog(xamlRoot, SR.M_UsbDialogTitle, panel);
        if (bindings.Count > 0)
            dialog.Buttons.Add(new FATaskDialogButton(SR.C_UnbindSelected, "unbind"));
        dialog.Buttons.Add(new FATaskDialogButton(SR.C_Cancel, "cancel"));
        dialog.Buttons.Add(new FATaskDialogButton(SR.C_Bind, "bind") { IsDefault = true });
        dialog.Closing += (_, args) =>
        {
            if (Equals(args.Result, "bind") && selectedPath is null)
                args.Cancel = true;
            if (Equals(args.Result, "unbind") && bindingsBox.SelectedItem is not UsbBindingInfo)
                args.Cancel = true;
        };

        return await dialog.ShowAsync() switch
        {
            "unbind" when bindingsBox.SelectedItem is UsbBindingInfo binding => new UsbBindingResult(null, binding.Id),
            "bind" => new UsbBindingResult(selectedPath, null),
            _ => null
        };
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
