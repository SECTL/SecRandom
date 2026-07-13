using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using QRCoder;
using SecRandom.Core.Controls;
using SecRandom.Core.Icons;
using SR = SecRandom.Langs.SettingsPages.Security.Resources;

namespace SecRandom.Services.Security;

internal sealed record PasswordEditorResult(string CurrentPassword, string NewPassword, bool Remove);

internal sealed class PasswordEditorWindow : Window
{
    private readonly TextBox _current = CreatePasswordInput(SR.C_CurrentPasswordPlaceholder);
    private readonly TextBox _password = CreatePasswordInput(SR.C_NewPasswordPlaceholder);
    private readonly TextBox _confirmation = CreatePasswordInput(SR.C_ConfirmPasswordPlaceholder);

    public PasswordEditorWindow(bool hasPassword)
    {
        Title = hasPassword ? SR.M_PasswordDialogTitle : SR.M_SetPasswordDialogTitle;
        Width = 420;
        MinHeight = hasPassword ? 390 : 320;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = SR.S_Password_D, TextWrapping = TextWrapping.Wrap });
        if (hasPassword)
        {
            panel.Children.Add(new TextBlock { Text = SR.C_CurrentPassword });
            panel.Children.Add(_current);
        }
        panel.Children.Add(new TextBlock { Text = SR.C_NewPassword });
        panel.Children.Add(_password);
        panel.Children.Add(new TextBlock { Text = SR.C_ConfirmPassword });
        panel.Children.Add(_confirmation);

        var buttons = CreateButtonPanel();
        if (hasPassword)
        {
            var remove = new Button { Content = SR.C_RemovePassword };
            remove.Click += (_, _) => Close(new PasswordEditorResult(_current.Text ?? string.Empty, string.Empty, true));
            buttons.Children.Insert(0, remove);
        }
        var save = new Button { Content = SR.C_Save };
        save.Click += (_, _) =>
        {
            if ((_password.Text ?? string.Empty) == (_confirmation.Text ?? string.Empty))
                Close(new PasswordEditorResult(_current.Text ?? string.Empty, _password.Text ?? string.Empty, false));
        };
        buttons.Children.Add(save);
        panel.Children.Add(buttons);
        Content = panel;
    }

    private static StackPanel CreateButtonPanel()
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        var cancel = new Button { Content = SR.C_Cancel };
        cancel.Click += (_, _) => (TopLevel.GetTopLevel(cancel) as Window)?.Close((PasswordEditorResult?)null);
        buttons.Children.Add(cancel);
        return buttons;
    }

    private static TextBox CreatePasswordInput(string placeholderText) => new() { PasswordChar = '●', PlaceholderText = placeholderText };
}

internal sealed class TotpSetupWindow : Window
{
    private readonly TextBox[] _digits = Enumerable.Range(0, 6).Select(_ => new TextBox
    {
        Width = 42,
        MaxLength = 1,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        FontSize = 20
    }).ToArray();

    public TotpSetupWindow(string secret)
    {
        Title = SR.M_TotpDialogTitle;
        Width = 620;
        MinHeight = 480;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
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
        keyPanel.Children.Add(new TextBox
        {
            Text = secret,
            IsReadOnly = true,
            FontFamily = FontFamily.Default
        });
        var copy = new Button
        {
            Content = new FluentIcon(FluentIcons.CopyFilled)
        };
        ToolTip.SetTip(copy, SR.C_Copy);
        copy.Click += async (_, _) =>
        {
            await (TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(secret) ?? System.Threading.Tasks.Task.CompletedTask);
        };
        Grid.SetColumn(copy, 1);
        keyPanel.Children.Add(copy);
        panel.Children.Add(keyPanel);
        panel.Children.Add(new TextBlock { Text = SR.M_TotpCode });
        var codePanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 8 };
        for (var index = 0; index < _digits.Length; index++)
        {
            var currentIndex = index;
            _digits[index].TextChanged += (_, _) => MoveToNextDigit(currentIndex);
            codePanel.Children.Add(_digits[index]);
        }
        panel.Children.Add(codePanel);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        var cancel = new Button { Content = SR.C_Cancel };
        cancel.Click += (_, _) => Close((string?)null);
        var save = new Button { Content = SR.C_VerifyAndSave };
        save.Click += (_, _) => Close(string.Concat(_digits.Select(input => input.Text)));
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        panel.Children.Add(buttons);
        Content = panel;
    }

    private void MoveToNextDigit(int index)
    {
        var input = _digits[index];
        if (input.Text is not { Length: > 0 }) return;
        input.Text = input.Text[^1..];
        if (index < _digits.Length - 1)
            _digits[index + 1].Focus();
    }

    private static IImage BuildQrCode(string content)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data).GetGraphic(8);
        return new Avalonia.Media.Imaging.Bitmap(new MemoryStream(png));
    }
}

internal sealed record UsbBindingResult(string? RootPath, string? UnbindId);

internal sealed class UsbBindingWindow : Window
{
    private readonly TextBlock _selectedUsb = new() { Text = SR.M_SelectUsbFirst, TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox _bindings = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private string? _selectedPath;

    public UsbBindingWindow(IReadOnlyList<UsbBindingInfo> bindings)
    {
        Title = SR.M_UsbDialogTitle;
        Width = 640;
        MinHeight = 360;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = SR.M_UsbShortDescription, TextWrapping = TextWrapping.Wrap });
        var selection = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        selection.Children.Add(_selectedUsb);
        var select = new Button { Content = SR.C_SelectUsb };
        select.Click += SelectUsb_OnClick;
        Grid.SetColumn(select, 1);
        selection.Children.Add(select);
        panel.Children.Add(selection);
        if (bindings.Count > 0)
        {
            panel.Children.Add(new TextBlock { Text = SR.C_BoundDevices });
            _bindings.ItemsSource = bindings;
            _bindings.ItemTemplate = new FuncDataTemplate<UsbBindingInfo>((binding, _) =>
            {
                if (binding is null) return new TextBlock();
                var displayName = string.IsNullOrWhiteSpace(binding.DisplayName) ? binding.Id : binding.DisplayName;
                var state = binding.IsPresent ? SR.C_UsbConnected : SR.C_UsbDisconnected;
                return new TextBlock { Text = $"{displayName} ({state})", TextTrimming = TextTrimming.CharacterEllipsis };
            });
            panel.Children.Add(_bindings);
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        var cancel = new Button { Content = SR.C_Cancel };
        cancel.Click += (_, _) => Close((UsbBindingResult?)null);
        if (bindings.Count > 0)
        {
            var unbind = new Button { Content = SR.C_UnbindSelected };
            unbind.Click += (_, _) =>
            {
                if (_bindings.SelectedItem is UsbBindingInfo binding)
                    Close(new UsbBindingResult(null, binding.Id));
            };
            buttons.Children.Add(unbind);
        }
        var bind = new Button { Content = SR.C_Bind };
        bind.Click += (_, _) =>
        {
            if (_selectedPath is not null)
                Close(new UsbBindingResult(_selectedPath, null));
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(bind);
        panel.Children.Add(buttons);
        Content = panel;
    }

    private async void SelectUsb_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = SR.M_UsbPickerTitle, AllowMultiple = false });
        var folder = folders.FirstOrDefault();
        if (folder is null) return;
        _selectedPath = folder.TryGetLocalPath();
        _selectedUsb.Text = _selectedPath ?? folder.Name;
    }
}
