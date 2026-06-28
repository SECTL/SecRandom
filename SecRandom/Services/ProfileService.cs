using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Services.Config;
using SecRandom.Shared;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Services;

public class ProfileService : IProfileService
{
    private readonly ILogger<ProfileService> _logger = IAppHost.GetService<ILogger<ProfileService>>();
    private MainConfigHandler Config { get; } = IAppHost.GetService<MainConfigHandler>();

    public ProfileService()
    {
        var studentListName = ResolveProfileName("data", "list", "roll_call_list", Config.Data.RollCallSettings.DefaultClass);
        var prizeListName = ResolveProfileName("data", "list", "lottery_list", Config.Data.QuickDrawSettings.DefaultClass);

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

    private static string ResolveProfileName(string rootA, string rootB, string rootC, string preferredName)
    {
        var directory = Utils.GetDirectoryPath(rootA, rootB, rootC);
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
