using System;
using System.Collections.Generic;
using System.Linq;
using SecRandom.Core.Models.Profile;
using SecRandom.Enums;
using SecRandom.Models.Draw;

namespace SecRandom.Services.Draw;

public partial class DrawEngine
{
    /*
     * 算法来源：
     * https://github.com/SECTL/SecRandom/tree/v2.3.7/app/common/roll_call/roll_call_utils.py L280-L412
     * https://github.com/SECTL/SecRandom/tree/v2.3.7/app/common/history/weight_utils.py L272-L407
     */
    public List<WeightedCandidate<Student>> CalculateStudentWeight(List<Student> candidates) //根据给定学生集合计算每一位学生的
    {
        double baseWeight = configData.FairDrawSettings.BaseWeight;
        double frequencyWeight = configData.FairDrawSettings.FrequencyWeight;
        double groupWeight = configData.FairDrawSettings.GroupWeight;
        double genderWeight = configData.FairDrawSettings.GenderWeight;
        double timeWeight = configData.FairDrawSettings.TimeWeight;

        //拿到所有符合条件的学生的历史记录（我太难了）
        //而且还得保证性能
        HashSet<string> validStudentName = candidates.Select(s => s.Name).ToHashSet();
        int maxStudentDrawCount = studentHistory.Students
            .Where(s => validStudentName.Contains(s.Key))
            .Select(s => s.Value.TotalCount)
            .DefaultIfEmpty(0)
            .Max();

        var frequencyFunction = configData.FairDrawSettings.FrequencyFunction;

        if (configData.FairDrawSettings.FairDraw == false) //公平抽取未开启则回退
            return candidates.Select(s => new WeightedCandidate<Student> { Candidate = s, Weight = 1.0 }).ToList();

        List<WeightedCandidate<Student>> calculatedStudentWeight = [];

        foreach (var candidate in candidates)
        {
            int currentCount = studentHistory.Students.GetValueOrDefault(candidate.Id)?.TotalCount ?? 0;

            //计算频率因子
            double freqencyIndex = 0.0;
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

            if (configData.FairDrawSettings.CoolStartEnabled)
                freqencyIndex = Math.Min(0.8 + 0.2 * freqencyIndex, freqencyIndex) * frequencyWeight;

            //计算小组平衡
            double groupIndex = 0.0;
            if (configData.FairDrawSettings.FairDrawGroup)
            {
                // 有效小组数量
                int effectiveGroupCount = studentHistory.GroupStats.Count;
                int effectiveGroupMaxDrawCount = studentHistory.GroupStats.Values.DefaultIfEmpty(0).Max();
                int currentGroupDrawCount = studentHistory.GroupStats.GetValueOrDefault(candidate.Group);
                if (effectiveGroupCount > 3)
                    groupIndex = 1.0 / (0.2 * currentGroupDrawCount + 1.0) * groupWeight;
                else
                {
                    if (effectiveGroupMaxDrawCount == 0)
                        groupIndex = 0.2 * groupWeight;
                    else if (currentGroupDrawCount == 0)
                        groupIndex = 0.5 * groupWeight;
                    else
                        groupIndex = groupWeight * (1.0 - (currentGroupDrawCount * 1.0 / effectiveGroupMaxDrawCount));
                }
            }

            //计算性别平衡
            double genderIndex = 0.0;
            if (configData.FairDrawSettings.FairDrawGender)
            {
                // 有效小组数量
                int effectiveGenderCount = studentHistory.GenderStatus.Count;
                int effectiveGenderMaxDrawCount = studentHistory.GenderStatus.Select(s => s.Value).ToList().Max();
                int currentGenderDrawCount = studentHistory.GenderStatus.GetValueOrDefault(candidate.Gender);
                if (effectiveGenderCount > 3)
                    genderIndex = 1.0 / (0.2 * currentGenderDrawCount + 1.0) * groupWeight;
                else
                {
                    if (effectiveGenderMaxDrawCount == 0)
                        genderIndex = 0.2 * groupWeight;
                    else if (currentGenderDrawCount == 0)
                        genderIndex = 0.5 * groupWeight;
                    else
                        genderIndex = groupWeight *
                                      (1.0 - (currentGenderDrawCount * 1.0 / effectiveGenderMaxDrawCount));
                }
            }

            //计算时间因素
            double timeIndex = 0.0;
            if (configData.FairDrawSettings.FairDrawTime)
            {
                timeIndex = Math.Min(1,
                    (DateTime.Now - studentHistory.Students.GetValueOrDefault(candidate.Id)?.LastDrawnTime ??
                     new TimeSpan(int.MaxValue)).Days / 30.0);
            }

            //最后计算总的权重
            double totalIndex = freqencyIndex + groupIndex + genderIndex + timeIndex;
            calculatedStudentWeight.Add(new WeightedCandidate<Student> { Candidate = candidate, Weight = totalIndex });
        }

        //实行屏蔽保护
        double minWeight = calculatedStudentWeight.Min(s => s.Weight);
        double maxWeight = calculatedStudentWeight.Max(s => s.Weight);
        if (configData.FairDrawSettings.ShieldEnabled)
        {
            var currentTime = DateTime.Now;
            calculatedStudentWeight = calculatedStudentWeight.Select(ws =>
            {
                var prevDrawTime = studentHistory.Students.TryGetValue(ws.Candidate.Id, out var h)
                    ? h.LastDrawnTime
                    : DateTime.Now;
                var shieldTimeSpan = configData.FairDrawSettings.ShieldTimeUnit switch
                {
                    ShieldTimeUnit.Hours => TimeSpan.FromHours(configData.FairDrawSettings.ShieldTime),
                    ShieldTimeUnit.Minutes => TimeSpan.FromMinutes(configData.FairDrawSettings.ShieldTime),
                    ShieldTimeUnit.Seconds => TimeSpan.FromSeconds(configData.FairDrawSettings.ShieldTime),
                    _ => TimeSpan.FromDays(configData.FairDrawSettings.ShieldTime)
                };
                if (currentTime - prevDrawTime < shieldTimeSpan)
                    return new WeightedCandidate<Student> { Candidate = ws.Candidate, Weight = minWeight / 10.0 };
                else
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
}
