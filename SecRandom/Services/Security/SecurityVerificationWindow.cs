using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using SR = SecRandom.Langs.SettingsPages.Security.Resources;

namespace SecRandom.Services.Security;

internal static class SecurityVerificationDialog
{
    public static async Task<SecurityVerificationResponse> ShowAsync(TopLevel xamlRoot, SecurityVerificationRequest request)
    {
        var password = new TextBox
        {
            PasswordChar = '●',
            PlaceholderText = SR.C_PasswordPlaceholder
        };
        var totp = new TextBox { PlaceholderText = SR.C_TotpPlaceholder, MaxLength = 6 };
        var usb = new CheckBox { Content = SR.M_UsbPresent };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = request.LockoutRemaining is { } remaining
                ? string.Format(SR.M_VerificationLockedFormat, Math.Ceiling(remaining.TotalSeconds))
                : request.RequireAllSelectedFactors ? SR.M_VerificationAllFactors : SR.M_VerificationAnyFactor,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });

        if (request.RequiredFactors.Contains(SecurityFactor.Password))
        {
            panel.Children.Add(new TextBlock { Text = SR.S_Password });
            panel.Children.Add(password);
        }

        if (request.RequiredFactors.Contains(SecurityFactor.Totp))
        {
            panel.Children.Add(new TextBlock { Text = SR.S_Totp });
            panel.Children.Add(totp);
        }

        if (request.RequiredFactors.Contains(SecurityFactor.Usb))
            panel.Children.Add(usb);

        var dialog = new FATaskDialog
        {
            XamlRoot = xamlRoot,
            Title = SR.M_VerificationDialogTitle,
            Header = SR.M_VerificationDialogTitle,
            Content = panel
        };
        dialog.Buttons.Add(new FATaskDialogButton(SR.C_Cancel, "cancel"));
        if (request.AllowPreview)
            dialog.Buttons.Add(new FATaskDialogButton(SR.C_Preview, "preview"));
        dialog.Buttons.Add(new FATaskDialogButton(SR.C_Verify, "verify") { IsDefault = true });

        return await dialog.ShowAsync() switch
        {
            "preview" => new SecurityVerificationResponse(string.Empty, string.Empty, false, PreviewRequested: true),
            "verify" => new SecurityVerificationResponse(password.Text ?? string.Empty, totp.Text ?? string.Empty,
                usb.IsChecked == true),
            _ => new SecurityVerificationResponse(string.Empty, string.Empty, false, Cancelled: true)
        };
    }
}
