using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SR = SecRandom.Langs.SettingsPages.Security.Resources;

namespace SecRandom.Services.Security;

internal sealed class SecurityVerificationWindow : Window
{
    private readonly SecurityVerificationRequest _request;
    private readonly TextBox _password = new()
    {
        PasswordChar = '●',
        PlaceholderText = SR.C_PasswordPlaceholder
    };
    private readonly TextBox _totp = new();
    private readonly CheckBox _usb = new() { Content = SR.M_UsbPresent };
    private readonly TaskCompletionSource<SecurityVerificationResponse> _completion = new();

    public Task<SecurityVerificationResponse> Completion => _completion.Task;
    public SecurityVerificationWindow(SecurityVerificationRequest request)
    {
        _request = request;
        Title = SR.M_VerificationDialogTitle;
        Width = 420;
        MinHeight = 250;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = request.LockoutRemaining is { } remaining
                ? string.Format(SR.M_VerificationLockedFormat, Math.Ceiling(remaining.TotalSeconds))
                : request.RequireAllSelectedFactors ? SR.M_VerificationAllFactors : SR.M_VerificationAnyFactor,
            TextWrapping = TextWrapping.Wrap
        });

        if (request.RequiredFactors.Contains(SecurityFactor.Password))
        {
            panel.Children.Add(new TextBlock { Text = SR.S_Password });
            panel.Children.Add(_password);
        }

        if (request.RequiredFactors.Contains(SecurityFactor.Totp))
        {
            panel.Children.Add(new TextBlock { Text = SR.S_Totp });
            _totp.PlaceholderText = SR.C_TotpPlaceholder;
            panel.Children.Add(_totp);
        }

        if (request.RequiredFactors.Contains(SecurityFactor.Usb))
            panel.Children.Add(_usb);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        var cancel = new Button { Content = SR.C_Cancel };
        cancel.Click += (_, _) => Complete(new SecurityVerificationResponse(string.Empty, string.Empty, false, true));
        if (request.AllowPreview)
        {
            var preview = new Button { Content = SR.C_Preview };
            preview.Click += (_, _) => Complete(new SecurityVerificationResponse(string.Empty, string.Empty, false, PreviewRequested: true));
            buttons.Children.Add(preview);
        }
        var confirm = new Button { Content = SR.C_Verify };
        confirm.Click += (_, _) => Complete(new SecurityVerificationResponse(_password.Text ?? string.Empty, _totp.Text ?? string.Empty, _usb.IsChecked == true));
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        panel.Children.Add(buttons);
        Content = panel;
    }

    protected override void OnClosed(EventArgs e)
    {
        _completion.TrySetResult(new SecurityVerificationResponse(string.Empty, string.Empty, false, true));
        base.OnClosed(e);
    }

    private void Complete(SecurityVerificationResponse response)
    {
        _completion.TrySetResult(response);
        Close();
    }

}
