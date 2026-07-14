using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Services.Config;
using SecRandom.Shared;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Services.Profiles;

public class ProfileService : IProfileService
{
    private readonly ILogger<ProfileService> _logger = IAppHost.GetService<ILogger<ProfileService>>();
    private MainConfigHandler Config { get; } = IAppHost.GetService<MainConfigHandler>();

    public ProfileService()
    {
        var studentListName = ResolveProfileName("list", "roll_call_list", Config.Data.RollCallSettings.DefaultClass);
        var prizeListName = ResolveProfileName("list", "lottery_list", Config.Data.LotterySettings.DefaultPool);

        StudentListConfig = new StudentListConfig(studentListName);
        StudentHistoryConfig = new StudentHistoryConfig(studentListName);
        PrizeListConfig = new PrizeListConfig(prizeListName);
        PrizeHistoryConfig = new PrizeHistoryConfig(prizeListName);

        _logger.LogInformation(
            "已加载默认档案：学生名单={StudentListName}，学生数量={StudentCount}，奖品池={PrizeListName}，奖品数量={PrizeCount}。",
            StudentListConfig.Name,
            CurrentStudentList?.Students.Count ?? 0,
            PrizeListConfig.Name,
            CurrentPrizeList?.Prizes.Count ?? 0);
    }

    public StudentList? CurrentStudentList => StudentListConfig?.Data;
    public StudentHistory? CurrentStudentHistory => StudentHistoryConfig?.Data;
    public PrizeList? CurrentPrizeList => PrizeListConfig?.Data;
    public PrizeHistory? CurrentPrizeHistory => PrizeHistoryConfig?.Data;

    public StudentListConfig? StudentListConfig { get; private set; }
    public StudentHistoryConfig? StudentHistoryConfig { get; private set; }
    public PrizeListConfig? PrizeListConfig { get; private set; }
    public PrizeHistoryConfig? PrizeHistoryConfig { get; private set; }

    public void LoadStudentProfile(string name, bool saveCurrent = true)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        if (StudentListConfig?.Name == name && StudentHistoryConfig?.Name == name)
        {
            StudentListConfig?.Reload();
            StudentHistoryConfig?.Reload();
            return;
        }

        if (saveCurrent)
        {
            StudentListConfig?.Save();
            StudentHistoryConfig?.Save();
        }

        StudentListConfig = new StudentListConfig(name);
        StudentHistoryConfig = new StudentHistoryConfig(name);

        _logger.LogInformation(
            "已切换点名名单：学生名单={StudentListName}，学生数量={StudentCount}。",
            name,
            CurrentStudentList?.Students.Count ?? 0);
    }

    public void LoadPrizeProfile(string name, bool saveCurrent = true)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        if (PrizeListConfig?.Name == name && PrizeHistoryConfig?.Name == name)
        {
            PrizeListConfig?.Reload();
            PrizeHistoryConfig?.Reload();
            return;
        }

        if (saveCurrent)
        {
            PrizeListConfig?.Save();
            PrizeHistoryConfig?.Save();
        }

        PrizeListConfig = new PrizeListConfig(name);
        PrizeHistoryConfig = new PrizeHistoryConfig(name);

        _logger.LogInformation(
            "已切换奖品池：奖品池={PrizeListName}，奖品数量={PrizeCount}。",
            name,
            CurrentPrizeList?.Prizes.Count ?? 0);
    }

    public void SaveProfile()
    {
        try
        {
            StudentListConfig?.Save();
            StudentHistoryConfig?.Save();
            PrizeListConfig?.Save();
            PrizeHistoryConfig?.Save();

            _logger.LogInformation(
                "档案已保存：学生名单={StudentListName}，奖品池={PrizeListName}。",
                StudentListConfig?.Name,
                PrizeListConfig?.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存档案失败。");
            throw;
        }
    }

    public void RecordStudentHistory(
        IReadOnlyList<Student> students,
        DateTime now,
        int requestedCount,
        string drawGroup = "",
        string drawGender = "",
        int drawMethod = 0,
        IReadOnlyDictionary<Student, double>? weights = null,
        string courseName = "")
    {
        var history = CurrentStudentHistory;
        if (history is null || students.Count == 0)
            return;

        history.TotalRounds++;
        history.TotalStats += students.Count;
        var drawRoundId = Guid.NewGuid().ToString("N");
        var allStudents = CurrentStudentList?.Students ?? [];
        var uniqueLegacyKeys = ProfileRecordIdentity.BuildUniqueStudentLegacyKeySet(allStudents);
        HashSet<string> drawnKeys = [];

        foreach (var student in students)
        {
            var key = ProfileRecordIdentity.EnsureRecordId(student);
            drawnKeys.Add(key);

            var item = ProfileRecordIdentity.GetStudentHistory(history, student, uniqueLegacyKeys.Contains);
            if (item is null)
            {
                item = new History();
                history.Students[key] = item;
            }

            item.TotalCount++;
            item.LastDrawnTime = now;
            item.RoundsMissed = 0;
            item.Histories.Add(new HistoryItem
            {
                RecordId = key,
                RecordNumber = student.Id,
                RecordName = student.Name,
                RecordGender = student.Gender,
                RecordGroup = student.Group,
                DrawTime = now,
                DrawRoundId = drawRoundId,
                DrawNumbers = requestedCount,
                DrawGroup = drawGroup,
                DrawGender = drawGender,
                DrawMethod = drawMethod,
                CourseName = courseName,
                Weight = weights?.GetValueOrDefault(student, 1) ?? 1
            });

            if (!string.IsNullOrWhiteSpace(student.Group))
                history.GroupStats[student.Group] = history.GroupStats.GetValueOrDefault(student.Group) + 1;
            if (!string.IsNullOrWhiteSpace(student.Gender))
                history.GenderStatus[student.Gender] = history.GenderStatus.GetValueOrDefault(student.Gender) + 1;
        }

        foreach (var student in allStudents)
        {
            var key = ProfileRecordIdentity.EnsureRecordId(student);
            if (drawnKeys.Contains(key))
                continue;

            var existing = ProfileRecordIdentity.GetStudentHistory(history, student, uniqueLegacyKeys.Contains);
            if (existing is not null)
                existing.RoundsMissed++;
        }

        StudentHistoryConfig?.Save();

        _logger.LogInformation(
            "已记录点名历史：学生名单={StudentListName}，抽取数量={StudentCount}。",
            StudentListConfig?.Name,
            students.Count);
    }

    public void RecordPrizeHistory(IReadOnlyList<Prize> prizes, DateTime now, int requestedCount)
    {
        var history = CurrentPrizeHistory;
        if (history is null || prizes.Count == 0)
            return;

        history.TotalRounds++;
        history.TotalStats += prizes.Count;
        var drawRoundId = Guid.NewGuid().ToString("N");
        var allPrizes = CurrentPrizeList?.Prizes ?? [];
        var uniqueLegacyKeys = ProfileRecordIdentity.BuildUniquePrizeLegacyKeySet(allPrizes);

        HashSet<string> drawnKeys = [];
        foreach (var prize in prizes)
        {
            var key = ProfileRecordIdentity.EnsureRecordId(prize);
            drawnKeys.Add(key);

            var item = ProfileRecordIdentity.GetPrizeHistory(history, prize, uniqueLegacyKeys.Contains);
            if (item is null)
            {
                item = new History();
                history.Prizes[key] = item;
            }

            item.TotalCount++;
            item.LastDrawnTime = now;
            item.RoundsMissed = 0;
            item.Histories.Add(new HistoryItem
            {
                RecordId = key,
                RecordNumber = prize.Id,
                RecordName = prize.Name,
                DrawTime = now,
                DrawRoundId = drawRoundId,
                DrawNumbers = requestedCount,
                DrawGroup = string.Empty,
                DrawGender = string.Empty,
                DrawMethod = 0,
                Weight = prize.Weight
            });
        }

        foreach (var prize in allPrizes)
        {
            var key = ProfileRecordIdentity.EnsureRecordId(prize);
            if (drawnKeys.Contains(key))
                continue;

            var existing = ProfileRecordIdentity.GetPrizeHistory(history, prize, uniqueLegacyKeys.Contains);
            if (existing is not null)
                existing.RoundsMissed++;
        }

        PrizeHistoryConfig?.Save();

        _logger.LogInformation(
            "已记录抽奖历史：奖品池={PrizeListName}，抽取数量={PrizeCount}。",
            PrizeListConfig?.Name,
            prizes.Count);
    }

    public void ClearCurrentStudentHistory()
    {
        var history = CurrentStudentHistory;
        if (history is null)
            return;

        history.TotalRounds = 0;
        history.TotalStats = 0;
        history.Students.Clear();
        history.GroupStats.Clear();
        history.GenderStatus.Clear();
        StudentHistoryConfig?.Save();

        _logger.LogInformation("已清空当前点名历史：学生名单={StudentListName}。", StudentHistoryConfig?.Name);
    }

    public void ClearCurrentPrizeHistory()
    {
        var history = CurrentPrizeHistory;
        if (history is null)
            return;

        history.TotalRounds = 0;
        history.TotalStats = 0;
        history.Prizes.Clear();
        history.GroupStats.Clear();
        history.GenderStatus.Clear();
        PrizeHistoryConfig?.Save();

        _logger.LogInformation("已清空当前抽奖历史：奖品池={PrizeListName}。", PrizeHistoryConfig?.Name);
    }

    private static string ResolveProfileName(string rootA, string rootB, string preferredName)
    {
        var directory = Utils.GetDirectoryPath(rootA, rootB);
        Directory.CreateDirectory(directory);

        if (!string.IsNullOrWhiteSpace(preferredName))
        {
            var preferredPath = Path.Combine(directory, $"{preferredName}.json");
            if (File.Exists(preferredPath))
                return preferredName;
        }

        var existing = Directory.GetFiles(directory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));

        return string.IsNullOrWhiteSpace(existing) ? "default" : existing;
    }
}
