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
using SecRandom.Shared.Models;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services.Draw;

public partial class DrawEngine
{
    private static readonly Guid BehindSceneAttachedSettingsId = Guid.Parse(GlobalConstants.BehindSceneAttachedSettings);

    private readonly MainConfigHandler configHandler = IAppHost.GetService<MainConfigHandler>();
    private readonly IProfileService profileService = IAppHost.GetService<IProfileService>();

    private MainConfigModel configData => configHandler.Data;
    private StudentHistory studentHistory => profileService.CurrentStudentHistory ?? new StudentHistory();
    private PrizeHistory prizeHistory => profileService.CurrentPrizeHistory ?? new PrizeHistory();
    private StudentList studentList => profileService.CurrentStudentList ?? new StudentList();
    private PrizeList prizeList => profileService.CurrentPrizeList ?? new PrizeList();

    public DrawResult<Student> DrawStudent(int count, Func<Student, bool> filter)
    {
        var hasBaseCandidates = false;
        var repeatThreshold = getRollCallRepeatThreshold();
        try
        {
            hasBaseCandidates = studentList.Students.Any(filter);

            //先添加半重复的条件
            bool filter1(Student student)
            {
                if (!filter(student))
                    return false;

                if (repeatThreshold <= 0)
                    return true;

                return getStudentDrawCount(student) < repeatThreshold;
            }

            //先筛选出符合条件的学生
            var usable = filterStudents(filter1, count);


            var weightedCandidates = configData.RollCallSettings.DrawType switch
            {
                DrawType.Fair => CalculateStudentWeight(usable),
                DrawType.Random => usable
                    .Select(s => new WeightedCandidate<Student> { Candidate = s, Weight = 1.0 })
                    .ToList(),
                _ => usable
                    .Select(s => new WeightedCandidate<Student> { Candidate = s, Weight = 1.0 })
                    .ToList()
            };
            var result = drawWithBehindSceneWeights(weightedCandidates, count);

            return result;
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
            {
                return new DrawResult<Student>
                {
                    Status = DrawStatus.RepeatLimitExhausted
                };
            }

            return new DrawResult<Student>
            {
                Status = DrawStatus.NoCandidates
            };
        }
    }

    private int getRollCallRepeatThreshold()
    {
        return configData.DefaultDrawSettings.DrawMode switch
        {
            DrawMode.Repeat => 0,
            DrawMode.NoRepeat => 1,
            DrawMode.HalfRepeat => Math.Max(1, configData.DefaultDrawSettings.HalfRepeat),
            _ => 1
        };
    }

    private int getLotteryRepeatThreshold()
    {
        return configData.LotterySettings.DrawMode switch
        {
            DrawMode.Repeat => 0,
            DrawMode.NoRepeat => 1,
            DrawMode.HalfRepeat => Math.Max(1, configData.LotterySettings.HalfRepeat),
            _ => 1
        };
    }

    public DrawResult<Prize> DrawPrize(int count, Func<Prize, bool> filter)
    {
        try
        {
            var usable = filterPrizes(filter, count);
            var weightedCandidates = buildPrizeCandidates(usable);

            if (count > weightedCandidates.Count)
                throw new RepeatLimitExhaustedException();

            var result = drawWithBehindSceneWeights(weightedCandidates, count);
            if (result.IsSuccess)
                recordPrizeHistory(result.Result, weightedCandidates);

            return result;
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

    private List<WeightedCandidate<Prize>> buildPrizeCandidates(List<Prize> prizes)
    {
        if (configData.LotterySettings.DrawType == LotteryDrawType.Count)
        {
            List<WeightedCandidate<Prize>> result = [];
            foreach (var prize in prizes)
            {
                var remainingCount = Math.Max(0, prize.Count - getPrizeDrawCount(prize));
                for (var i = 0; i < remainingCount; i++)
                {
                    result.Add(new WeightedCandidate<Prize>
                    {
                        Candidate = prize,
                        Weight = prize.Weight
                    });
                }
            }

            return result;
        }

        return prizes.Select(p => new WeightedCandidate<Prize>
        {
            Candidate = p,
            Weight = p.Weight
        }).ToList();
    }

    private DrawResult<TCandidate> drawWithBehindSceneWeights<TCandidate>(
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
            var settings = getBehindSceneSettings(candidate.Candidate);
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
        {
            return drawEngine.Draw(new DrawRequest<TCandidate>
            {
                Candidates = guaranteedCandidates,
                Count = count
            });
        }

        var result = guaranteedCandidates.Select(c => c.Candidate).ToList();
        var remainingCount = count - result.Count;
        if (remainingCount <= 0)
        {
            return new DrawResult<TCandidate>
            {
                Result = result,
                Status = DrawStatus.Success
            };
        }

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

    private static BehindSceneAttachedSettings? getBehindSceneSettings(IAttachableSettingsObject candidate)
    {
        return candidate.GetAttachedObject<BehindSceneAttachedSettings>(BehindSceneAttachedSettingsId);
    }

    private void recordPrizeHistory(
        IReadOnlyList<Prize> drawnPrizes,
        IReadOnlyList<WeightedCandidate<Prize>> weightedCandidates)
    {
        var currentTime = DateTime.Now;
        var weightByPrize = weightedCandidates
            .GroupBy(c => c.Candidate)
            .ToDictionary(g => g.Key, g => g.First().Weight);

        foreach (var prize in drawnPrizes)
        {
            if (string.IsNullOrWhiteSpace(prize.Name))
                continue;

            var history = getOrCreatePrizeHistory(prize.Name);
            history.TotalCount++;
            history.LastDrawnTime = currentTime;
            history.RoundsMissed = 0;
            history.Histories.Add(new HistoryItem
            {
                DrawTime = currentTime,
                DrawNumbers = drawnPrizes.Count,
                Weight = weightByPrize.GetValueOrDefault(prize, prize.Weight)
            });
        }

        foreach (var prize in prizeList.Prizes.Where(p => !drawnPrizes.Contains(p)))
        {
            if (string.IsNullOrWhiteSpace(prize.Name) || !prizeHistory.Prizes.TryGetValue(prize.Name, out var history))
                continue;

            history.RoundsMissed++;
        }

        prizeHistory.TotalRounds++;
        prizeHistory.TotalStats += drawnPrizes.Count;
        profileService.SaveProfile();
    }

    private History getOrCreateStudentHistory(string key)
    {
        if (!studentHistory.Students.TryGetValue(key, out var history))
        {
            history = new History();
            studentHistory.Students[key] = history;
        }

        return history;
    }

    private History getOrCreatePrizeHistory(string key)
    {
        if (!prizeHistory.Prizes.TryGetValue(key, out var history))
        {
            history = new History();
            prizeHistory.Prizes[key] = history;
        }

        return history;
    }

    private static string getStudentHistoryKey(Student student)
    {
        return !string.IsNullOrWhiteSpace(student.Id) ? student.Id : student.Name;
    }
}
