using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
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

    private readonly MainConfigHandler _configHandler = IAppHost.GetService<MainConfigHandler>();
    private readonly IProfileService _profileService = IAppHost.GetService<IProfileService>();
    private readonly ILogger<DrawEngine> _logger = IAppHost.GetService<ILogger<DrawEngine>>();
    private readonly IRandomSource _randomSource;

    public DrawEngine(IRandomSource? randomSource = null)
    {
        _randomSource = randomSource ?? new CryptoRandomSource();
    }

    private MainConfigModel ConfigData => _configHandler.Data;
    private StudentHistory StudentHistory => _profileService.CurrentStudentHistory ?? new StudentHistory();
    private PrizeHistory PrizeHistory => _profileService.CurrentPrizeHistory ?? new PrizeHistory();
    private StudentList StudentList => _profileService.CurrentStudentList ?? new StudentList();
    private PrizeList PrizeList => _profileService.CurrentPrizeList ?? new PrizeList();

    public DrawResult<Student> DrawStudent(int count, Func<Student, bool> filter)
    {
        return DrawStudent(count, filter, DrawSettingsType.RollCall);
    }

    public DrawResult<Student> DrawStudent(int count, Func<Student, bool> filter, DrawSettingsType drawSettingsType)
    {
        var hasBaseCandidates = false;
        var repeatThreshold = GetStudentRepeatThreshold(drawSettingsType);
        var drawType = GetStudentDrawType(drawSettingsType);
        var historyCache = BuildStudentHistoryCache(StudentList.Students);
        _logger.LogInformation("开始学生抽取：请求数量={Count}，设置类型={SettingsType}，重复阈值={RepeatThreshold}，抽取类型={DrawType}.",
            count, drawSettingsType, repeatThreshold, drawType);

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

            var usable = FilterStudents(Filter1, count, historyCache);
            var weightedCandidates = drawType switch
            {
                DrawType.Fair => CalculateStudentWeight(usable, historyCache),
                DrawType.Random => usable.Select(s => new WeightedCandidate<Student> { Candidate = s, Weight = 1.0 }).ToList(),
                _ => usable.Select(s => new WeightedCandidate<Student> { Candidate = s, Weight = 1.0 }).ToList()
            };

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

    public DrawResult<Student> DrawStudent(int count, IReadOnlyCollection<Student> candidates)
    {
        var candidateSet = candidates as HashSet<Student> ?? candidates.ToHashSet();
        return DrawStudent(count, candidate => candidateSet.Contains(candidate));
    }

    public DrawResult<Student> DrawStudent(
        int count,
        IReadOnlyCollection<Student> candidates,
        DrawSettingsType drawSettingsType)
    {
        var candidateSet = candidates as HashSet<Student> ?? candidates.ToHashSet();
        return DrawStudent(count, candidate => candidateSet.Contains(candidate), drawSettingsType);
    }

    public DrawResult<Student> DrawPreparedStudents(
        int count,
        IReadOnlyCollection<Student> candidates,
        DrawSettingsType drawSettingsType)
    {
        var usable = candidates.Where(student => student.IsCandidate).ToList();
        if (usable.Count == 0)
            return new DrawResult<Student> { Status = DrawStatus.NoCandidates };
        if (count > usable.Count)
            return new DrawResult<Student> { Status = DrawStatus.RepeatLimitExhausted };

        var drawType = GetStudentDrawType(drawSettingsType);
        var historyCache = BuildStudentHistoryCache(usable);
        var weightedCandidates = drawType switch
        {
            DrawType.Fair => CalculateStudentWeight(usable, historyCache),
            DrawType.Random => usable.Select(s => new WeightedCandidate<Student> { Candidate = s, Weight = 1.0 }).ToList(),
            _ => usable.Select(s => new WeightedCandidate<Student> { Candidate = s, Weight = 1.0 }).ToList()
        };

        return DrawWithBehindSceneWeights(weightedCandidates, count);
    }

    private int GetStudentRepeatThreshold(DrawSettingsType drawSettingsType)
    {
        var (drawMode, halfRepeat) = drawSettingsType switch
        {
            DrawSettingsType.RollCall => (ConfigData.RollCallSettings.DrawMode, ConfigData.RollCallSettings.HalfRepeat),
            DrawSettingsType.QuickDraw => (ConfigData.QuickDrawSettings.DrawMode, ConfigData.QuickDrawSettings.HalfRepeat),
            _ => (ConfigData.RollCallSettings.DrawMode, ConfigData.RollCallSettings.HalfRepeat)
        };

        return drawMode switch
        {
            DrawMode.Repeat => 0,
            DrawMode.NoRepeat => 1,
            DrawMode.HalfRepeat => Math.Max(1, halfRepeat),
            _ => 1
        };
    }

    private DrawType GetStudentDrawType(DrawSettingsType drawSettingsType)
    {
        return drawSettingsType switch
        {
            DrawSettingsType.RollCall => ConfigData.RollCallSettings.DrawType,
            DrawSettingsType.QuickDraw => ConfigData.QuickDrawSettings.DrawType,
            _ => ConfigData.RollCallSettings.DrawType
        };
    }

    private int GetLotteryRepeatThreshold()
    {
        return ConfigData.LotterySettings.DrawMode switch
        {
            DrawMode.Repeat => 0,
            DrawMode.NoRepeat => 1,
            DrawMode.HalfRepeat => Math.Max(1, ConfigData.LotterySettings.HalfRepeat),
            _ => 1
        };
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

            var result = DrawWithBehindSceneWeights(weightedCandidates, count);
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

            var result = DrawWithBehindSceneWeights(weightedCandidates, count);
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
        if (ConfigData.LotterySettings.DrawType == LotteryDrawType.Count)
        {
            List<WeightedCandidate<Prize>> result = [];
            foreach (var prize in prizes)
            {
                var remainingCount = Math.Max(0, prize.Count - (historyCache.GetValueOrDefault(prize)?.TotalCount ?? 0));
                for (var i = 0; i < remainingCount; i++)
                    result.Add(new WeightedCandidate<Prize> { Candidate = prize, Weight = prize.Weight });
            }

            return result;
        }

        return prizes.Select(p => new WeightedCandidate<Prize> { Candidate = p, Weight = p.Weight }).ToList();
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
