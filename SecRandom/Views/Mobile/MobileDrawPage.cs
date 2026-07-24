using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Markup.Xaml;
using FluentAvalonia.UI.Controls;
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
        if (request.Stop)
            MobileAnimations.Cancel(_resultText);
        else if (request.Reveal)
            MobileAnimations.PlayResultReveal(_resultCard);
        else if (request.Names.Count > 0)
            MobileAnimations.StartNameRoll(_resultText, request.Names);
    }

    private void ViewModel_OnDialogRequested(object? sender, MobileDrawDialogRequest request)
    {
        _ = request.Kind switch
        {
            MobileDrawDialogKind.Remaining => ShowRemainingListAsync(request.Remaining ?? []),
            MobileDrawDialogKind.More => ShowMoreActionsAsync(),
            _ => Task.CompletedTask
        };
    }

    private async Task ShowRemainingListAsync(IReadOnlyList<SecRandom.Shared.Models.Profile.Student> remaining)
    {
        Control content;
        if (remaining.Count == 0)
        {
            content = new TextBlock { Text = LR.M_NoRemaining, TextWrapping = TextWrapping.Wrap };
        }
        else
        {
            var rows = new StackPanel { Spacing = 6 };
            foreach (var student in remaining)
            {
                rows.Children.Add(new FASettingsExpanderItem
                {
                    Content = FormatStudent(student),
                    Description = string.Join(" · ", new[] { student.Gender, student.Group, student.Tags }
                        .Where(value => !string.IsNullOrWhiteSpace(value))),
                    MinHeight = 64,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
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

    private async Task ShowMoreActionsAsync()
    {
        var dialog = new FAContentDialog
        {
            Title = LR.C_More,
            CloseButtonText = LR.C_Close,
            DefaultButton = FAContentDialogButton.Close
        };
        dialog.Content = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                CreateDialogButton(LR.C_ResetRange, () =>
                {
                    dialog.Hide();
                    ViewModel.ResetScope();
                }),
                CreateDialogButton(LR.C_ClearTemporaryRecords, () => _ = ConfirmClearTemporaryRecordsAsync(dialog)),
                CreateDialogButton(LR.C_ManageStudentList, () =>
                {
                    dialog.Hide();
                    ViewModel.OpenListManagementCommand.Execute(null);
                }),
                CreateDialogButton(LR.C_DrawSettings, () =>
                {
                    dialog.Hide();
                    ViewModel.OpenDrawSettingsCommand.Execute(null);
                })
            }
        };
        await dialog.ShowAsync(TopLevel.GetTopLevel(this));
    }

    private async Task ConfirmClearTemporaryRecordsAsync(FAContentDialog parent)
    {
        parent.Hide();
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

    private static Button CreateDialogButton(string text, Action onClick)
    {
        var button = new Button
        {
            Content = text,
            MinHeight = 44,
            FontWeight = FontWeight.SemiBold,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private static string FormatStudent(SecRandom.Shared.Models.Profile.Student student) => string.IsNullOrWhiteSpace(student.Id)
        ? student.Name
        : string.IsNullOrWhiteSpace(student.Name) ? student.Id : $"{student.Id}  {student.Name}";

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
