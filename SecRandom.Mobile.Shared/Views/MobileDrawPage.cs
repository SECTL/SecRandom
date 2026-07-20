using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.Draw;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Views;
using SecRandom.Mobile.Controls;
using SecRandom.Shared.Models.Profile;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;
using AvaloniaButton = Avalonia.Controls.Button;

namespace SecRandom.Mobile.Views;

/// <summary>
/// 抽取主页：点名/抽奖双 surface。切换器使用 <see cref="MobileSegmentedControl"/>，
/// 结果区由 <see cref="MobileCard"/> 承载；抽取进行中用候选人名滚动微动效，
/// 完成时按「停滚动 → 写结果 → 结果揭示」顺序播放。核心抽取逻辑仍由 Core 会话决定。
/// </summary>
public sealed class MobileDrawPage : ViewBase
{
    // DrawOnce 是瞬时同步调用；这段最短滚动窗口只让候选人名滚动可被感知，不改变抽取时机与结果。
    private const int RollDurationMs = 800;

    private readonly IProfileService _profileService;
    private readonly IDrawTemporaryRecordService _temporaryRecordService;
    private readonly MainConfigHandler _configHandler;
    private readonly IRollCallSession _rollCallSession;
    private readonly ILotterySession _lotterySession;
    private readonly IViewEngine _viewEngine;
    private DrawSurface _drawSurface = DrawSurface.RollCall;
    private bool _firstRender = true;

    // 抽奖开关等能力投影统一经 MobileCapabilities 消费；保留 IFeatureAvailabilityService
    // 注入参数以维持视图引擎激活时的构造签名稳定。
    public MobileDrawPage(
        IProfileService profileService,
        IDrawTemporaryRecordService temporaryRecordService,
        IFeatureAvailabilityService featureAvailabilityService,
        MainConfigHandler configHandler,
        IRollCallSession rollCallSession,
        ILotterySession lotterySession,
        IViewEngine viewEngine)
    {
        _profileService = profileService;
        _temporaryRecordService = temporaryRecordService;
        _configHandler = configHandler;
        _rollCallSession = rollCallSession;
        _lotterySession = lotterySession;
        _viewEngine = viewEngine;
        EnsureRestartTemporaryRecordsCleared();
        Render();
    }

    // 抽取按钮状态机：可抽 / 抽取中 / 已抽完（结果揭示中，防重复点击）/ 无候选。
    // 禁用态视觉完全交给主题资源（accent 按钮的 disabled 样式），页面不硬编码颜色。
    private enum DrawButtonState
    {
        Ready,
        Drawing,
        Drawn,
        Empty
    }

    private void Render()
    {
        // 抽奖被关闭时 surface 回退到点名，胶囊中的抽奖项同时处于禁用态。
        if (_drawSurface == DrawSurface.Lottery && !MobileCapabilities.IsLotteryEnabled)
            _drawSurface = DrawSurface.RollCall;

        var selector = new MobileSegmentedControl();
        selector.SetItems([
            (LR.C_RollCall, (object)DrawSurface.RollCall, true),
            (LR.C_Lottery, (object)DrawSurface.Lottery, MobileCapabilities.IsLotteryEnabled)
        ]);
        if (_drawSurface == DrawSurface.Lottery)
            selector.Select(1);
        selector.SelectionChanged += (_, _) =>
        {
            if (selector.SelectedTag is DrawSurface surface)
                SelectSurface(surface);
        };

        ScrollViewer scroll = _drawSurface == DrawSurface.RollCall
            ? CreateRollCallContent(selector)
            : CreateLotteryContent(selector);
        Content = scroll;

        // 进入过渡只在页面首次渲染时播放；点名/抽奖 surface 互换不重复播放。
        if (_firstRender)
        {
            _firstRender = false;
            MobileAnimations.PlayPageEnter(scroll);
        }
    }

    private ScrollViewer CreateRollCallContent(Control selector)
    {
        var candidates = GetEligibleStudents();
        var hasStudents = _profileService.CurrentStudentList?.Students.Any(student => student.IsCandidate) ?? false;
        var (result, detail, card) = CreateResultCard(
            candidates.Count > 0 ? LR.M_Ready : hasStudents ? LR.M_NoEligibleCandidates : LR.M_NoStudents,
            candidates.Count > 0
                ? string.Format(System.Globalization.CultureInfo.CurrentCulture, LR.M_CandidateStudents, candidates.Count)
                : hasStudents ? LR.M_RepeatLimitExhausted : LR.M_AddStudentsPrompt,
            MobileTheme.Keys.PrimaryWash);

        var draw = MobileUi.CreatePrimaryButton(LR.C_DrawOne, candidates.Count > 0);
        draw.Click += async (_, _) => await DrawStudentAsync(candidates, result, detail, card, draw);

        return MobileUi.CreateScroll([
            selector,
            MobileUi.CreateLabel(LR.S_CurrentList),
            MobileUi.CreateTitle(_profileService.StudentListConfig?.Name ?? LR.M_DefaultList),
            card,
            draw,
            MobileUi.CreateSecondaryButton(LR.C_ManageStudentList, OpenListManagement)
        ]);
    }

    private ScrollViewer CreateLotteryContent(Control selector)
    {
        var candidates = GetEligiblePrizes();
        var hasPrizes = _profileService.CurrentPrizeList?.Prizes.Any(prize => prize.IsCandidate) ?? false;
        var (result, detail, card) = CreateResultCard(
            candidates.Count > 0 ? LR.M_Ready : hasPrizes ? LR.M_NoEligibleCandidates : LR.M_NoPrizes,
            candidates.Count > 0
                ? string.Format(System.Globalization.CultureInfo.CurrentCulture, LR.M_CandidatePrizes, candidates.Count)
                : hasPrizes ? LR.M_RepeatLimitExhausted : LR.M_AddPrizesPrompt,
            MobileTheme.Keys.WarmWash);

        var draw = MobileUi.CreatePrimaryButton(LR.C_DrawPrize, candidates.Count > 0);
        draw.Click += async (_, _) => await DrawPrizeAsync(candidates, result, detail, card, draw);

        return MobileUi.CreateScroll([
            selector,
            MobileUi.CreateLabel(LR.S_CurrentPool),
            MobileUi.CreateTitle(_profileService.PrizeListConfig?.Name ?? LR.M_DefaultPool),
            card,
            draw,
            MobileUi.CreateSecondaryButton(LR.C_ManagePrizePool, OpenListManagement)
        ]);
    }

    private static (TextBlock Result, TextBlock Detail, Control Card) CreateResultCard(
        string resultText, string detailText, string washResourceKey)
    {
        var result = new TextBlock
        {
            Text = resultText,
            FontSize = MobileTheme.FindDouble("MobileFontSizeResult", 34),
            FontWeight = FontWeight.SemiBold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        MobileTheme.BindBrush(result, TextBlock.ForegroundProperty, MobileTheme.Keys.Text);
        var detail = new TextBlock
        {
            Text = detailText,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        MobileTheme.BindBrush(detail, TextBlock.ForegroundProperty, MobileTheme.Keys.MutedText);
        return (result, detail, MobileUi.CreateResultPanel(result, detail, washResourceKey));
    }

    private async Task DrawStudentAsync(
        IReadOnlyList<Student> candidates, TextBlock result, TextBlock detail, Control card, AvaloniaButton draw)
    {
        SetDrawButtonState(draw, DrawButtonState.Drawing, LR.C_DrawOne);
        var names = GetStudentNames(candidates);
        if (names.Count > 0)
            MobileAnimations.StartNameRoll(result, names);

        // 核心抽取逻辑保持不变：同步、单次，由 Core 会话决定公平性并落临时记录/历史。
        var output = _rollCallSession.DrawOnce();
        await Task.Delay(RollDurationMs);

        // 揭示顺序固定为：停滚动 → 写结果 → 播放揭示动效。
        MobileAnimations.Cancel(result);
        SetDrawButtonState(draw, DrawButtonState.Drawn, LR.C_DrawOne);
        if (!output.IsSuccess || output.Result.Count == 0)
        {
            result.Text = GetDrawFailureText(output.Status);
            detail.Text = string.Empty;
        }
        else
        {
            var student = output.Result[0];
            result.Text = string.IsNullOrWhiteSpace(student.Name) ? student.Id : student.Name;
            detail.Text = string.IsNullOrWhiteSpace(student.Id)
                ? LR.M_DrawCompleted
                : string.Format(System.Globalization.CultureInfo.CurrentCulture, LR.M_StudentId, student.Id);
        }

        MobileAnimations.PlayResultReveal(card);
        SetDrawButtonState(draw, GetEligibleStudents().Count > 0 ? DrawButtonState.Ready : DrawButtonState.Empty, LR.C_DrawOne);
    }

    private async Task DrawPrizeAsync(
        IReadOnlyList<Prize> candidates, TextBlock result, TextBlock detail, Control card, AvaloniaButton draw)
    {
        SetDrawButtonState(draw, DrawButtonState.Drawing, LR.C_DrawPrize);
        var names = GetPrizeNames(candidates);
        if (names.Count > 0)
            MobileAnimations.StartNameRoll(result, names);

        var output = _lotterySession.DrawOnce();
        await Task.Delay(RollDurationMs);

        MobileAnimations.Cancel(result);
        SetDrawButtonState(draw, DrawButtonState.Drawn, LR.C_DrawPrize);
        if (!output.IsSuccess || output.Result.Count == 0)
        {
            result.Text = GetDrawFailureText(output.Status);
            detail.Text = string.Empty;
        }
        else
        {
            var prize = output.Result[0];
            result.Text = string.IsNullOrWhiteSpace(prize.Name) ? prize.Id : prize.Name;
            detail.Text = string.IsNullOrWhiteSpace(prize.Id)
                ? LR.M_DrawCompleted
                : string.Format(System.Globalization.CultureInfo.CurrentCulture, LR.M_PrizeId, prize.Id);
        }

        MobileAnimations.PlayResultReveal(card);
        SetDrawButtonState(draw, GetEligiblePrizes().Count > 0 ? DrawButtonState.Ready : DrawButtonState.Empty, LR.C_DrawPrize);
    }

    private static void SetDrawButtonState(AvaloniaButton button, DrawButtonState state, string readyText)
    {
        MobileAnimations.CrossFade(button, () =>
        {
            button.Content = state == DrawButtonState.Drawing ? LR.M_Drawing : readyText;
            button.IsEnabled = state == DrawButtonState.Ready;
        });
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
        return _rollCallSession.GetEligibleStudents().ToList();
    }

    private List<Prize> GetEligiblePrizes()
    {
        return _lotterySession.GetEligiblePrizes().ToList();
    }

    private static List<string> GetStudentNames(IReadOnlyList<Student> candidates) => candidates
        .Select(student => string.IsNullOrWhiteSpace(student.Name) ? student.Id : student.Name)
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .ToList();

    private static List<string> GetPrizeNames(IReadOnlyList<Prize> candidates) => candidates
        .Select(prize => string.IsNullOrWhiteSpace(prize.Name) ? prize.Id : prize.Name)
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .ToList();

    private string GetStudentListName() => _profileService.StudentListConfig?.Name ?? MobileDefaults.ProfileName;
    private string GetPrizeListName() => _profileService.PrizeListConfig?.Name ?? MobileDefaults.ProfileName;

    private void SelectSurface(DrawSurface surface)
    {
        if (surface == _drawSurface)
            return;
        if (surface == DrawSurface.Lottery && !MobileCapabilities.IsLotteryEnabled)
            return;

        _drawSurface = surface;
        Render();
    }

    private void OpenListManagement()
    {
        _ = _viewEngine.ShowAsync(MobileRoutes.ListManagement);
    }

    private static string GetDrawFailureText(DrawStatus status) => status switch
    {
        DrawStatus.NoCandidates => LR.M_NoCandidates,
        DrawStatus.NoEligibleCandidates => LR.M_NoEligibleCandidates,
        DrawStatus.RepeatLimitExhausted => LR.M_RepeatLimitExhausted,
        DrawStatus.InvalidWeight => LR.M_InvalidWeight,
        _ => LR.M_DrawFailed
    };
}
