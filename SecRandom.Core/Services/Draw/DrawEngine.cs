using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
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

            //先筛选出符合条件的学生
            var usable = filterStudents(filter, count);
            //TODO: 等待半重复功能加进来之后实现半重复抽取功能的筛选
            // filter = student => !filter1(student) || !studentHistory.Students.TryGetValue(student.Id, out var h) ||
            //                     (h.TotalCount >= 0);
            // usable = filterStudents(filter, count);

            var weightedCandidates = CalculateStudentWeight(usable);
            var drawEngine = new WeightedDrawEngine<Student>(new CryptoRandomSource());


            //TODO: 等待加入抽取设置开关后再执行具体判断是否需要幕后设置
            if (true)
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
}
