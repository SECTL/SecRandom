using System;
using System.Collections.Generic;
using System.IO;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Services.Config;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Services.FirstRun;

public sealed class OobeDataSetupService(MainConfigHandler configHandler, IProfileService profileService)
{
    public void SaveStudentList(string name, IReadOnlyList<Student> students)
    {
        name = ValidateName(name, "班级名称");
        var listConfig = new StudentListConfig(name);
        listConfig.Data.Students.Clear();
        foreach (var student in students)
        {
            student.RecordId = student.RecordId == Guid.Empty ? Guid.NewGuid() : student.RecordId;
            listConfig.Data.Students.Add(student);
        }
        listConfig.Save();
        configHandler.Data.RollCallSettings.DefaultClass = name;
        configHandler.Save();
        profileService.LoadStudentProfile(name, saveCurrent: false);
    }

    public void SavePrizeList(string name, IReadOnlyList<Prize> prizes)
    {
        name = ValidateName(name, "奖品池名称");
        var listConfig = new PrizeListConfig(name);
        listConfig.Data.Prizes.Clear();
        foreach (var prize in prizes)
        {
            prize.RecordId = prize.RecordId == Guid.Empty ? Guid.NewGuid() : prize.RecordId;
            listConfig.Data.Prizes.Add(prize);
        }
        listConfig.Save();
        configHandler.Data.LotterySettings.DefaultPool = name;
        configHandler.Save();
        profileService.LoadPrizeProfile(name, saveCurrent: false);
    }

    private static string ValidateName(string name, string displayName)
    {
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidOperationException($"{displayName}不能为空，且不能包含文件名非法字符。");
        return name;
    }
}
