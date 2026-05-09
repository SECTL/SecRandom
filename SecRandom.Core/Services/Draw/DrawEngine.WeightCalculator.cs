using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.Draw;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services.Draw;

public partial class DrawEngine
{
    /*
     * 算法来源：
     * https://github.com/SECTL/SecRandom/tree/v2.3.7/app/common/roll_call/roll_call_utils.py L280-L412
     * https://github.com/SECTL/SecRandom/tree/v2.3.7/app/common/history/weight_utils.py L272-L407
     */
    public List<WeightedCandidate<Student>> CalculateStudentWeight(List<Student> candidates) //根据给定学生集合计算每一位学生的
    {
        var baseWeight = ConfigData.FairDrawSettings.BaseWeight;
        var frequencyWeight = ConfigData.FairDrawSettings.FrequencyWeight;
        var groupWeight = ConfigData.FairDrawSettings.GroupWeight;
        var genderWeight = ConfigData.FairDrawSettings.GenderWeight;
        var timeWeight = ConfigData.FairDrawSettings.TimeWeight;

        //拿到所有符合条件的学生的历史记录（我太难了）
        //而且还得保证性能
        if (candidates.Count == 0)
            return [];

        var maxStudentDrawCount = candidates
            .Select(GetStudentDrawCount)
            .DefaultIfEmpty(0)
            .Max();

        var frequencyFunction = ConfigData.FairDrawSettings.FrequencyFunction;

        if (!ConfigData.FairDrawSettings.FairDraw) //公平抽取未开启则回退
            return candidates.Select(s => new WeightedCandidate<Student> { Candidate = s, Weight = baseWeight })
                .ToList();

        List<WeightedCandidate<Student>> calculatedStudentWeight = [];

        foreach (var candidate in candidates)
        {
            var currentCount = GetStudentDrawCount(candidate);

            //计算频率因子
            var freqencyIndex = 0.0;
            switch (frequencyFunction)
            {
                case FrequencyFunctionMode.Linear:
                    freqencyIndex = (1.0 * maxStudentDrawCount - currentCount * 1.0 + 1.0) /
                                    (1.0 * maxStudentDrawCount + 1);
                    break;
                case FrequencyFunctionMode.SquareRoot:
                    freqencyIndex = Math.Sqrt((maxStudentDrawCount * 1.0 + 1.0) / (currentCount * 1.0 + 1.0));
                    break;
                case FrequencyFunctionMode.Index:
                    if (maxStudentDrawCount == 0)
                        freqencyIndex = 1;
                    else
                        freqencyIndex = Math.Pow(Math.E,
                            (maxStudentDrawCount * 1.0 - currentCount * 1.0) / maxStudentDrawCount * 1.0);
                    break;
            }

            if (ConfigData.FairDrawSettings.CoolStartEnabled &&
                StudentHistory.TotalStats < ConfigData.FairDrawSettings.CoolStartRounds)
                freqencyIndex = Math.Max(0.8 + 0.2 * freqencyIndex, freqencyIndex);

            freqencyIndex *= frequencyWeight;

            //计算小组平衡
            var groupIndex = 0.0;
            if (ConfigData.FairDrawSettings.FairDrawGroup)
            {
                // 有效小组数量
                var effectiveGroupCount = StudentHistory.GroupStats.Count;
                var effectiveGroupMaxDrawCount = StudentHistory.GroupStats.Values.DefaultIfEmpty(0).Max();
                var currentGroupDrawCount = StudentHistory.GroupStats.GetValueOrDefault(candidate.Group);
                if (effectiveGroupCount > 3)
                {
                    groupIndex = 1.0 / (0.2 * currentGroupDrawCount + 1.0) * groupWeight;
                }
                else
                {
                    if (effectiveGroupMaxDrawCount == 0)
                        groupIndex = 0.2 * groupWeight;
                    else if (currentGroupDrawCount == 0)
                        groupIndex = 0.5 * groupWeight;
                    else
                        groupIndex = groupWeight * (1.0 - currentGroupDrawCount * 1.0 / effectiveGroupMaxDrawCount);
                }
            }

            //计算性别平衡
            var genderIndex = 0.0;
            if (ConfigData.FairDrawSettings.FairDrawGender)
            {
                // 有效小组数量
                var effectiveGenderCount = StudentHistory.GenderStatus.Count;
                var effectiveGenderMaxDrawCount = StudentHistory.GenderStatus.Values.DefaultIfEmpty(0).Max();
                var currentGenderDrawCount = StudentHistory.GenderStatus.GetValueOrDefault(candidate.Gender);
                if (effectiveGenderCount > 3)
                {
                    genderIndex = 1.0 / (0.2 * currentGenderDrawCount + 1.0) * genderWeight;
                }
                else
                {
                    if (effectiveGenderMaxDrawCount == 0)
                        genderIndex = 0.2 * genderWeight;
                    else if (currentGenderDrawCount == 0)
                        genderIndex = 0.5 * genderWeight;
                    else
                        genderIndex = genderWeight *
                                      (1.0 - currentGenderDrawCount * 1.0 / effectiveGenderMaxDrawCount);
                }
            }

            //计算时间因素
            var timeIndex = 0.0;
            if (ConfigData.FairDrawSettings.FairDrawTime)
            {
                var lastDrawnTime = GetStudentLastDrawnTime(candidate);
                if (lastDrawnTime != DateTime.MinValue)
                    timeIndex = Math.Min(1, (DateTime.Now - lastDrawnTime).Days / 30.0) * timeWeight;
            }

            //最后计算总的权重
            var totalIndex = baseWeight + freqencyIndex + groupIndex + genderIndex + timeIndex;
            calculatedStudentWeight.Add(new WeightedCandidate<Student> { Candidate = candidate, Weight = totalIndex });
        }

        //实行屏蔽保护
        var minWeight = ConfigData.FairDrawSettings.MinWeight;
        var maxWeight = ConfigData.FairDrawSettings.MaxWeight;
        if (ConfigData.FairDrawSettings.ShieldEnabled)
        {
            var currentTime = DateTime.Now;
            calculatedStudentWeight = calculatedStudentWeight.Select(ws =>
            {
                var prevDrawTime = GetStudentLastDrawnTime(ws.Candidate);
                if (prevDrawTime == DateTime.MinValue)
                    return new WeightedCandidate<Student> { Candidate = ws.Candidate, Weight = ws.Weight };

                var shieldTimeSpan = ConfigData.FairDrawSettings.ShieldTimeUnit switch
                {
                    ShieldTimeUnit.Hours => TimeSpan.FromHours(ConfigData.FairDrawSettings.ShieldTime),
                    ShieldTimeUnit.Minutes => TimeSpan.FromMinutes(ConfigData.FairDrawSettings.ShieldTime),
                    _ => TimeSpan.FromSeconds(ConfigData.FairDrawSettings.ShieldTime)
                };
                if (currentTime - prevDrawTime < shieldTimeSpan)
                    return new WeightedCandidate<Student> { Candidate = ws.Candidate, Weight = minWeight / 10.0 };
                return new WeightedCandidate<Student> { Candidate = ws.Candidate, Weight = ws.Weight };
            }).ToList();
        }

        //夹范围，保留两位小数
        calculatedStudentWeight = calculatedStudentWeight.Select(ws => new WeightedCandidate<Student>
        {
            Candidate = ws.Candidate,
            Weight = Math.Round(
                Math.Max(
                    minWeight / 10.0,
                    Math.Min(maxWeight, ws.Weight)
                )
                , 2)
        }).ToList();

        return calculatedStudentWeight;
    }

    private DateTime GetStudentLastDrawnTime(Student student)
    {
        if (!string.IsNullOrWhiteSpace(student.Id) &&
            StudentHistory.Students.TryGetValue(student.Id, out var historyById))
            return historyById.LastDrawnTime;

        if (!string.IsNullOrWhiteSpace(student.Name) &&
            StudentHistory.Students.TryGetValue(student.Name, out var historyByName))
            return historyByName.LastDrawnTime;

        return DateTime.MinValue;
    }
}