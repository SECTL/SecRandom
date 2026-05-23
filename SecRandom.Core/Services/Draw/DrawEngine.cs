using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums.Configs;
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

    private MainConfigModel ConfigData => _configHandler.Data;
    private StudentHistory StudentHistory => _profileService.CurrentStudentHistory ?? new StudentHistory();
    private PrizeHistory PrizeHistory => _profileService.CurrentPrizeHistory ?? new PrizeHistory();
    private StudentList StudentList => _profileService.CurrentStudentList ?? new StudentList();
    private PrizeList PrizeList => _profileService.CurrentPrizeList ?? new PrizeList();

    public DrawResult<Student> DrawStudent(int count, Func<Student, bool> filter)
    {
        var hasBaseCandidates = false;
        var repeatThreshold = GetRollCallRepeatThreshold();
        try
        {
            hasBaseCandidates = StudentList.Students.Any(filter);

            //先添加半重复的条件
            bool Filter1(Student student)
            {
                if (!filter(student))
                    return false;

                if (repeatThreshold <= 0)
                    return true;

                return GetStudentDrawCount(student) < repeatThreshold;
            }

            //先筛选出符合条件的学生
            var usable = FilterStudents(Filter1, count);


            var weightedCandidates = ConfigData.RollCallSettings.DrawType switch
            {
                DrawType.Fair => CalculateStudentWeight(usable),
                DrawType.Random => usable
                    .Select(s => new WeightedCandidate<Student> { Candidate = s, Weight = 1.0 })
                    .ToList(),
                _ => usable
                    .Select(s => new WeightedCandidate<Student> { Candidate = s, Weight = 1.0 })
                    .ToList()
            };
            return DrawWithBehindSceneWeights(weightedCandidates, count);
        }
        catch (RepeatLimitExhaustedException)
        {
            return new DrawResult<Student>
            {
                Status = DrawStatus.RepeatLimitExhausted
            };
        }
        catch (CandidateNotFoundException)
        {
            if (hasBaseCandidates && repeatThreshold > 0)
                return new DrawResult<Student>
                {
                    Status = DrawStatus.RepeatLimitExhausted
                };

            return new DrawResult<Student>
            {
                Status = DrawStatus.NoCandidates
            };
        }
    }

    private int GetRollCallRepeatThreshold()
    {
        return ConfigData.RollCallSettings.DrawMode switch
        {
            DrawMode.Repeat => 0,
            DrawMode.NoRepeat => 1,
            DrawMode.HalfRepeat => Math.Max(1, ConfigData.RollCallSettings.HalfRepeat),
            _ => 1
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
        try
        {
            var usable = FilterPrizes(filter, count);
            var weightedCandidates = BuildPrizeCandidates(usable);

            if (count > weightedCandidates.Count)
                throw new RepeatLimitExhaustedException();

            return DrawWithBehindSceneWeights(weightedCandidates, count);
        }
        catch (RepeatLimitExhaustedException)
        {
            return new DrawResult<Prize>
            {
                Status = DrawStatus.RepeatLimitExhausted
            };
        }
        catch (CandidateNotFoundException)
        {
            return new DrawResult<Prize>
            {
                Status = DrawStatus.NoCandidates
            };
        }
    }

    private List<WeightedCandidate<Prize>> BuildPrizeCandidates(List<Prize> prizes)
    {
        if (ConfigData.LotterySettings.DrawType == LotteryDrawType.Count)
        {
            List<WeightedCandidate<Prize>> result = [];
            foreach (var prize in prizes)
            {
                var remainingCount = Math.Max(0, prize.Count - GetPrizeDrawCount(prize));
                for (var i = 0; i < remainingCount; i++)
                    result.Add(new WeightedCandidate<Prize>
                    {
                        Candidate = prize,
                        Weight = prize.Weight
                    });
            }

            return result;
        }

        return prizes.Select(p => new WeightedCandidate<Prize>
        {
            Candidate = p,
            Weight = p.Weight
        }).ToList();
    }

    private DrawResult<TCandidate> DrawWithBehindSceneWeights<TCandidate>(
        IReadOnlyList<WeightedCandidate<TCandidate>> weightedCandidates,
        int count)
        where TCandidate : IAttachableSettingsObject
    {
        var drawEngine = new WeightedDrawEngine<TCandidate>(new CryptoRandomSource());
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

            if (settings.Probability >= 1000)
            {
                guaranteedCandidateSet.Add(candidate.Candidate);
                guaranteedCandidates.Add(new WeightedCandidate<TCandidate>
                {
                    Candidate = candidate.Candidate,
                    Weight = 1.0
                });
                continue;
            }

            if (settings.Probability <= 0)
                continue;

            effectiveCandidates.Add(new WeightedCandidate<TCandidate>
            {
                Candidate = candidate.Candidate,
                Weight = candidate.Weight * settings.Probability
            });
        }

        if (guaranteedCandidates.Count >= count)
            return drawEngine.Draw(new DrawRequest<TCandidate>
            {
                Candidates = guaranteedCandidates,
                Count = count
            });

        var result = guaranteedCandidates.Select(c => c.Candidate).ToList();
        var remainingCount = count - result.Count;
        if (remainingCount <= 0)
            return new DrawResult<TCandidate>
            {
                Result = result,
                Status = DrawStatus.Success
            };

        if (effectiveCandidates.Count < remainingCount)
            return new DrawResult<TCandidate>
            {
                Status = DrawStatus.NoEligibleCandidates
            };

        var restResult = drawEngine.Draw(new DrawRequest<TCandidate>
        {
            Candidates = effectiveCandidates,
            Count = remainingCount
        });

        if (!restResult.IsSuccess)
            return new DrawResult<TCandidate> { Status = restResult.Status };

        result.AddRange(restResult.Result);
        return new DrawResult<TCandidate>
        {
            Result = result,
            Status = DrawStatus.Success
        };
    }

    private static BehindSceneAttachedSettings? GetBehindSceneSettings(IAttachableSettingsObject candidate)
    {
        return candidate.GetAttachedObject<BehindSceneAttachedSettings>(BehindSceneAttachedSettingsId);
    }
}
