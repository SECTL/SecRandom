using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using SecRandom.Core;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.AttachedSettings;
using SecRandom.Core.Models.Draw;
using SecRandom.Core.Models.SubConfigs.Picking;
using SecRandom.Core.Services.Config;
using SecRandom.Mobile.Controls;
using SecRandom.Mobile.Services;
using SecRandom.Shared.Extensions;
using SecRandom.Shared.Interfaces;
using SecRandom.Shared.Models.Profile;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;
using AvaloniaButton = Avalonia.Controls.Button;

namespace SecRandom.Mobile.Views;

public sealed partial class MobileDrawPage : UserControl
{
    private const int RollDurationMs = 800;

    private readonly IProfileService _profileService;
    private readonly IDrawTemporaryRecordService _temporaryRecordService;
    private readonly MainConfigHandler _configHandler;
    private readonly MobileRollCallService _rollCallService;
    private readonly ILotterySession _lotterySession;
    private readonly IMobileNavigator _navigator;
    private readonly IMobileCapabilities _capabilities;
    private readonly MobileDrawMediaService _drawMedia;
    private DrawSurface _drawSurface = DrawSurface.RollCall;
    private string _group = string.Empty;
    private string _gender = string.Empty;
    private int _drawCount = 1;
    private bool _drawing;
    private bool _firstRender = true;
    private IReadOnlyList<Student> _studentResult = [];
    private Prize? _prizeResult;

    public MobileDrawPage(
        IProfileService profileService,
        IDrawTemporaryRecordService temporaryRecordService,
        IFeatureAvailabilityService featureAvailabilityService,
        MainConfigHandler configHandler,
        MobileRollCallService rollCallService,
        ILotterySession lotterySession,
        MobileDrawMediaService drawMedia,
        IMobileNavigator navigator,
        IMobileCapabilities capabilities)
    {
        _profileService = profileService;
        _temporaryRecordService = temporaryRecordService;
        _configHandler = configHandler;
        _rollCallService = rollCallService;
        _lotterySession = lotterySession;
        _drawMedia = drawMedia;
        _navigator = navigator;
        _capabilities = capabilities;
        InitializeComponent();
        EnsureRestartTemporaryRecordsCleared();
        Render();
    }

    private void Render()
    {
        if (_drawSurface == DrawSurface.Lottery && !_capabilities.IsLotteryEnabled)
            _drawSurface = DrawSurface.RollCall;

        var selector = MobileViewFactory.CreateTabSplit(
            (int)_drawSurface,
            [
                (LR.C_RollCall, true),
                (LR.C_Lottery, _capabilities.IsLotteryEnabled)
            ],
            selectedIndex =>
            {
                var surface = (DrawSurface)selectedIndex;
                if (surface == _drawSurface)
                    return;

                _drawSurface = surface;
                Render();
            });
        selector.IsEnabled = !_drawing;

        ScrollViewer scroll = _drawSurface == DrawSurface.RollCall
            ? CreateRollCallContent(selector)
            : CreateLotteryContent(selector);
        this.FindControl<ContentControl>("PageContent")!.Content = scroll;
        if (_firstRender)
        {
            _firstRender = false;
            MobileAnimations.PlayPageEnter(scroll);
        }
    }

    private ScrollViewer CreateRollCallContent(Control selector)
    {
        var snapshot = _rollCallService.GetSnapshot(_group, _gender);
        _drawCount = Math.Clamp(_drawCount, 1, Math.Max(1, snapshot.RemainingCount));
        var hasStudents = _profileService.CurrentStudentList?.Students.Any(student => student.IsCandidate) ?? false;
        var (result, detail, resultCard) = CreateStudentResultCard(snapshot, hasStudents);
        var lockableControls = new List<Control> { selector };
        var controls = CreateRollCallControls(snapshot, result, detail, resultCard, lockableControls);

        return MobileViewFactory.CreateScroll([
            selector,
            resultCard,
            controls
        ]);
    }

    private MobileCard CreateRollCallControls(
        MobileRollCallSnapshot snapshot,
        TextBlock result,
        TextBlock detail,
        Control resultCard,
        ICollection<Control> lockableControls)
    {
        var list = CreateComboBox(_rollCallService.GetListNames(), GetStudentListName(), selected =>
        {
            _rollCallService.SwitchList(selected);
            _group = string.Empty;
            _gender = string.Empty;
            _drawCount = 1;
            _studentResult = [];
            _prizeResult = null;
            Render();
        });
        var group = CreateScopeComboBox(_rollCallService.GetGroups(), _group, selected =>
        {
            _group = selected;
            _drawCount = 1;
            Render();
        });
        var gender = CreateScopeComboBox(_rollCallService.GetGenders(), _gender, selected =>
        {
            _gender = selected;
            _drawCount = 1;
            Render();
        });
        list.IsEnabled = !_drawing;
        group.IsEnabled = !_drawing;
        gender.IsEnabled = !_drawing;

        var filters = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("1.2*,*,*"),
            ColumnSpacing = 8,
            Children =
            {
                CreateLabeledControl(LR.S_CurrentList, list),
                CreateLabeledControl(LR.S_Group, group),
                CreateLabeledControl(LR.S_Gender, gender)
            }
        };
        Grid.SetColumn(filters.Children[1], 1);
        Grid.SetColumn(filters.Children[2], 2);

        var summary = new TextBlock
        {
            Text = string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                LR.M_CountSummary,
                snapshot.TotalCount,
                snapshot.RemainingCount),
            TextWrapping = TextWrapping.Wrap
        };
        MobileResources.BindBrush(summary, TextBlock.ForegroundProperty, MobileResources.Keys.MutedText);

        var countText = new TextBlock
        {
            Text = _drawCount.ToString(System.Globalization.CultureInfo.CurrentCulture),
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            MinWidth = 40,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        MobileResources.BindBrush(countText, TextBlock.ForegroundProperty, MobileResources.Keys.Text);
        var minus = CreateIconButton(FluentIcons.SubtractFilled, LR.S_DrawCount);
        var plus = CreateIconButton(FluentIcons.AddFilled, LR.S_DrawCount);
        void UpdateStepper()
        {
            countText.Text = _drawCount.ToString(System.Globalization.CultureInfo.CurrentCulture);
            minus.IsEnabled = _drawCount > 1 && !_drawing;
            plus.IsEnabled = _drawCount < snapshot.RemainingCount && !_drawing;
        }
        minus.Click += (_, _) =>
        {
            _drawCount = Math.Max(1, _drawCount - 1);
            UpdateStepper();
        };
        plus.Click += (_, _) =>
        {
            _drawCount = Math.Min(Math.Max(1, snapshot.RemainingCount), _drawCount + 1);
            UpdateStepper();
        };
        UpdateStepper();
        var stepper = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Bottom,
            Children = { minus, countText, plus }
        };

        var start = MobileViewFactory.CreatePrimaryButton(LR.C_StartDraw, snapshot.RemainingCount > 0 && !_drawing);
        start.MinWidth = 150;
        start.Click += async (_, _) => await DrawStudentsAsync(snapshot, result, detail, resultCard, start, lockableControls);
        var actions = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 10,
            Children = { CreateLabeledControl(LR.S_DrawCount, stepper), start }
        };
        Grid.SetColumn(start, 1);
        start.HorizontalAlignment = HorizontalAlignment.Stretch;
        start.VerticalAlignment = VerticalAlignment.Bottom;

        var remaining = MobileViewFactory.CreateSecondaryButton(LR.C_RemainingList, () => ShowRemainingList(snapshot.Remaining));
        remaining.IsEnabled = !_drawing;
        var more = MobileViewFactory.CreateSecondaryButton(LR.C_More, ShowMoreActions);
        more.IsEnabled = !_drawing;
        var secondaryActions = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 8,
            Children = { remaining, more }
        };
        Grid.SetColumn(more, 1);

        lockableControls.Add(list);
        lockableControls.Add(group);
        lockableControls.Add(gender);
        lockableControls.Add(minus);
        lockableControls.Add(plus);
        lockableControls.Add(start);
        lockableControls.Add(remaining);
        lockableControls.Add(more);

        return new MobileCard
        {
            Content = new StackPanel
            {
                Spacing = 12,
                Children = { filters, summary, actions, secondaryActions }
            }
        };
    }

    private (TextBlock Result, TextBlock Detail, Control Card) CreateStudentResultCard(
        MobileRollCallSnapshot snapshot,
        bool hasStudents)
    {
        string resultText;
        string detailText;
        if (_studentResult.Count > 0)
        {
            resultText = string.Join("\n", _studentResult.Select(FormatStudent));
            detailText = string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                LR.M_DrawResultCount,
                _studentResult.Count);
        }
        else
        {
            resultText = snapshot.RemainingCount > 0
                ? LR.M_Ready
                : hasStudents ? LR.M_NoEligibleCandidates : LR.M_NoStudents;
            detailText = snapshot.RemainingCount > 0
                ? string.Format(System.Globalization.CultureInfo.CurrentCulture, LR.M_CandidateStudents, snapshot.RemainingCount)
                : hasStudents ? LR.M_RepeatLimitExhausted : LR.M_AddStudentsPrompt;
        }

        return CreateResultCard(resultText, detailText, MobileResources.Keys.PrimaryWash,
            ShouldShowStudentImages() ? CreateImageStrip(_studentResult) : null);
    }

    private ScrollViewer CreateLotteryContent(Control selector)
    {
        var candidates = _lotterySession.GetEligiblePrizes().ToList();
        var hasPrizes = _profileService.CurrentPrizeList?.Prizes.Any(prize => prize.IsCandidate) ?? false;
        var (result, detail, card) = CreateResultCard(
            candidates.Count > 0 ? LR.M_Ready : hasPrizes ? LR.M_NoEligibleCandidates : LR.M_NoPrizes,
            candidates.Count > 0
                ? string.Format(System.Globalization.CultureInfo.CurrentCulture, LR.M_CandidatePrizes, candidates.Count)
                : hasPrizes ? LR.M_RepeatLimitExhausted : LR.M_AddPrizesPrompt,
            MobileResources.Keys.WarmWash,
            _configHandler.Data.LotterySettings.LotteryImage ? CreateImageStrip(_prizeResult is null ? [] : [_prizeResult]) : null);
        var draw = MobileViewFactory.CreatePrimaryButton(LR.C_DrawPrize, candidates.Count > 0);
        var manage = MobileViewFactory.CreateSecondaryButton(LR.C_ManagePrizePool, OpenListManagement);
        var lockableControls = new List<Control> { selector, draw, manage };
        draw.Click += async (_, _) => await DrawPrizeAsync(candidates, result, detail, card, draw, lockableControls);

        return MobileViewFactory.CreateScroll([
            selector,
            MobileViewFactory.CreateLabel(LR.S_CurrentPool),
            MobileViewFactory.CreateTitle(_profileService.PrizeListConfig?.Name ?? LR.M_DefaultPool),
            card,
            draw,
            manage
        ]);
    }

    private async Task DrawStudentsAsync(
        MobileRollCallSnapshot snapshot,
        TextBlock result,
        TextBlock detail,
        Control card,
        AvaloniaButton start,
        IEnumerable<Control> lockableControls)
    {
        if (_drawing)
            return;

        _drawing = true;
        SetControlsEnabled(lockableControls, false);
        start.Content = LR.M_Drawing;
        HideResultMedia(card);
        await StopMediaSafelyAsync();
        IReadOnlyList<Student>? resultMediaStudents = null;
        var names = snapshot.Remaining.Select(FormatStudent).Where(name => name.Length > 0).ToList();
        if (names.Count > 0)
            MobileAnimations.StartNameRoll(result, names);

        try
        {
            var output = _rollCallService.Draw(_drawCount, _group, _gender);
            if (output.IsSuccess && output.Result.Count > 0)
                await StartStudentAnimationMediaAsync(output.Result.First());
            await Task.Delay(RollDurationMs);
            if (!output.IsSuccess || output.Result.Count == 0)
            {
                _studentResult = [];
                result.Text = GetDrawFailureText(output.Status);
                detail.Text = string.Empty;
            }
            else
            {
                _studentResult = output.Result;
                result.Text = string.Join("\n", output.Result.Select(FormatStudent));
                detail.Text = string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    LR.M_DrawResultCount,
                    output.Result.Count);
                resultMediaStudents = output.Result;
            }
        }
        catch
        {
            _studentResult = [];
            result.Text = LR.M_DrawFailed;
            detail.Text = string.Empty;
            await StopMediaSafelyAsync();
        }
        finally
        {
            MobileAnimations.Cancel(result);
            _drawing = false;
        }

        MobileAnimations.PlayResultReveal(card);
        if (resultMediaStudents is not null)
            await PlayStudentResultMediaAsync(resultMediaStudents);
        await Task.Delay(300);
        Render();
    }

    private async Task DrawPrizeAsync(
        IReadOnlyList<Prize> candidates,
        TextBlock result,
        TextBlock detail,
        Control card,
        AvaloniaButton draw,
        IReadOnlyCollection<Control> lockableControls)
    {
        if (_drawing)
            return;

        _drawing = true;
        SetControlsEnabled(lockableControls, false);
        draw.Content = LR.M_Drawing;
        HideResultMedia(card);
        await StopMediaSafelyAsync();
        Prize? resultMediaPrize = null;
        var names = candidates.Select(prize => string.IsNullOrWhiteSpace(prize.Name) ? prize.Id : prize.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name)).ToList();
        if (names.Count > 0)
            MobileAnimations.StartNameRoll(result, names);

        try
        {
            var output = _lotterySession.DrawOnce();
            if (output.IsSuccess && output.Result.Count > 0)
                await StartPrizeAnimationMediaAsync(output.Result[0]);
            await Task.Delay(RollDurationMs);
            if (!output.IsSuccess || output.Result.Count == 0)
            {
                _prizeResult = null;
                result.Text = GetDrawFailureText(output.Status);
                detail.Text = string.Empty;
            }
            else
            {
                var prize = output.Result[0];
                _prizeResult = prize;
                result.Text = string.IsNullOrWhiteSpace(prize.Name) ? prize.Id : prize.Name;
                detail.Text = string.IsNullOrWhiteSpace(prize.Id)
                    ? LR.M_DrawCompleted
                    : string.Format(System.Globalization.CultureInfo.CurrentCulture, LR.M_PrizeId, prize.Id);
                resultMediaPrize = prize;
            }
        }
        catch
        {
            _prizeResult = null;
            result.Text = LR.M_DrawFailed;
            detail.Text = string.Empty;
            await StopMediaSafelyAsync();
        }
        finally
        {
            MobileAnimations.Cancel(result);
            _drawing = false;
        }

        MobileAnimations.PlayResultReveal(card);
        if (resultMediaPrize is not null)
            await PlayPrizeResultMediaAsync(resultMediaPrize);
        await Task.Delay(300);
        Render();
    }

    private async void ShowRemainingList(IReadOnlyList<Student> remaining)
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
                rows.Children.Add(new MobileSettingRow
                {
                    Title = FormatStudent(student),
                    Description = string.Join(" · ", new[] { student.Gender, student.Group, student.Tags }
                        .Where(value => !string.IsNullOrWhiteSpace(value)))
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

    private async void ShowMoreActions()
    {
        var dialog = new FAContentDialog
        {
            Title = LR.C_More,
            CloseButtonText = LR.C_Close,
            DefaultButton = FAContentDialogButton.Close
        };
        var reset = MobileViewFactory.CreateSecondaryButton(LR.C_ResetRange, () =>
        {
            dialog.Hide();
            _rollCallService.ClearTemporaryRecords(_group, _gender);
            _studentResult = [];
            Render();
        });
        var clear = MobileViewFactory.CreateSecondaryButton(LR.C_ClearTemporaryRecords, () =>
        {
            dialog.Hide();
            _temporaryRecordService.ClearStudentList(GetStudentListName());
            _temporaryRecordService.ClearPrizeList(GetPrizeListName());
            _studentResult = [];
            _prizeResult = null;
            Render();
        });
        var manage = MobileViewFactory.CreateSecondaryButton(LR.C_ManageStudentList, () =>
        {
            dialog.Hide();
            OpenListManagement();
        });
        var settings = MobileViewFactory.CreateSecondaryButton(LR.C_DrawSettings, () =>
        {
            dialog.Hide();
            _ = _navigator.NavigateAsync(MobilePageIds.DrawSettings);
        });
        dialog.Content = new StackPanel { Spacing = 8, Children = { reset, clear, manage, settings } };
        await dialog.ShowAsync(TopLevel.GetTopLevel(this));
    }

    private static ComboBox CreateComboBox(
        IReadOnlyList<string> values,
        string selected,
        Action<string> changed)
    {
        var combo = new ComboBox
        {
            ItemsSource = values,
            SelectedItem = values.FirstOrDefault(value => string.Equals(value, selected, StringComparison.Ordinal))
                           ?? values.FirstOrDefault(),
            MinHeight = 44,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is string value && !string.Equals(value, selected, StringComparison.Ordinal))
                changed(value);
        };
        return combo;
    }

    private static ComboBox CreateScopeComboBox(
        IReadOnlyList<string> values,
        string selected,
        Action<string> changed)
    {
        var options = new[] { LR.O_All }.Concat(values).ToArray();
        var combo = new ComboBox
        {
            ItemsSource = options,
            SelectedIndex = string.IsNullOrEmpty(selected) ? 0 : Math.Max(0, Array.IndexOf(options, selected)),
            MinHeight = 44,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        combo.SelectionChanged += (_, _) =>
        {
            var value = combo.SelectedIndex <= 0 ? string.Empty : options[combo.SelectedIndex];
            if (!string.Equals(value, selected, StringComparison.Ordinal))
                changed(value);
        };
        return combo;
    }

    private static Control CreateLabeledControl(string label, Control control)
    {
        var text = new TextBlock { Text = label, FontSize = 12 };
        MobileResources.BindBrush(text, TextBlock.ForegroundProperty, MobileResources.Keys.MutedText);
        return new StackPanel { Spacing = 4, Children = { text, control } };
    }

    private static AvaloniaButton CreateIconButton(string glyph, string tooltip)
    {
        return new AvaloniaButton
        {
            Content = MobileViewFactory.CreateIcon(glyph, 18, HorizontalAlignment.Center),
            MinWidth = 44,
            MinHeight = 44,
            [ToolTip.TipProperty] = tooltip
        };
    }

    private static (TextBlock Result, TextBlock Detail, Control Card) CreateResultCard(
        string resultText,
        string detailText,
        string washResourceKey,
        Control? media = null)
    {
        var result = new TextBlock
        {
            Text = resultText,
            FontSize = MobileResources.FindDouble("MobileFontSizeResult", 34),
            FontWeight = FontWeight.SemiBold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        MobileResources.BindBrush(result, TextBlock.ForegroundProperty, MobileResources.Keys.Text);
        var detail = new TextBlock
        {
            Text = detailText,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        MobileResources.BindBrush(detail, TextBlock.ForegroundProperty, MobileResources.Keys.MutedText);
        return (result, detail, MobileViewFactory.CreateResultPanel(result, detail, washResourceKey, media));
    }

    private bool ShouldShowStudentImages() => _configHandler.Data
        .GetOverrideDrawSettings(DrawSettingsType.RollCall, OverridableDrawSettingsType.StudentImage)
        .StudentImage;

    private async Task PlayStudentResultMediaAsync(IReadOnlyList<Student> students)
    {
        try
        {
            var music = _configHandler.Data.GetOverrideDrawSettings(
                DrawSettingsType.RollCall, OverridableDrawSettingsType.Music);
            await _drawMedia.PlayResultAsync(students.FirstOrDefault(), music);
            var voiceSettings = _configHandler.Data.GetOverrideDrawSettings(
                DrawSettingsType.RollCall, OverridableDrawSettingsType.VoiceAnnouncement);
            if (_configHandler.Data.VoiceSettings.VoiceEnable && voiceSettings.VoiceAnnouncementEnabled)
                await _drawMedia.SpeakStudentsAsync(students,
                    _configHandler.Data.VoiceSettings.AnnounceId,
                    _configHandler.Data.VoiceSettings.AnnounceName,
                    _configHandler.Data.VoiceSettings.VolumeSize,
                    _configHandler.Data.VoiceSettings.SpeechRate);
        }
        catch
        {
            // Decorative media cannot invalidate a committed draw.
        }
    }

    private async Task StartStudentAnimationMediaAsync(Student student)
    {
        try
        {
            await _drawMedia.StartAnimationAsync(student, _configHandler.Data.GetOverrideDrawSettings(
                DrawSettingsType.RollCall, OverridableDrawSettingsType.Music));
        }
        catch
        {
            // The draw animation continues without optional media.
        }
    }

    private async Task PlayPrizeResultMediaAsync(Prize prize)
    {
        try
        {
            var music = _configHandler.Data.GetOverrideDrawSettings(
                DrawSettingsType.Lottery, OverridableDrawSettingsType.Music);
            await _drawMedia.PlayResultAsync(prize, music);
            var voiceSettings = _configHandler.Data.GetOverrideDrawSettings(
                DrawSettingsType.Lottery, OverridableDrawSettingsType.VoiceAnnouncement);
            if (_configHandler.Data.VoiceSettings.VoiceEnable && voiceSettings.VoiceAnnouncementEnabled)
                await _drawMedia.SpeakPrizesAsync([prize],
                    _configHandler.Data.VoiceSettings.AnnounceId,
                    _configHandler.Data.VoiceSettings.AnnounceName,
                    _configHandler.Data.VoiceSettings.VolumeSize,
                    _configHandler.Data.VoiceSettings.SpeechRate);
        }
        catch
        {
            // Decorative media cannot invalidate a committed draw.
        }
    }

    private async Task StartPrizeAnimationMediaAsync(Prize prize)
    {
        try
        {
            await _drawMedia.StartAnimationAsync(prize, _configHandler.Data.GetOverrideDrawSettings(
                DrawSettingsType.Lottery, OverridableDrawSettingsType.Music));
        }
        catch
        {
            // The draw animation continues without optional media.
        }
    }

    private async Task StopMediaSafelyAsync()
    {
        try { await _drawMedia.StopAsync(); }
        catch { }
    }

    private static void SetControlsEnabled(IEnumerable<Control> controls, bool enabled)
    {
        foreach (var control in controls)
            control.IsEnabled = enabled;
    }

    private static void HideResultMedia(Control card)
    {
        foreach (var image in card.GetVisualDescendants().OfType<Image>())
            image.IsVisible = false;
    }

    private static Control? CreateImageStrip(IEnumerable<IAttachableSettingsObject> records)
    {
        var images = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var record in records)
        {
            var settings = record.GetAttachedObject<DrawImageAttachedSettings>(
                Guid.Parse(GlobalConstants.DrawImageAttachedSettings));
            if (settings is not { IsAttachSettingsEnabled: true } || string.IsNullOrWhiteSpace(settings.ImagePath) ||
                !File.Exists(settings.ImagePath))
                continue;
            try
            {
                images.Children.Add(new Border
                {
                    Width = 88,
                    Height = 88,
                    Margin = new Thickness(4),
                    CornerRadius = new CornerRadius(12),
                    ClipToBounds = true,
                    Child = new Image { Source = new Bitmap(settings.ImagePath), Stretch = Stretch.UniformToFill }
                });
            }
            catch { }
        }
        return images.Children.Count == 0 ? null : images;
    }

    private void EnsureRestartTemporaryRecordsCleared()
    {
        if (_configHandler.Data.RollCallSettings.ClearRecord == ClearRecordMode.Restarted)
            _temporaryRecordService.ClearStudentListOnce(GetStudentListName());
        if (_configHandler.Data.LotterySettings.ClearRecord == ClearRecordMode.Restarted)
            _temporaryRecordService.ClearPrizeListOnce(GetPrizeListName());
    }

    private static string FormatStudent(Student student) => MobileViewFactory.Format(student.Id, student.Name);
    private string GetStudentListName() => _profileService.StudentListConfig?.Name ?? MobileDefaults.ProfileName;
    private string GetPrizeListName() => _profileService.PrizeListConfig?.Name ?? MobileDefaults.ProfileName;

    private void OpenListManagement() => _ = _navigator.NavigateAsync(MobilePageIds.ListManagement);

    private static string GetDrawFailureText(DrawStatus status) => status switch
    {
        DrawStatus.NoCandidates => LR.M_NoCandidates,
        DrawStatus.NoEligibleCandidates => LR.M_NoEligibleCandidates,
        DrawStatus.RepeatLimitExhausted => LR.M_RepeatLimitExhausted,
        DrawStatus.InvalidWeight => LR.M_InvalidWeight,
        _ => LR.M_DrawFailed
    };

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
