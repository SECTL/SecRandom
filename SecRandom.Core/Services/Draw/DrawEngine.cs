using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models;
using SecRandom.Core.Models.AttachedSettings;
using SecRandom.Core.Models.Draw;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Draw.Exceptions;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services.Draw;

public partial class DrawEngine
{
    private readonly MainConfigHandler configHandler = IAppHost.GetService<MainConfigHandler>();
    private readonly IProfileService profileService = IAppHost.GetService<IProfileService>();

    private MainConfigModel configData => configHandler.Data;
    private StudentHistory studentHistory => profileService.CurrentStudentHistory ?? new StudentHistory();
    private PrizeHistory prizeHistory => profileService.CurrentPrizeHistory ?? new PrizeHistory();
    private StudentList studentList => profileService.CurrentStudentList ?? new StudentList();
    private PrizeList prizeList => profileService.CurrentPrizeList ?? new PrizeList();

    public DrawResult<Student> DrawStudent(int count, Func<Student, bool> filter)
    {
        try
        {
            var repeatThreshold = getRollCallRepeatThreshold();

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
            var drawEngine = new WeightedDrawEngine<Student>(new CryptoRandomSource());

            if (weightedCandidates.Count > 0)
            {
                List<WeightedCandidate<Student>> tempWeightedCandidates = [];
                List<WeightedCandidate<Student>> mustStudent = []; //必中学生列表，稍后result中将会加入他们
                foreach (var candidate in weightedCandidates)
                {
                    //接下来拿掉必中，直接加入到result中
                    var currentBehindSceneSettings =
                        candidate.Candidate.GetAttachedObject<BehindSceneAttachedSettings>(
                            Guid.Parse(GlobalConstants.BehindSceneAttachedSettings));
                    if (currentBehindSceneSettings is { IsAttachSettingsEnabled: true, Probability: >= 1000 })
                    {
                        mustStudent.Add(
                            new WeightedCandidate<Student>
                                { Candidate = candidate.Candidate, Weight = 1.0 }); //为超出截断做准备
                        continue;
                    }
                    else if (currentBehindSceneSettings is { IsAttachSettingsEnabled: true, Probability: <= 0 })
                        continue; //必不中直接丢弃

                    applyBehindSceneWeight(candidate);
                    tempWeightedCandidates.Add(candidate);
                }

                if (mustStudent.Count >= count)
                {
                    return drawEngine.Draw(new DrawRequest<Student>
                    {
                        Candidates = mustStudent,
                        Count = count
                    });
                }

                //首先给所有的必中加入到最终的result中
                var result = mustStudent.Select(s => s.Candidate).ToList();
                //接着排除掉所有的必中学生
                count -= mustStudent.Count;
                //最后在应用过权重的剩余学生中抽取
                var tempResult = drawEngine.Draw(new DrawRequest<Student>
                {
                    Candidates = tempWeightedCandidates,
                    Count = count
                });
                //处理异常
                if (!tempResult.IsSuccess)
                    return new DrawResult<Student> { Status = tempResult.Status };
                //构建结果（拼接）
                result.AddRange(tempResult.Result);
                return new DrawResult<Student>
                {
                    Result = result,
                    Status = DrawStatus.Success
                };
            }

            return drawEngine.Draw(new DrawRequest<Student>
            {
                Candidates = weightedCandidates,
                Count = count
            });
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
            return new DrawResult<Student>
            {
                Status = DrawStatus.NoCandidates
            };
        }
    }

    private int getRollCallRepeatThreshold()
    {
        return configData.RollCallSettings.DrawMode switch
        {
            DrawMode.Repeat => 0,
            DrawMode.NoRepeat => 1,
            DrawMode.HalfRepeat => Math.Max(1, configData.RollCallSettings.HalfRepeat),
            _ => 1
        };
    }
}
