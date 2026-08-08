using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Markup.Xaml;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Helpers.UI;
using SecRandom.ViewModels.Mobile;
using LR = SecRandom.Langs.Mobile.Resources;

namespace SecRandom.Views.Mobile;

/// <summary>
/// Hosts view-only animations and dialogs. Draw state and commands belong to the ViewModel.
/// </summary>
public sealed partial class MobileDrawPage : UserControl
{
    private readonly TextBlock _resultText;
    private readonly Control _resultCard;

    public MobileDrawPage(MobileDrawPageViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        DataContext = ViewModel;
        _resultText = this.FindControl<TextBlock>("ResultText")!;
        _resultCard = this.FindControl<Control>("DrawResultCard")!;
        ViewModel.AnimationRequested += ViewModel_OnAnimationRequested;
        ViewModel.DialogRequested += ViewModel_OnDialogRequested;
        DetachedFromVisualTree += (_, _) =>
        {
            ViewModel.AnimationRequested -= ViewModel_OnAnimationRequested;
            ViewModel.DialogRequested -= ViewModel_OnDialogRequested;
            MobileAnimations.Cancel(_resultText);
        };
    }

    public MobileDrawPageViewModel ViewModel { get; }

    private void ViewModel_OnAnimationRequested(object? sender, MobileDrawAnimationRequest request)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (request.Stop)
                MobileAnimations.Cancel(_resultText);
            else if (request.Reveal)
                MobileAnimations.PlayResultReveal(_resultCard);
            else if (request.Names.Count > 0)
                MobileAnimations.StartNameRoll(_resultText, request.Names);
        });
    }

    private void ViewModel_OnDialogRequested(object? sender, MobileDrawDialogRequest request)
    {
        _ = request.Kind switch
        {
            MobileDrawDialogKind.Remaining => ShowRemainingListAsync(request.Remaining ?? []),
            MobileDrawDialogKind.RemainingPrizes => ShowRemainingPrizesAsync(request.RemainingPrizes ?? []),
            _ => Task.CompletedTask
        };
    }

    private async Task ShowRemainingPrizesAsync(IReadOnlyList<SecRandom.Shared.Models.Profile.Prize> remaining)
    {
        FlyoutHelper.CloseAncestorFlyout(this.FindControl<Grid>("PrizeFlyoutGrid"));
        Control content;
        if (remaining.Count == 0)
        {
            content = new TextBlock { Text = LR.M_NoRemaining, TextWrapping = TextWrapping.Wrap };
        }
        else
        {
            var rows = new StackPanel { Spacing = 4 };
            foreach (var prize in remaining)
            {
                rows.Children.Add(new FASettingsExpander
                {
                    Header = FormatPrize(prize),
                    Description = string.Join(" | ", new[] { prize.Id, prize.Tags }
                        .Where(value => !string.IsNullOrWhiteSpace(value))),
                    MinHeight = 64,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                });
            }

            content = new ScrollViewer
            {
                MaxHeight = 520,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = rows
            };
        }

        await new FAContentDialog
        {
            Title = LR.M_RemainingTitle,
            Content = content,
            CloseButtonText = LR.C_Close,
            DefaultButton = FAContentDialogButton.Close
        }.ShowAsync(TopLevel.GetTopLevel(this));
    }

    private async Task ShowRemainingListAsync(IReadOnlyList<SecRandom.Shared.Models.Profile.Student> remaining)
    {
        FlyoutHelper.CloseAncestorFlyout(FlyoutGrid);
        
        Control content;
        if (remaining.Count == 0)
        {
            content = new TextBlock { Text = LR.M_NoRemaining, TextWrapping = TextWrapping.Wrap };
        }
        else
        {
            var rows = new StackPanel { Spacing = 4 };
            foreach (var student in remaining)
            {
                rows.Children.Add(new FASettingsExpander
                {
                    Header = FormatStudent(student),
                    Description = string.Join(" | ", new[] { student.Gender, student.Group, student.Tags }
                        .Where(value => !string.IsNullOrWhiteSpace(value))),
                    MinHeight = 64,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                });
            }

            content = new ScrollViewer
            {
                MaxHeight = 520,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = rows
            };
        }

        await new FAContentDialog
        {
            Title = LR.M_RemainingTitle,
            Content = content,
            CloseButtonText = LR.C_Close,
            DefaultButton = FAContentDialogButton.Close
        }.ShowAsync(TopLevel.GetTopLevel(this));
    }

    private void ClearTemporaryRecords_OnClick(object? sender, RoutedEventArgs e)
    {
        _ = ConfirmClearTemporaryRecordsAsync();
    }

    private async Task ConfirmClearTemporaryRecordsAsync()
    {
        var result = await new FAContentDialog
        {
            Title = LR.C_ClearTemporaryRecords,
            Content = LR.C_ClearTemporaryRecords,
            PrimaryButtonText = LR.C_ClearTemporaryRecords,
            CloseButtonText = LR.C_Cancel,
            DefaultButton = FAContentDialogButton.Close
        }.ShowAsync(TopLevel.GetTopLevel(this));
        if (result == FAContentDialogResult.Primary)
            ViewModel.ClearTemporaryRecords();
    }

    private static string FormatStudent(SecRandom.Shared.Models.Profile.Student student) => string.IsNullOrWhiteSpace(student.Id)
        ? student.Name
        : string.IsNullOrWhiteSpace(student.Name) ? student.Id : $"{student.Id}  {student.Name}";

    private static string FormatPrize(SecRandom.Shared.Models.Profile.Prize prize) =>
        string.IsNullOrWhiteSpace(prize.Name) ? prize.Id : prize.Name;
}
