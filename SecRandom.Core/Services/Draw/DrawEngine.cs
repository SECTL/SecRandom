using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Interfaces;
using SecRandom.Core.Models;
using SecRandom.Core.Models.AttachedSettings;
using SecRandom.Core.Models.Draw;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Draw.Exceptions;
using SecRandom.Shared.Extensions;
using SecRandom.Shared.Interfaces;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services.Draw;

public partial class DrawEngine
{
    private static readonly Guid BehindSceneAttachedSettingsId =
        Guid.Parse(GlobalConstants.BehindSceneAttachedSettings);

    private readonly MainConfigHandler _configHandler;
    private readonly IProfileService _profileService;
    private readonly ILogger<DrawEngine> _logger;
    private readonly IRandomSource _randomSource;
    private readonly IRollCallAlgorithmRegistry _rollCallAlgorithms;
    private readonly ILotteryAlgorithmRegistry _lotteryAlgorithms;

    public DrawEngine(
        MainConfigHandler configHandler,
        IProfileService profileService,
        ILogger<DrawEngine> logger,
        IRandomSource? randomSource = null,
        IRollCallAlgorithmRegistry? rollCallAlgorithms = null,
        ILotteryAlgorithmRegistry? lotteryAlgorithms = null)
    {
        _configHandler = configHandler;
        _profileService = profileService;
        _logger = logger;
        _randomSource = randomSource ?? new CryptoRandomSource();
        _rollCallAlgorithms = rollCallAlgorithms ?? new RollCallAlgorithmRegistry(
            [new FairRollCallAlgorithm(), new RandomRollCallAlgorithm()]);
        _lotteryAlgorithms = lotteryAlgorithms ?? new LotteryAlgorithmRegistry(
            [new InventoryLotteryAlgorithm(), new WeightedLotteryAlgorithm()]);
    }

    private MainConfigModel ConfigData => _configHandler.Data;
    private StudentHistory StudentHistory => _profileService.CurrentStudentHistory ?? new StudentHistory();
    private PrizeHistory PrizeHistory => _profileService.CurrentPrizeHistory ?? new PrizeHistory();
    private StudentList StudentList => _profileService.CurrentStudentList ?? new StudentList();
    private PrizeList PrizeList => _profileService.CurrentPrizeList ?? new PrizeList();

    public DrawResult<Student> DrawStudent(int count, Func<Student, bool> filter, string courseName = "")
    {
        return DrawStudent(count, filter, DrawSettingsType.RollCall, courseName);
    }

    public DrawResult<Student> DrawStudent(
        int count,
        Func<Student, bool> filter,
        DrawSettingsType drawSettingsType,
        string courseName = "")
    {
        return DrawStudent(count, filter, drawSettingsType, StudentDrawExecutionPolicy.DesktopConfigured(
            GetStudentDrawType(drawSettingsType),
            ConfigData.FairDrawSettings,
            GetStudentAlgorithmId(drawSettingsType)), courseName);
    }

    internal DrawResult<Student> DrawStudent(
        int count,
        Func<Student, bool> filter,
        DrawSettingsType drawSettingsType,
        StudentDrawExecutionPolicy executionPolicy,
        string courseName = "")
    {
        var hasBaseCandidates = false;
        var repeatThreshold = GetStudentRepeatThreshold(drawSettingsType);
        var historyCache = BuildStudentHistoryCache(StudentList.Students, courseName);
        _logger.LogInformation("开始学生抽取：请求数量={Count}，设置类型={SettingsType}，重复阈值={RepeatThreshold}，抽取类型={DrawType}.",
            count, drawSettingsType, repeatThreshold, executionPolicy.DrawType);

        try
        {
            hasBaseCandidates = StudentList.Students.Any(student => student.IsCandidate && filter(student));

            bool Filter1(Student student)
            {
                if (!student.IsCandidate || !filter(student))
                    return false;

                if (repeatThreshold <= 0)
                    return true;

                return (historyCache.GetValueOrDefault(student)?.TotalCount ?? 0) < repeatThreshold;
            }

            var usable = FilterStudents(Filter1, count, historyCache, executionPolicy);
            var weightedCandidates = BuildStudentWeightedCandidates(usable, historyCache, executionPolicy, courseName);

            var result = DrawWithBehindSceneWeights(weightedCandidates, count);
            LogDrawResult("学生抽取", result.Status, count, usable.Count, result.Result.Count);
            return result;
        }
        catch (RepeatLimitExhaustedException)
        {
            _logger.LogWarning("点名抽取失败：重复限制耗尽。请求数量={Count}，存在基础候选={HasBaseCandidates}，重复阈值={RepeatThreshold}.",
                count, hasBaseCandidates, repeatThreshold);
            return new DrawResult<Student> { Status = DrawStatus.RepeatLimitExhausted };
        }
        catch (CandidateNotFoundException)
        {
            if (hasBaseCandidates && repeatThreshold > 0)
            {
                _logger.LogWarning("点名抽取失败：基础筛选有候选，但重复限制后无可用候选。请求数量={Count}，重复阈值={RepeatThreshold}.",
                    count, repeatThreshold);
                return new DrawResult<Student> { Status = DrawStatus.RepeatLimitExhausted };
            }

            _logger.LogWarning("点名抽取失败：未找到候选学生。请求数量={Count}.", count);
            return new DrawResult<Student> { Status = DrawStatus.NoCandidates };
        }
    }

    public DrawResult<Student> DrawStudent(int count, IReadOnlyCollection<Student> candidates, string courseName = "")
    {
        var candidateSet = candidates as HashSet<Student> ?? candidates.ToHashSet();
        return DrawStudent(count, candidate => candidateSet.Contains(candidate), courseName);
    }

    public DrawResult<Student> DrawStudent(
        int count,
        IReadOnlyCollection<Student> candidates,
        DrawSettingsType drawSettingsType,
        string courseName = "")
    {
        var candidateSet = candidates as HashSet<Student> ?? candidates.ToHashSet();
        return DrawStudent(count, candidate => candidateSet.Contains(candidate), drawSettingsType, courseName);
    }

    public DrawResult<Student> DrawPreparedStudents(
        int count,
        IReadOnlyCollection<Student> candidates,
        DrawSettingsType drawSettingsType,
        string courseName = "")
    {
        return DrawPreparedStudents(count, candidates, drawSettingsType, StudentDrawExecutionPolicy.DesktopConfigured(
            GetStudentDrawType(drawSettingsType),
            ConfigData.FairDrawSettings,
            GetStudentAlgorithmId(drawSettingsType)), courseName);
    }

    internal DrawResult<Student> DrawPreparedStudentsWithMobileDesktopDefaults(
        int count,
        IReadOnlyCollection<Student> candidates,
        DrawSettingsType drawSettingsType,
        DrawType drawType,
        string courseName = "")
    {
        return DrawPreparedStudents(
            count,
            candidates,
            drawSettingsType,
            StudentDrawExecutionPolicy.MobileDesktopDefaultsV1(drawType),
            courseName);
    }

    internal DrawPreparedStudentsSnapshot PrepareStudentsForDraw(
        int count,
        IReadOnlyCollection<Student> candidates,
        StudentDrawExecutionPolicy executionPolicy,
        string courseName = "")
    {
        var preparedCandidates = candidates.Where(student => student.IsCandidate).ToList();
        var historyCache = BuildStudentHistoryCache(preparedCandidates, courseName);
        var usable = FilterPreparedStudents(preparedCandidates, count, historyCache, executionPolicy);
        var weightedCandidates = BuildStudentWeightedCandidates(usable, historyCache, executionPolicy, courseName);
        return new DrawPreparedStudentsSnapshot(usable, weightedCandidates, historyCache);
    }

    internal DrawPreparedStudentsSnapshot PrepareStudentsForMobileDesktopDefaults(
        int count,
        IReadOnlyCollection<Student> candidates,
        DrawSettingsType drawSettingsType,
        DrawType drawType,
        string courseName = "")
    {
        return PrepareStudentsForDraw(
            count,
            candidates,
            StudentDrawExecutionPolicy.MobileDesktopDefaultsV1(drawType),
            courseName);
    }

    internal DrawResult<Student> DrawPreparedStudents(
        int count,
        IReadOnlyCollection<Student> candidates,
        DrawSettingsType drawSettingsType,
        StudentDrawExecutionPolicy executionPolicy,
        string courseName = "")
    {
        try
        {
            var prepared = PrepareStudentsForDraw(count, candidates, executionPolicy, courseName);
            return DrawPreparedStudents(prepared, count);
        }
        catch (CandidateNotFoundException)
        {
            return new DrawResult<Student> { Status = DrawStatus.NoCandidates };
        }
        catch (RepeatLimitExhaustedException)
        {
            return new DrawResult<Student> { Status = DrawStatus.RepeatLimitExhausted };
        }
    }

    internal DrawResult<Student> DrawPreparedStudents(DrawPreparedStudentsSnapshot prepared, int count)
    {
        return DrawWithBehindSceneWeights(prepared.WeightedCandidates, count);
    }

    private int GetStudentRepeatThreshold(DrawSettingsType drawSettingsType)
    {
        var (drawMode, halfRepeat) = drawSettingsType switch
        {
            DrawSettingsType.RollCall => (ConfigData.RollCallSettings.DrawMode, ConfigData.RollCallSettings.HalfRepeat),
            DrawSettingsType.QuickDraw => (ConfigData.QuickDrawSettings.DrawMode, ConfigData.QuickDrawSettings.HalfRepeat),
            _ => (ConfigData.RollCallSettings.DrawMode, ConfigData.RollCallSettings.HalfRepeat)
        };

        return DrawRepeatPolicy.ResolveThreshold(drawMode, halfRepeat);
    }

    private DrawType GetStudentDrawType(DrawSettingsType drawSettingsType)
    {
        if (drawSettingsType == DrawSettingsType.RollCall)
            return string.Equals(ConfigData.RollCallSettings.AlgorithmId, "builtin.random", StringComparison.OrdinalIgnoreCase)
                ? DrawType.Random
                : DrawType.Fair;
        if (drawSettingsType == DrawSettingsType.QuickDraw)
            return string.Equals(ConfigData.QuickDrawSettings.AlgorithmId, "builtin.random", StringComparison.OrdinalIgnoreCase)
                ? DrawType.Random
                : DrawType.Fair;

        return drawSettingsType switch
        {
            DrawSettingsType.RollCall => ConfigData.RollCallSettings.DrawType,
            DrawSettingsType.QuickDraw => ConfigData.QuickDrawSettings.DrawType,
            _ => ConfigData.RollCallSettings.DrawType
        };
    }

    private string? GetStudentAlgorithmId(DrawSettingsType drawSettingsType) => drawSettingsType switch
    {
        DrawSettingsType.RollCall => ConfigData.RollCallSettings.AlgorithmId,
        DrawSettingsType.QuickDraw => ConfigData.QuickDrawSettings.AlgorithmId,
        _ => null
    };

    private List<WeightedCandidate<Student>> BuildStudentWeightedCandidates(
        List<Student> usable,
        IReadOnlyDictionary<Student, History> historyCache,
        StudentDrawExecutionPolicy executionPolicy,
        string courseName)
    {
        var algorithm = _rollCallAlgorithms.Resolve(executionPolicy.AlgorithmId ??
            (executionPolicy.DrawType == DrawType.Fair ? "builtin.fair" : "builtin.random"));
        return algorithm.BuildCandidates(this, usable, historyCache, executionPolicy.FairDrawSettings, courseName)
            .ToList();
    }

    private int GetLotteryRepeatThreshold()
    {
        return DrawRepeatPolicy.ResolveThreshold(ConfigData.LotterySettings.DrawMode, ConfigData.LotterySettings.HalfRepeat);
    }

    public DrawResult<Prize> DrawPrize(int count, Func<Prize, bool> filter)
    {
        var historyCache = BuildPrizeHistoryCache(PrizeList.Prizes);
        _logger.LogInformation("开始奖品抽取：请求数量={Count}，抽取模式={DrawMode}，抽取类型={DrawType}.",
            count, ConfigData.LotterySettings.DrawMode, ConfigData.LotterySettings.DrawType);

        try
        {
            var usable = FilterPrizes(filter, count, historyCache);
            var weightedCandidates = BuildPrizeCandidates(usable, historyCache);

            if (count > weightedCandidates.Count)
                throw new RepeatLimitExhaustedException();

            var result = DrawPrizeCandidates(weightedCandidates, count);
            LogDrawResult("奖品抽取", result.Status, count, usable.Count, result.Result.Count);
            return result;
        }
        catch (RepeatLimitExhaustedException)
        {
            _logger.LogWarning("奖品抽取失败：重复限制或奖品剩余数量耗尽。请求数量={Count}.", count);
            return new DrawResult<Prize> { Status = DrawStatus.RepeatLimitExhausted };
        }
        catch (CandidateNotFoundException)
        {
            _logger.LogWarning("奖品抽取失败：未找到候选奖品。请求数量={Count}.", count);
            return new DrawResult<Prize> { Status = DrawStatus.NoCandidates };
        }
    }

    public DrawResult<Prize> DrawPrizeWithTemporaryCounts(
        int count,
        Func<Prize, bool> filter,
        IReadOnlyDictionary<string, int> temporaryCounts)
    {
        var historyCache = BuildPrizeTemporaryHistoryCache(PrizeList.Prizes, temporaryCounts);
        _logger.LogInformation("开始奖品抽取：请求数量={Count}，抽取模式={DrawMode}，抽取类型={DrawType}，使用临时记录。",
            count, ConfigData.LotterySettings.DrawMode, ConfigData.LotterySettings.DrawType);

        try
        {
            var usable = FilterPrizes(filter, count, historyCache);
            var weightedCandidates = BuildPrizeCandidates(usable, historyCache);

            if (count > weightedCandidates.Count)
                throw new RepeatLimitExhaustedException();

            var result = DrawPrizeCandidates(weightedCandidates, count);
            LogDrawResult("奖品抽取", result.Status, count, usable.Count, result.Result.Count);
            return result;
        }
        catch (RepeatLimitExhaustedException)
        {
            _logger.LogWarning("奖品抽取失败：临时重复限制或奖品剩余数量耗尽。请求数量={Count}.", count);
            return new DrawResult<Prize> { Status = DrawStatus.RepeatLimitExhausted };
        }
        catch (CandidateNotFoundException)
        {
            _logger.LogWarning("奖品抽取失败：未找到候选奖品。请求数量={Count}.", count);
            return new DrawResult<Prize> { Status = DrawStatus.NoCandidates };
        }
    }

    private static Dictionary<Prize, History> BuildPrizeTemporaryHistoryCache(
        IEnumerable<Prize> prizes,
        IReadOnlyDictionary<string, int> temporaryCounts)
    {
        Dictionary<Prize, History> result = [];
        foreach (var prize in prizes)
        {
            var recordId = ProfileRecordIdentity.EnsureRecordId(prize);
            var count = temporaryCounts.GetValueOrDefault(recordId);
            if (count <= 0)
                continue;

            result[prize] = new History { TotalCount = count };
        }

        return result;
    }

    private List<WeightedCandidate<Prize>> BuildPrizeCandidates(
        List<Prize> prizes,
        IReadOnlyDictionary<Prize, History> historyCache)
    {
        var fallback = ConfigData.LotterySettings.DrawType == LotteryDrawType.Count
            ? "builtin.inventory"
            : "builtin.weighted";
        return _lotteryAlgorithms.Resolve(ConfigData.LotterySettings.AlgorithmId ?? fallback)
            .BuildCandidates(prizes, historyCache).ToList();
    }

    private DrawResult<Prize> DrawPrizeCandidates(IReadOnlyList<WeightedCandidate<Prize>> candidates, int count)
    {
        if (ConfigData.LotterySettings.DrawType != LotteryDrawType.Count
            || candidates.Any(candidate => GetBehindSceneSettings(candidate.Candidate) is { IsAttachSettingsEnabled: true }))
            return DrawWithBehindSceneWeights(candidates, count);

        if (count > candidates.Count)
            return new DrawResult<Prize> { Status = DrawStatus.NoEligibleCandidates };

        var tickets = candidates.Select(candidate => candidate.Candidate).ToList();
        for (var index = 0; index < count; index++)
        {
            var selectedIndex = index + _randomSource.NextInt32(tickets.Count - index);
            (tickets[index], tickets[selectedIndex]) = (tickets[selectedIndex], tickets[index]);
        }

        return new DrawResult<Prize>
        {
            Status = DrawStatus.Success,
            Result = tickets.Take(count).ToList()
        };
    }

    private DrawResult<TCandidate> DrawWithBehindSceneWeights<TCandidate>(
        IReadOnlyList<WeightedCandidate<TCandidate>> weightedCandidates,
        int count)
        where TCandidate : IAttachableSettingsObject
    {
        var drawEngine = new WeightedDrawEngine<TCandidate>(_randomSource);
        List<WeightedCandidate<TCandidate>> guaranteedCandidates = [];
        List<WeightedCandidate<TCandidate>> effectiveCandidates = [];
        HashSet<TCandidate> guaranteedCandidateSet = [];

        foreach (var candidate in weightedCandidates)
        {
            var settings = GetBehindSceneSettings(candidate.Candidate);
            if (guaranteedCandidateSet.Contains(candidate.Candidate))
                continue;

            if (settings is not { IsAttachSettingsEnabled: true })
            {
                effectiveCandidates.Add(new WeightedCandidate<TCandidate>
                {
                    Candidate = candidate.Candidate,
                    Weight = candidate.Weight
                });
                continue;
            }

            var probability = Math.Clamp(settings.Probability, 0, 100);
            if (probability >= 100)
            {
                guaranteedCandidateSet.Add(candidate.Candidate);
                guaranteedCandidates.Add(new WeightedCandidate<TCandidate>
                {
                    Candidate = candidate.Candidate,
                    Weight = 1.0
                });
                continue;
            }

            if (probability <= 0)
                continue;

            effectiveCandidates.Add(new WeightedCandidate<TCandidate>
            {
                Candidate = candidate.Candidate,
                Weight = candidate.Weight * (probability / 100.0)
            });
        }

        if (guaranteedCandidates.Count >= count)
            return drawEngine.Draw(new DrawRequest<TCandidate> { Candidates = guaranteedCandidates, Count = count });

        var result = guaranteedCandidates.Select(c => c.Candidate).ToList();
        var remainingCount = count - result.Count;
        if (remainingCount <= 0)
            return new DrawResult<TCandidate> { Result = result, Status = DrawStatus.Success };

        if (effectiveCandidates.Count < remainingCount)
            return new DrawResult<TCandidate> { Status = DrawStatus.NoEligibleCandidates };

        var restResult = drawEngine.Draw(new DrawRequest<TCandidate>
        {
            Candidates = effectiveCandidates,
            Count = remainingCount
        });

        if (!restResult.IsSuccess)
            return new DrawResult<TCandidate> { Status = restResult.Status };

        result.AddRange(restResult.Result);
        return new DrawResult<TCandidate> { Result = result, Status = DrawStatus.Success };
    }

    private void LogDrawResult(
        string operation,
        DrawStatus status,
        int requestedCount,
        int candidateCount,
        int resultCount)
    {
        if (status == DrawStatus.Success)
        {
            _logger.LogInformation("{Operation}完成：请求数量={RequestedCount}，候选数量={CandidateCount}，结果数量={ResultCount}.",
                operation, requestedCount, candidateCount, resultCount);
            return;
        }

        _logger.LogWarning("{Operation}未成功：状态={Status}，请求数量={RequestedCount}，候选数量={CandidateCount}.",
            operation, status, requestedCount, candidateCount);
    }

    private static BehindSceneAttachedSettings? GetBehindSceneSettings(IAttachableSettingsObject candidate)
    {
        return candidate.GetAttachedObject<BehindSceneAttachedSettings>(BehindSceneAttachedSettingsId);
    }

}
