using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Services.Config;
using SecRandom.Shared;
using SecRandom.Shared.Extensions;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services.Profiles;

internal sealed class ProfileCatalogManager(
    ConfigServiceBase configService,
    IProfileService profileService,
    MainConfigHandler configHandler,
    ILogger<ProfileCatalogManager> logger) : IProfileCatalogManager
{
    public IReadOnlyList<string> GetStudentListNames() => GetListNames("roll_call_list");

    public IReadOnlyList<string> GetPrizeListNames() => GetListNames("lottery_list");

    public bool StudentListExists(string name) => ContainsName(GetStudentListNames(), name);

    public bool PrizeListExists(string name) => ContainsName(GetPrizeListNames(), name);

    public bool CreateStudentList(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || StudentListExists(name))
            return false;

        // FileConfigService 在缺失时先写入 fallback，这里再显式保存一次与桌面既有行为一致。
        new StudentListConfig(name, logger, configService).Save();
        logger.LogInformation("已创建点名名单：名单={ListName}。", name);
        return true;
    }

    public bool CreatePrizeList(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || PrizeListExists(name))
            return false;

        new PrizeListConfig(name, logger, configService).Save();
        logger.LogInformation("已创建奖品池：奖品池={ListName}。", name);
        return true;
    }

    public bool RenameStudentList(string oldName, string newName) =>
        RenameList(oldName, newName, "roll_call_list", "roll_call_history", "roll_call_record_", isStudent: true);

    public bool RenamePrizeList(string oldName, string newName) =>
        RenameList(oldName, newName, "lottery_list", "lottery_history", "lottery_record_", isStudent: false);

    public bool DeleteStudentList(string name, bool deleteHistory)
    {
        if (string.IsNullOrWhiteSpace(name) || !StudentListExists(name))
            return false;

        DeleteFile(Utils.GetFilePath("list", "roll_call_list", $"{name}.json"));
        if (deleteHistory)
            DeleteFile(Utils.GetFilePath("history", "roll_call_history", $"{name}.json"));

        if (string.Equals(profileService.StudentListConfig?.Name, name, StringComparison.Ordinal))
        {
            var nextName = GetStudentListNames().FirstOrDefault();
            if (nextName is not null)
                profileService.LoadStudentProfile(nextName, saveCurrent: false);
        }

        logger.LogInformation("已删除点名名单：名单={ListName}，连带历史={DeleteHistory}。", name, deleteHistory);
        return true;
    }

    public bool DeletePrizeList(string name, bool deleteHistory)
    {
        if (string.IsNullOrWhiteSpace(name) || !PrizeListExists(name))
            return false;

        DeleteFile(Utils.GetFilePath("list", "lottery_list", $"{name}.json"));
        if (deleteHistory)
            DeleteFile(Utils.GetFilePath("history", "lottery_history", $"{name}.json"));

        if (string.Equals(profileService.PrizeListConfig?.Name, name, StringComparison.Ordinal))
        {
            var nextName = GetPrizeListNames().FirstOrDefault();
            if (nextName is not null)
                profileService.LoadPrizeProfile(nextName, saveCurrent: false);
        }

        logger.LogInformation("已删除奖品池：奖品池={ListName}，连带历史={DeleteHistory}。", name, deleteHistory);
        return true;
    }

    public StudentList? LoadStudentList(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !StudentListExists(name))
            return null;

        try
        {
            var data = new StudentListConfig(name, logger, configService).Data;
            if (SortInPlace(data.Students))
                configService.SaveConfig(data);
            return data;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "读取点名名单失败：名单={ListName}。", name);
            return null;
        }
    }

    public PrizeList? LoadPrizeList(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !PrizeListExists(name))
            return null;

        try
        {
            var data = new PrizeListConfig(name, logger, configService).Data;
            if (SortInPlace(data.Prizes))
                configService.SaveConfig(data);
            return data;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "读取奖品池失败：奖品池={ListName}。", name);
            return null;
        }
    }

    public bool SaveStudentList(StudentList list)
    {
        ArgumentNullException.ThrowIfNull(list);
        if (string.IsNullOrWhiteSpace(list.Name))
            return false;

        ProfileRecordIdentity.Normalize(list);
        SortInPlace(list.Students);
        configService.SaveConfig(list);
        SyncActiveStudentProfile(list.Name);
        return true;
    }

    public bool SavePrizeList(PrizeList list)
    {
        ArgumentNullException.ThrowIfNull(list);
        if (string.IsNullOrWhiteSpace(list.Name))
            return false;

        ProfileRecordIdentity.Normalize(list);
        SortInPlace(list.Prizes);
        configService.SaveConfig(list);
        SyncActivePrizeProfile(list.Name);
        return true;
    }

    public bool ReplaceStudents(string name, IReadOnlyList<Student> students)
    {
        ArgumentNullException.ThrowIfNull(students);
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var list = LoadStudentList(name) ?? new StudentList(name);
        list.Students.Clear();
        foreach (var student in students)
            list.Students.Add(student);

        return SaveStudentList(list);
    }

    public bool ReplacePrizes(string name, IReadOnlyList<Prize> prizes)
    {
        ArgumentNullException.ThrowIfNull(prizes);
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var list = LoadPrizeList(name) ?? new PrizeList(name);
        list.Prizes.Clear();
        foreach (var prize in prizes)
            list.Prizes.Add(prize);

        return SavePrizeList(list);
    }

    public void SetDefaultStudentList(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || configHandler.Data.RollCallSettings.DefaultClass == name)
            return;

        configHandler.Data.RollCallSettings.DefaultClass = name;
        configHandler.Save();
    }

    public void SetDefaultPrizePool(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || configHandler.Data.LotterySettings.DefaultPool == name)
            return;

        configHandler.Data.LotterySettings.DefaultPool = name;
        configHandler.Save();
    }

    public bool ClearStudentHistory(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (string.Equals(profileService.StudentHistoryConfig?.Name, name, StringComparison.OrdinalIgnoreCase))
        {
            profileService.ClearCurrentStudentHistory();
            return true;
        }

        if (!File.Exists(Utils.GetFilePath("history", "roll_call_history", $"{name}.json")))
            return false;

        var config = new StudentHistoryConfig(name, logger, configService);
        var history = config.Data;
        history.TotalRounds = 0;
        history.TotalStats = 0;
        history.Students.Clear();
        history.GroupStats.Clear();
        history.GenderStatus.Clear();
        config.Save();
        logger.LogInformation("已清空点名历史：班级={ClassName}。", name);
        return true;
    }

    public bool ClearPrizeHistory(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (string.Equals(profileService.PrizeHistoryConfig?.Name, name, StringComparison.OrdinalIgnoreCase))
        {
            profileService.ClearCurrentPrizeHistory();
            return true;
        }

        if (!File.Exists(Utils.GetFilePath("history", "lottery_history", $"{name}.json")))
            return false;

        var config = new PrizeHistoryConfig(name, logger, configService);
        var history = config.Data;
        history.TotalRounds = 0;
        history.TotalStats = 0;
        history.Prizes.Clear();
        history.GroupStats.Clear();
        history.GenderStatus.Clear();
        config.Save();
        logger.LogInformation("已清空抽奖历史：奖池={PoolName}。", name);
        return true;
    }

    private void SyncActiveStudentProfile(string name)
    {
        if (string.Equals(profileService.StudentListConfig?.Name, name, StringComparison.Ordinal))
            profileService.LoadStudentProfile(name, saveCurrent: false);
    }

    private void SyncActivePrizeProfile(string name)
    {
        if (string.Equals(profileService.PrizeListConfig?.Name, name, StringComparison.Ordinal))
            profileService.LoadPrizeProfile(name, saveCurrent: false);
    }

    private static IReadOnlyList<string> GetListNames(string directory)
    {
        var path = Utils.GetDirectoryPath("list", directory);
        return Directory.Exists(path)
            ? Directory.GetFiles(path, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .OfType<string>()
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .OrderBy(name => name)
                .ToArray()
            : [];
    }

    private static bool ContainsName(IReadOnlyList<string> names, string name) =>
        !string.IsNullOrWhiteSpace(name) && names.Contains(name, StringComparer.OrdinalIgnoreCase);

    private static bool SortInPlace(ObservableCollection<Student> students)
    {
        var sorted = students.OrderForList().ToList();
        if (students.SequenceEqual(sorted))
            return false;

        students.Clear();
        foreach (var student in sorted)
            students.Add(student);
        return true;
    }

    private static bool SortInPlace(ObservableCollection<Prize> prizes)
    {
        var sorted = prizes.OrderForList().ToList();
        if (prizes.SequenceEqual(sorted))
            return false;

        prizes.Clear();
        foreach (var prize in sorted)
            prizes.Add(prize);
        return true;
    }

    private static void DeleteFile(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private bool RenameList(string oldName, string newName, string listDirectory, string historyDirectory,
        string temporaryPrefix, bool isStudent)
    {
        oldName = oldName.Trim();
        newName = newName.Trim();
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName) ||
            oldName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
            return false;

        var exists = isStudent ? StudentListExists(oldName) : PrizeListExists(oldName);
        var targetExists = isStudent ? StudentListExists(newName) : PrizeListExists(newName);
        if (!exists || targetExists)
            return false;

        var isActive = isStudent
            ? string.Equals(profileService.StudentListConfig?.Name, oldName, StringComparison.Ordinal)
            : string.Equals(profileService.PrizeListConfig?.Name, oldName, StringComparison.Ordinal);
        profileService.SaveProfile();

        var moves = new[]
        {
            (Source: Utils.GetFilePath("list", listDirectory, $"{oldName}.json"),
             Target: Utils.GetFilePath("list", listDirectory, $"{newName}.json")),
            (Source: Utils.GetFilePath("history", historyDirectory, $"{oldName}.json"),
             Target: Utils.GetFilePath("history", historyDirectory, $"{newName}.json")),
            (Source: Utils.GetFilePath("TEMP", $"{temporaryPrefix}{NormalizeFileComponent(oldName)}.json"),
             Target: Utils.GetFilePath("TEMP", $"{temporaryPrefix}{NormalizeFileComponent(newName)}.json"))
        };

        var moved = new List<(string Source, string Target)>();
        try
        {
            foreach (var move in moves)
            {
                if (!File.Exists(move.Source))
                    continue;

                File.Move(move.Source, move.Target);
                moved.Add(move);
            }

            if (isStudent)
            {
                if (configHandler.Data.RollCallSettings.DefaultClass == oldName)
                    configHandler.Data.RollCallSettings.DefaultClass = newName;
                if (isActive)
                    profileService.LoadStudentProfile(newName, saveCurrent: false);
            }
            else
            {
                if (configHandler.Data.LotterySettings.DefaultPool == oldName)
                    configHandler.Data.LotterySettings.DefaultPool = newName;
                if (isActive)
                    profileService.LoadPrizeProfile(newName, saveCurrent: false);
            }

            configHandler.Save();
            logger.LogInformation("已重命名{ListType}：旧名称={OldName}，新名称={NewName}。",
                isStudent ? "点名名单" : "奖品池", oldName, newName);
            return true;
        }
        catch
        {
            foreach (var move in moved.AsEnumerable().Reverse())
            {
                if (File.Exists(move.Target) && !File.Exists(move.Source))
                    File.Move(move.Target, move.Source);
            }

            throw;
        }
    }

    private static string NormalizeFileComponent(string value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "default" : value.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
            text = text.Replace(invalid, '_');
        return text.Replace(' ', '_');
    }
}
