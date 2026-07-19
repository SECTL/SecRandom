using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.Draw;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Draw;
using SecRandom.Shared.Models.Profile;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;
using AvaloniaButton = Avalonia.Controls.Button;
using AvaloniaOrientation = Avalonia.Layout.Orientation;

namespace SecRandom.Mobile.Views;

internal sealed class MobileDrawPage : UserControl
{
    private readonly IProfileService _profileService;
    private readonly IDrawTemporaryRecordService _temporaryRecordService;
    private readonly MainConfigHandler _configHandler;
    private readonly DrawEngine _drawEngine;
    private readonly IRollCallSession _rollCallSession;
    private readonly ILotterySession _lotterySession;

    internal MobileDrawPage(
        IProfileService profileService,
        IDrawTemporaryRecordService temporaryRecordService,
        IFeatureAvailabilityService featureAvailabilityService,
        MainConfigHandler configHandler,
        DrawEngine drawEngine,
        IRollCallSession rollCallSession,
        ILotterySession lotterySession,
        DrawSurface drawSurface,
        Action<DrawSurface> selectSurface,
        Action openListManagement)
    {
        _profileService = profileService;
        _temporaryRecordService = temporaryRecordService;
        _configHandler = configHandler;
        _drawEngine = drawEngine;
        _rollCallSession = rollCallSession;
        _lotterySession = lotterySession;
        EnsureRestartTemporaryRecordsCleared();

        var rollCall = MobileUi.CreateSegmentButton(LR.C_RollCall, drawSurface == DrawSurface.RollCall, true);
        var lottery = MobileUi.CreateSegmentButton(LR.C_Lottery, drawSurface == DrawSurface.Lottery, false);
        lottery.IsEnabled = featureAvailabilityService.IsLotteryEnabled;
        rollCall.Click += (_, _) => selectSurface(DrawSurface.RollCall);
        lottery.Click += (_, _) => selectSurface(DrawSurface.Lottery);

        var selector = new Border
        {
            Background = MobileTheme.SurfaceMuted,
            CornerRadius = new Avalonia.CornerRadius(18),
            Padding = new Avalonia.Thickness(3),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new StackPanel
            {
                Orientation = AvaloniaOrientation.Horizontal,
                Spacing = 2,
                Children = { rollCall, lottery }
            }
        };

        Content = drawSurface == DrawSurface.RollCall
            ? CreateRollCallContent(selector, openListManagement)
            : CreateLotteryContent(selector, openListManagement);
    }

    private Control CreateRollCallContent(Control selector, Action openListManagement)
    {
        var candidates = GetEligibleStudents();
        var hasStudents = (_profileService.CurrentStudentList?.Students.Any(student => student.IsCandidate) ?? false);
        var result = new TextBlock
        {
            Text = candidates.Count > 0 ? LR.M_Ready : hasStudents ? LR.M_NoEligibleCandidates : LR.M_NoStudents,
            FontSize = 34,
            FontWeight = FontWeight.SemiBold,
            Foreground = MobileTheme.Text,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        var detail = new TextBlock
        {
            Text = candidates.Count > 0
                ? string.Format(System.Globalization.CultureInfo.CurrentCulture, LR.M_CandidateStudents, candidates.Count)
                : hasStudents ? LR.M_RepeatLimitExhausted : LR.M_AddStudentsPrompt,
            Foreground = MobileTheme.MutedText,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        var draw = MobileUi.CreatePrimaryButton(LR.C_DrawOne, candidates.Count > 0);
        draw.Click += (_, _) => DrawStudent(candidates, result, detail, draw);

        return MobileUi.CreateScroll([
            selector,
            MobileUi.CreateLabel(LR.S_CurrentList),
            MobileUi.CreateTitle(_profileService.StudentListConfig?.Name ?? LR.M_DefaultList),
            MobileUi.CreateResultPanel(result, detail, MobileTheme.PrimaryWash),
            draw,
            MobileUi.CreateSecondaryButton(LR.C_ManageStudentList, openListManagement)
        ]);
    }

    private Control CreateLotteryContent(Control selector, Action openListManagement)
    {
        var candidates = GetEligiblePrizes();
        var hasPrizes = (_profileService.CurrentPrizeList?.Prizes.Any(prize => prize.IsCandidate) ?? false);
        var result = new TextBlock
        {
            Text = candidates.Count > 0 ? LR.M_Ready : hasPrizes ? LR.M_NoEligibleCandidates : LR.M_NoPrizes,
            FontSize = 34,
            FontWeight = FontWeight.SemiBold,
            Foreground = MobileTheme.Text,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        var detail = new TextBlock
        {
            Text = candidates.Count > 0
                ? string.Format(System.Globalization.CultureInfo.CurrentCulture, LR.M_CandidatePrizes, candidates.Count)
                : hasPrizes ? LR.M_RepeatLimitExhausted : LR.M_AddPrizesPrompt,
            Foreground = MobileTheme.MutedText,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        var draw = MobileUi.CreatePrimaryButton(LR.C_DrawPrize, candidates.Count > 0);
        draw.Click += (_, _) => DrawPrize(candidates, result, detail, draw);

        return MobileUi.CreateScroll([
            selector,
            MobileUi.CreateLabel(LR.S_CurrentPool),
            MobileUi.CreateTitle(_profileService.PrizeListConfig?.Name ?? LR.M_DefaultPool),
            MobileUi.CreateResultPanel(result, detail, MobileTheme.WarmWash),
            draw,
            MobileUi.CreateSecondaryButton(LR.C_ManagePrizePool, openListManagement)
        ]);
    }

    private void DrawStudent(IReadOnlyList<Student> candidates, TextBlock result, TextBlock detail, AvaloniaButton draw)
    {
        draw.IsEnabled = false;
        try
        {
            var output = _rollCallSession.DrawOnce();
            if (!output.IsSuccess || output.Result.Count == 0)
            {
                result.Text = GetDrawFailureText(output.Status);
                detail.Text = string.Empty;
                return;
            }

            var student = output.Result[0];
            result.Text = string.IsNullOrWhiteSpace(student.Name) ? student.Id : student.Name;
            detail.Text = string.IsNullOrWhiteSpace(student.Id)
                ? LR.M_DrawCompleted
                : string.Format(System.Globalization.CultureInfo.CurrentCulture, LR.M_StudentId, student.Id);
        }
        finally
        {
            draw.IsEnabled = GetEligibleStudents().Count > 0;
        }
    }

    private void DrawPrize(IReadOnlyList<Prize> candidates, TextBlock result, TextBlock detail, AvaloniaButton draw)
    {
        draw.IsEnabled = false;
        try
        {
            var output = _lotterySession.DrawOnce();
            if (!output.IsSuccess || output.Result.Count == 0)
            {
                result.Text = GetDrawFailureText(output.Status);
                detail.Text = string.Empty;
                return;
            }

            var prize = output.Result[0];
            result.Text = string.IsNullOrWhiteSpace(prize.Name) ? prize.Id : prize.Name;
            detail.Text = string.IsNullOrWhiteSpace(prize.Id)
                ? LR.M_DrawCompleted
                : string.Format(System.Globalization.CultureInfo.CurrentCulture, LR.M_PrizeId, prize.Id);
        }
        finally
        {
            draw.IsEnabled = GetEligiblePrizes().Count > 0;
        }
    }

    private void EnsureRestartTemporaryRecordsCleared()
    {
        if (_configHandler.Data.RollCallSettings.ClearRecord == ClearRecordMode.Restarted)
            _temporaryRecordService.ClearStudentListOnce(GetStudentListName());
        if (_configHandler.Data.LotterySettings.ClearRecord == ClearRecordMode.Restarted)
            _temporaryRecordService.ClearPrizeListOnce(GetPrizeListName());
    }

    private List<Student> GetEligibleStudents()
    {
        var students = _profileService.CurrentStudentList?.Students.Where(student => student.IsCandidate).ToList() ?? [];
        var threshold = GetRepeatThreshold(_configHandler.Data.RollCallSettings.DrawMode, _configHandler.Data.RollCallSettings.HalfRepeat);
        if (threshold <= 0)
            return students;

        var counts = _temporaryRecordService.GetStudentCounts(GetStudentListName(), string.Empty, string.Empty);
        return students.Where(student => counts.GetValueOrDefault(ProfileRecordIdentity.EnsureRecordId(student)) < threshold).ToList();
    }

    private List<Prize> GetEligiblePrizes()
    {
        var prizes = _profileService.CurrentPrizeList?.Prizes.Where(prize => prize.IsCandidate).ToList() ?? [];
        var counts = _temporaryRecordService.GetPrizeCounts(GetPrizeListName());
        if (_configHandler.Data.LotterySettings.DrawType == LotteryDrawType.Count)
            return prizes.Where(prize => prize.Count - counts.GetValueOrDefault(ProfileRecordIdentity.EnsureRecordId(prize)) > 0).ToList();

        var threshold = GetRepeatThreshold(_configHandler.Data.LotterySettings.DrawMode, _configHandler.Data.LotterySettings.HalfRepeat);
        return threshold <= 0
            ? prizes
            : prizes.Where(prize => counts.GetValueOrDefault(ProfileRecordIdentity.EnsureRecordId(prize)) < threshold).ToList();
    }

    private string GetStudentListName() => _profileService.StudentListConfig?.Name ?? "default";
    private string GetPrizeListName() => _profileService.PrizeListConfig?.Name ?? "default";

    private static int GetRepeatThreshold(DrawMode mode, int halfRepeat) => mode switch
    {
        DrawMode.Repeat => 0,
        DrawMode.NoRepeat => 1,
        DrawMode.HalfRepeat => Math.Max(1, halfRepeat),
        _ => 1
    };

    private static string GetDrawFailureText(DrawStatus status) => status switch
    {
        DrawStatus.NoCandidates => LR.M_NoCandidates,
        DrawStatus.NoEligibleCandidates => LR.M_NoEligibleCandidates,
        DrawStatus.RepeatLimitExhausted => LR.M_RepeatLimitExhausted,
        DrawStatus.InvalidWeight => LR.M_InvalidWeight,
        _ => LR.M_DrawFailed
    };
}
