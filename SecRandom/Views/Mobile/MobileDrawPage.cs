using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Markup.Xaml;
using FluentAvalonia.UI.Controls;
using SecRandom.Core;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.AttachedSettings;
using SecRandom.Core.Models.Draw;
using SecRandom.Core.Models.SubConfigs.Picking;
using SecRandom.Core.Services.Config;
using SecRandom.Controls.Mobile;
using SecRandom.Services.Mobile;
using SecRandom.Shared.Extensions;
using SecRandom.Shared.Interfaces;
using SecRandom.Shared.Models.Profile;
using LR = SecRandom.Langs.Mobile.Resources;

namespace SecRandom.Views.Mobile;

public sealed partial class MobileDrawPage : UserControl
{
    private const int RollDurationMs = 800;

    private readonly IProfileService _profileService;
    private readonly IDrawTemporaryRecordService _temporaryRecordService;
    private readonly MainConfigHandler _configHandler;
    private readonly MobileRollCallService _rollCallService;
    private readonly ILotterySession _lotterySession;
    private readonly IMobileSettingsNavigator _settingsNavigator;
    private readonly IMobileCapabilities _capabilities;
    private readonly MobileDrawMediaService _drawMedia;
    private DrawSurface _drawSurface = DrawSurface.RollCall;
    private string _group = string.Empty;
    private string _gender = string.Empty;
    private int _drawCount = 1;
    private bool _drawing;
    private MobileRollCallSnapshot? _rollCallSnapshot;
    private IReadOnlyList<Prize> _lotteryCandidates = [];
    private IReadOnlyList<Student> _studentResult = [];
    private Prize? _prizeResult;
    private string? _resultStatusText;
    private string? _resultStatusDetail;
    private bool _synchronizingSurface;

    public MobileDrawPage(
        IProfileService profileService,
        IDrawTemporaryRecordService temporaryRecordService,
        MainConfigHandler configHandler,
        MobileRollCallService rollCallService,
        ILotterySession lotterySession,
        MobileDrawMediaService drawMedia,
        IMobileSettingsNavigator settingsNavigator,
        IMobileCapabilities capabilities)
    {
        _profileService = profileService;
        _temporaryRecordService = temporaryRecordService;
        _configHandler = configHandler;
        _rollCallService = rollCallService;
        _lotterySession = lotterySession;
        _drawMedia = drawMedia;
        _settingsNavigator = settingsNavigator;
        _capabilities = capabilities;
        InitializeComponent();
        EnsureRestartTemporaryRecordsCleared();
        RefreshSurface();
    }

    private void RefreshSurface()
    {
        if (_drawSurface == DrawSurface.Lottery && !_capabilities.IsLotteryEnabled)
            _drawSurface = DrawSurface.RollCall;

        _synchronizingSurface = true;
        try
        {
            var snapshot = _rollCallService.GetSnapshot(_group, _gender);
            _rollCallSnapshot = snapshot;
            _drawCount = Math.Clamp(_drawCount, 1, Math.Max(1, snapshot.RemainingCount));
            var hasStudents = _profileService.CurrentStudentList?.Students.Any(student => student.IsCandidate) ?? false;
            _lotteryCandidates = _lotterySession.GetEligiblePrizes().ToList();
            var hasPrizes = _profileService.CurrentPrizeList?.Prizes.Any(prize => prize.IsCandidate) ?? false;
            DrawSurfaceTabs.SelectedIndex = (int)_drawSurface;
            LotteryTab.IsVisible = _capabilities.IsLotteryEnabled;
            DrawSurfaceTabs.IsEnabled = !_drawing;
            RollCallPanel.IsVisible = _drawSurface == DrawSurface.RollCall;
            LotteryPanel.IsVisible = _drawSurface == DrawSurface.Lottery;
            StudentListSelector.ItemsSource = _rollCallService.GetListNames();
            StudentListSelector.SelectedItem = GetStudentListName();
            GroupSelector.ItemsSource = new[] { LR.O_All }.Concat(_rollCallService.GetGroups()).ToArray();
            GroupSelector.SelectedItem = string.IsNullOrEmpty(_group) ? LR.O_All : _group;
            GenderSelector.ItemsSource = new[] { LR.O_All }.Concat(_rollCallService.GetGenders()).ToArray();
            GenderSelector.SelectedItem = string.IsNullOrEmpty(_gender) ? LR.O_All : _gender;
            StudentListSelector.IsEnabled = !_drawing;
            GroupSelector.IsEnabled = !_drawing;
            GenderSelector.IsEnabled = !_drawing;
            RollCallSummary.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture,
                LR.M_CountSummary, snapshot.TotalCount, snapshot.RemainingCount);
            DrawCountText.Text = _drawCount.ToString(System.Globalization.CultureInfo.CurrentCulture);
            DecreaseCountButton.IsEnabled = _drawCount > 1 && !_drawing;
            IncreaseCountButton.IsEnabled = _drawCount < snapshot.RemainingCount && !_drawing;
            DrawStudentsButton.Content = _drawing ? LR.M_Drawing : LR.C_StartDraw;
            DrawStudentsButton.IsEnabled = snapshot.RemainingCount > 0 && !_drawing;
            RemainingButton.IsEnabled = !_drawing;
            MoreButton.IsEnabled = !_drawing;
            PrizePoolName.Text = _profileService.PrizeListConfig?.Name ?? LR.M_DefaultPool;
            DrawPrizeButton.Content = _drawing ? LR.M_Drawing : LR.C_DrawPrize;
            DrawPrizeButton.IsEnabled = _lotteryCandidates.Count > 0 && !_drawing;
            ManagePrizeButton.IsEnabled = !_drawing;
            UpdateResult(snapshot, hasStudents, hasPrizes);
        }
        finally
        {
            _synchronizingSurface = false;
        }
    }

    private async Task DrawStudentsAsync(MobileRollCallSnapshot snapshot)
    {
        if (_drawing)
            return;

        _drawing = true;
        _resultStatusText = null;
        _resultStatusDetail = null;
        RefreshSurface();
        ResultImages.IsVisible = false;
        await StopMediaSafelyAsync();
        IReadOnlyList<Student>? resultMediaStudents = null;
        var names = snapshot.Remaining.Select(FormatStudent).Where(name => name.Length > 0).ToList();
        if (names.Count > 0)
            MobileAnimations.StartNameRoll(ResultText, names);

        try
        {
            var output = _rollCallService.Draw(_drawCount, _group, _gender);
            if (output.IsSuccess && output.Result.Count > 0)
                await StartStudentAnimationMediaAsync(output.Result.First());
            await Task.Delay(RollDurationMs);
            if (!output.IsSuccess || output.Result.Count == 0)
            {
                _studentResult = [];
                _resultStatusText = GetDrawFailureText(output.Status);
                _resultStatusDetail = string.Empty;
            }
            else
            {
                _studentResult = output.Result;
                resultMediaStudents = output.Result;
            }
        }
        catch
            {
                _studentResult = [];
                _resultStatusText = LR.M_DrawFailed;
                _resultStatusDetail = string.Empty;
                await StopMediaSafelyAsync();
        }
        finally
        {
            MobileAnimations.Cancel(ResultText);
            _drawing = false;
        }

        RefreshSurface();
        MobileAnimations.PlayResultReveal(DrawResultCard);
        if (resultMediaStudents is not null)
            await PlayStudentResultMediaAsync(resultMediaStudents);
        await Task.Delay(300);
    }

    private async Task DrawPrizeAsync(IReadOnlyList<Prize> candidates)
    {
        if (_drawing)
            return;

        _drawing = true;
        _resultStatusText = null;
        _resultStatusDetail = null;
        RefreshSurface();
        ResultImages.IsVisible = false;
        await StopMediaSafelyAsync();
        Prize? resultMediaPrize = null;
        var names = candidates.Select(prize => string.IsNullOrWhiteSpace(prize.Name) ? prize.Id : prize.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name)).ToList();
        if (names.Count > 0)
            MobileAnimations.StartNameRoll(ResultText, names);

        try
        {
            var output = _lotterySession.DrawOnce();
            if (output.IsSuccess && output.Result.Count > 0)
                await StartPrizeAnimationMediaAsync(output.Result[0]);
            await Task.Delay(RollDurationMs);
            if (!output.IsSuccess || output.Result.Count == 0)
            {
                _prizeResult = null;
                _resultStatusText = GetDrawFailureText(output.Status);
                _resultStatusDetail = string.Empty;
            }
            else
            {
                var prize = output.Result[0];
                _prizeResult = prize;
                resultMediaPrize = prize;
            }
        }
        catch
            {
                _prizeResult = null;
                _resultStatusText = LR.M_DrawFailed;
                _resultStatusDetail = string.Empty;
                await StopMediaSafelyAsync();
        }
        finally
        {
            MobileAnimations.Cancel(ResultText);
            _drawing = false;
        }

        RefreshSurface();
        MobileAnimations.PlayResultReveal(DrawResultCard);
        if (resultMediaPrize is not null)
            await PlayPrizeResultMediaAsync(resultMediaPrize);
        await Task.Delay(300);
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

    private async void ShowMoreActions()
    {
        var dialog = new FAContentDialog
        {
            Title = LR.C_More,
            CloseButtonText = LR.C_Close,
            DefaultButton = FAContentDialogButton.Close
        };
        Button CreateDialogButton(string text, Action onClick)
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

        var reset = CreateDialogButton(LR.C_ResetRange, () =>
        {
            dialog.Hide();
            _rollCallService.ClearTemporaryRecords(_group, _gender);
            ClearResult();
            RefreshSurface();
        });
        var clear = CreateDialogButton(LR.C_ClearTemporaryRecords,
            () => _ = ConfirmClearTemporaryRecordsAsync(dialog));
        var manage = CreateDialogButton(LR.C_ManageStudentList, () =>
        {
            dialog.Hide();
            OpenListManagement();
        });
        var settings = CreateDialogButton(LR.C_DrawSettings, () =>
        {
            dialog.Hide();
            _ = _settingsNavigator.NavigateAsync(MobilePageIds.DrawSettings);
        });
        dialog.Content = new StackPanel { Spacing = 8, Children = { reset, clear, manage, settings } };
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
        if (result != FAContentDialogResult.Primary)
            return;

        _temporaryRecordService.ClearStudentList(GetStudentListName());
        _temporaryRecordService.ClearPrizeList(GetPrizeListName());
        ClearResult();
        RefreshSurface();
    }

    private void DrawSurfaceTabs_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not TabStrip tabs || _synchronizingSurface || tabs.SelectedIndex < 0)
            return;

        var surface = (DrawSurface)tabs.SelectedIndex;
        if (surface == _drawSurface || surface == DrawSurface.Lottery && !_capabilities.IsLotteryEnabled)
            return;

        _drawSurface = surface;
        _resultStatusText = null;
        _resultStatusDetail = null;
        RefreshSurface();
    }

    private void StudentListSelector_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_synchronizingSurface || StudentListSelector.SelectedItem is not string selected ||
            string.Equals(selected, GetStudentListName(), StringComparison.Ordinal))
            return;

        _rollCallService.SwitchList(selected);
        _group = string.Empty;
        _gender = string.Empty;
        _drawCount = 1;
        ClearResult();
        RefreshSurface();
    }

    private void GroupSelector_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_synchronizingSurface || GroupSelector.SelectedItem is not string selected)
            return;

        var value = string.Equals(selected, LR.O_All, StringComparison.Ordinal) ? string.Empty : selected;
        if (string.Equals(value, _group, StringComparison.Ordinal))
            return;

        _group = value;
        _drawCount = 1;
        ClearResult();
        RefreshSurface();
    }

    private void GenderSelector_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_synchronizingSurface || GenderSelector.SelectedItem is not string selected)
            return;

        var value = string.Equals(selected, LR.O_All, StringComparison.Ordinal) ? string.Empty : selected;
        if (string.Equals(value, _gender, StringComparison.Ordinal))
            return;

        _gender = value;
        _drawCount = 1;
        ClearResult();
        RefreshSurface();
    }

    private void DecreaseCountButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _drawCount = Math.Max(1, _drawCount - 1);
        RefreshSurface();
    }

    private void IncreaseCountButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var maximum = Math.Max(1, _rollCallSnapshot?.RemainingCount ?? 1);
        _drawCount = Math.Min(maximum, _drawCount + 1);
        RefreshSurface();
    }

    private async void DrawStudentsButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_rollCallSnapshot is not null)
            await DrawStudentsAsync(_rollCallSnapshot);
    }

    private void RemainingButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_rollCallSnapshot is not null)
            ShowRemainingList(_rollCallSnapshot.Remaining);
    }

    private void MoreButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ShowMoreActions();

    private async void DrawPrizeButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await DrawPrizeAsync(_lotteryCandidates);

    private void ManagePrizeButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => OpenListManagement();

    private void UpdateResult(MobileRollCallSnapshot snapshot, bool hasStudents, bool hasPrizes)
    {
        if (_drawSurface == DrawSurface.RollCall)
        {
            ResultText.Text = _resultStatusText ?? (_studentResult.Count > 0
                ? string.Join("\n", _studentResult.Select(FormatStudent))
                : snapshot.RemainingCount > 0
                    ? LR.M_Ready
                    : hasStudents ? LR.M_NoEligibleCandidates : LR.M_NoStudents);
            ResultDetail.Text = _resultStatusDetail ?? (_studentResult.Count > 0
                ? string.Format(System.Globalization.CultureInfo.CurrentCulture, LR.M_DrawResultCount, _studentResult.Count)
                : snapshot.RemainingCount > 0
                    ? string.Format(System.Globalization.CultureInfo.CurrentCulture, LR.M_CandidateStudents, snapshot.RemainingCount)
                    : hasStudents ? LR.M_RepeatLimitExhausted : LR.M_AddStudentsPrompt);
            UpdateResultImages(ShouldShowStudentImages() ? _studentResult : []);
            return;
        }

        ResultText.Text = _resultStatusText ?? (_prizeResult is not null
            ? string.IsNullOrWhiteSpace(_prizeResult.Name) ? _prizeResult.Id : _prizeResult.Name
            : _lotteryCandidates.Count > 0
                ? LR.M_Ready
                : hasPrizes ? LR.M_NoEligibleCandidates : LR.M_NoPrizes);
        ResultDetail.Text = _resultStatusDetail ?? (_prizeResult is not null
            ? string.IsNullOrWhiteSpace(_prizeResult.Id)
                ? LR.M_DrawCompleted
                : string.Format(System.Globalization.CultureInfo.CurrentCulture, LR.M_PrizeId, _prizeResult.Id)
            : _lotteryCandidates.Count > 0
                ? string.Format(System.Globalization.CultureInfo.CurrentCulture, LR.M_CandidatePrizes, _lotteryCandidates.Count)
                : hasPrizes ? LR.M_RepeatLimitExhausted : LR.M_AddPrizesPrompt);
        UpdateResultImages(_configHandler.Data.LotterySettings.LotteryImage && _prizeResult is not null
            ? [_prizeResult]
            : []);
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

    private void UpdateResultImages(IEnumerable<IAttachableSettingsObject> records)
    {
        ResultImages.Children.Clear();
        foreach (var record in records)
        {
            var settings = record.GetAttachedObject<DrawImageAttachedSettings>(
                Guid.Parse(GlobalConstants.DrawImageAttachedSettings));
            if (settings is not { IsAttachSettingsEnabled: true } || string.IsNullOrWhiteSpace(settings.ImagePath) ||
                !File.Exists(settings.ImagePath))
                continue;
            try
            {
                ResultImages.Children.Add(new Border
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
        ResultImages.IsVisible = ResultImages.Children.Count != 0;
    }

    private void ClearResult()
    {
        _studentResult = [];
        _prizeResult = null;
        _resultStatusText = null;
        _resultStatusDetail = null;
    }

    private void EnsureRestartTemporaryRecordsCleared()
    {
        if (_configHandler.Data.RollCallSettings.ClearRecord == ClearRecordMode.Restarted)
            _temporaryRecordService.ClearStudentListOnce(GetStudentListName());
        if (_configHandler.Data.LotterySettings.ClearRecord == ClearRecordMode.Restarted)
            _temporaryRecordService.ClearPrizeListOnce(GetPrizeListName());
    }

    private static string FormatStudent(Student student) => string.IsNullOrWhiteSpace(student.Id)
        ? student.Name
        : string.IsNullOrWhiteSpace(student.Name) ? student.Id : $"{student.Id}  {student.Name}";
    private string GetStudentListName() => _profileService.StudentListConfig?.Name ?? MobileDefaults.ProfileName;
    private string GetPrizeListName() => _profileService.PrizeListConfig?.Name ?? MobileDefaults.ProfileName;

    private void OpenListManagement() => _ = _settingsNavigator.NavigateAsync(MobilePageIds.ListManagement);

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
