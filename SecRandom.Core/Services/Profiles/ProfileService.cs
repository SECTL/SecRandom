using System.Text.Json;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Draw;
using SecRandom.Core.Services.Profiles;
using SecRandom.Shared;
using SecRandom.Shared.Abstraction;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services;

public static partial class CoreRuntimeServiceCollectionExtensions
{
    private sealed class ProfileService : IProfileService, IDrawHistorySnapshotCompensation
    {
    private readonly ILogger<ProfileService> _logger;
    private readonly MainConfigHandler _configHandler;
    private readonly ConfigServiceBase _configService;

    public ProfileService(
        ILogger<ProfileService> logger,
        MainConfigHandler configHandler,
        ConfigServiceBase configService)
    {
        _logger = logger;
        _configHandler = configHandler;
        _configService = configService;

        SeedDefaultListsOnFirstRun();

        var studentListName = ResolveProfileName("list", "roll_call_list", _configHandler.Data.RollCallSettings.DefaultClass);
        var prizeListName = ResolveProfileName("list", "lottery_list", _configHandler.Data.LotterySettings.DefaultPool);

        StudentListConfig = CreateStudentListConfig(studentListName);
        StudentHistoryConfig = CreateStudentHistoryConfig(studentListName);
        PrizeListConfig = CreatePrizeListConfig(prizeListName);
        PrizeHistoryConfig = CreatePrizeHistoryConfig(prizeListName);

        _logger.LogInformation(
            "已加载默认档案：学生名单={StudentListName}，学生数量={StudentCount}，奖品池={PrizeListName}，奖品数量={PrizeCount}。",
            StudentListConfig?.Name ?? NoListName,
            CurrentStudentList?.Students.Count ?? 0,
            PrizeListConfig?.Name ?? NoListName,
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
        // 空名不是"什么都不做"，而是"当前没有名单"：删掉最后一份名单后必须真的把活动档案清空，
        // 否则内存里还留着已删除的名单，随后任何一次保存都会把它写回磁盘。
        if (string.IsNullOrWhiteSpace(name))
        {
            ClearStudentProfile(saveCurrent);
            return;
        }

        ProfileConfigBase.ValidateProfileName(name);

        if (StudentListConfig?.Name == name && StudentHistoryConfig?.Name == name)
        {
            StudentListConfig.Reload();
            StudentHistoryConfig.Reload();
            return;
        }

        if (saveCurrent)
        {
            StudentListConfig?.Save();
            StudentHistoryConfig?.Save();
        }

        StudentListConfig = CreateStudentListConfig(name);
        StudentHistoryConfig = CreateStudentHistoryConfig(name);

        _logger.LogInformation(
            "已切换点名名单：学生名单={StudentListName}，学生数量={StudentCount}。",
            name,
            CurrentStudentList?.Students.Count ?? 0);
    }

    public void LoadPrizeProfile(string name, bool saveCurrent = true)
    {
        // 与 LoadStudentProfile 同理：空名代表"当前没有奖品池"，必须清空而不是保留旧档案。
        if (string.IsNullOrWhiteSpace(name))
        {
            ClearPrizeProfile(saveCurrent);
            return;
        }

        ProfileConfigBase.ValidateProfileName(name);

        if (PrizeListConfig?.Name == name && PrizeHistoryConfig?.Name == name)
        {
            PrizeListConfig.Reload();
            PrizeHistoryConfig.Reload();
            return;
        }

        if (saveCurrent)
        {
            PrizeListConfig?.Save();
            PrizeHistoryConfig?.Save();
        }

        PrizeListConfig = CreatePrizeListConfig(name);
        PrizeHistoryConfig = CreatePrizeHistoryConfig(name);

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
        string courseName = "",
        string? drawRoundId = null,
        string? historyGroup = null)
    {
        var history = CurrentStudentHistory;
        if (history is null || students.Count == 0)
            return;

        history.TotalRounds++;
        history.TotalStats += students.Count;
        var roundId = string.IsNullOrWhiteSpace(drawRoundId) ? Guid.NewGuid().ToString("N") : drawRoundId;
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
                DrawRoundId = roundId,
                DrawNumbers = requestedCount,
                DrawGroup = drawGroup,
                DrawGender = drawGender,
                DrawMethod = drawMethod,
                CourseName = courseName,
                HistoryGroup = historyGroup ?? string.Empty,
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

    public void RecordPrizeHistory(IReadOnlyList<Prize> prizes, DateTime now, int requestedCount, int drawMethod = 0, string? drawRoundId = null, string courseName = "", string? historyGroup = null)
    {
        var history = CurrentPrizeHistory;
        if (history is null || prizes.Count == 0)
            return;

        history.TotalRounds++;
        history.TotalStats += prizes.Count;
        var roundId = string.IsNullOrWhiteSpace(drawRoundId) ? Guid.NewGuid().ToString("N") : drawRoundId;
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
                DrawRoundId = roundId,
                DrawNumbers = requestedCount,
                DrawGroup = string.Empty,
                DrawGender = string.Empty,
                DrawMethod = drawMethod,
                CourseName = courseName,
                HistoryGroup = historyGroup ?? string.Empty,
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

    public string CaptureStudentHistorySnapshot()
    {
        return JsonSerializer.Serialize(CurrentStudentHistory ?? new StudentHistory(), ConfigServiceBase.JsonOptions);
    }

    public void RestoreStudentHistorySnapshot(string snapshotJson)
    {
        var history = CurrentStudentHistory;
        var snapshot = JsonSerializer.Deserialize<StudentHistory>(snapshotJson, ConfigServiceBase.JsonOptions);
        if (history is null || snapshot is null)
            return;

        history.TotalRounds = snapshot.TotalRounds;
        history.TotalStats = snapshot.TotalStats;
        history.Students.Clear();
        foreach (var pair in snapshot.Students)
            history.Students[pair.Key] = pair.Value;
        history.GroupStats.Clear();
        foreach (var pair in snapshot.GroupStats)
            history.GroupStats[pair.Key] = pair.Value;
        history.GenderStatus.Clear();
        foreach (var pair in snapshot.GenderStatus)
            history.GenderStatus[pair.Key] = pair.Value;
        StudentHistoryConfig?.Save();
    }

    public string CapturePrizeHistorySnapshot()
    {
        return JsonSerializer.Serialize(CurrentPrizeHistory ?? new PrizeHistory(), ConfigServiceBase.JsonOptions);
    }

    public void RestorePrizeHistorySnapshot(string snapshotJson)
    {
        var history = CurrentPrizeHistory;
        var snapshot = JsonSerializer.Deserialize<PrizeHistory>(snapshotJson, ConfigServiceBase.JsonOptions);
        if (history is null || snapshot is null)
            return;

        history.TotalRounds = snapshot.TotalRounds;
        history.TotalStats = snapshot.TotalStats;
        history.Prizes.Clear();
        foreach (var pair in snapshot.Prizes)
            history.Prizes[pair.Key] = pair.Value;
        history.GroupStats.Clear();
        foreach (var pair in snapshot.GroupStats)
            history.GroupStats[pair.Key] = pair.Value;
        history.GenderStatus.Clear();
        foreach (var pair in snapshot.GenderStatus)
            history.GenderStatus[pair.Key] = pair.Value;
        PrizeHistoryConfig?.Save();
    }

    private const string NoListName = "(无名单)";

    private StudentListConfig? CreateStudentListConfig(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : new StudentListConfig(name, _logger, _configService);

    private StudentHistoryConfig? CreateStudentHistoryConfig(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : new StudentHistoryConfig(name, _logger, _configService);

    private PrizeListConfig? CreatePrizeListConfig(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : new PrizeListConfig(name, _logger, _configService);

    private PrizeHistoryConfig? CreatePrizeHistoryConfig(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : new PrizeHistoryConfig(name, _logger, _configService);

    /// <summary>
    ///     Detaches the active roll-call profile: after the last list is deleted there is no profile to save, record or
    ///     reload, so every consumer must see <see langword="null" /> instead of a leftover (already deleted) file.
    /// </summary>
    private void ClearStudentProfile(bool saveCurrent)
    {
        if (StudentListConfig is null && StudentHistoryConfig is null)
            return;

        if (saveCurrent)
        {
            StudentListConfig?.Save();
            StudentHistoryConfig?.Save();
        }

        StudentListConfig = null;
        StudentHistoryConfig = null;
        _logger.LogInformation("已清空当前点名名单档案：现在没有名单。");
    }

    /// <summary>Detaches the active prize pool for the same reason as <see cref="ClearStudentProfile" />.</summary>
    private void ClearPrizeProfile(bool saveCurrent)
    {
        if (PrizeListConfig is null && PrizeHistoryConfig is null)
            return;

        if (saveCurrent)
        {
            PrizeListConfig?.Save();
            PrizeHistoryConfig?.Save();
        }

        PrizeListConfig = null;
        PrizeHistoryConfig = null;
        _logger.LogInformation("已清空当前奖品池档案：现在没有奖品池。");
    }

    /// <summary>
    ///     A first run (guide not completed yet) gets one blank <c>default</c> list per channel, which is what every
    ///     build up to now produced implicitly at startup.
    /// </summary>
    /// <remarks>
    ///     It only ever happens before the guide completes, because after that deleting the last list has to mean
    ///     "there are no lists": recreating <c>default</c> on every restart would make the empty state impossible.
    /// </remarks>
    private void SeedDefaultListsOnFirstRun()
    {
        if (_configHandler.Data.General.Basic.GuideCompleted)
            return;

        if (!HasListFiles("roll_call_list"))
            new StudentListConfig("default", _logger, _configService).Save();

        if (!HasListFiles("lottery_list"))
            new PrizeListConfig("default", _logger, _configService).Save();
    }

    private static bool HasListFiles(string directory)
    {
        var path = Utils.GetDirectoryPath("list", directory);
        return Directory.Exists(path) && Directory.GetFiles(path, "*.json").Length > 0;
    }

    private static string? ResolveProfileName(string rootA, string rootB, string preferredName)
    {
        // 「不使用名单」不是真实档案名：回落到磁盘上已有的名单，绝不为哨兵创建/读取档案文件。
        if (RosterSelection.IsNone(preferredName))
            preferredName = string.Empty;

        if (preferredName != string.Empty)
            ProfileConfigBase.ValidateProfileName(preferredName);

        var directory = Utils.GetDirectoryPath(rootA, rootB);
        if (preferredName != string.Empty)
        {
            var preferredPath = Path.Combine(directory, $"{preferredName}.json");
            if (File.Exists(preferredPath))
                return preferredName;
        }

        // 目录里没有任何名单文件就是"没有名单"：不再回落到 default，否则删掉最后一份名单后重启会被凭空重建，
        // 而"至少保留一份名单"的限制正是靠这条回落才显得存在。
        return Directory.GetFiles(directory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .FirstOrDefault(ProfileConfigBase.IsValidProfileName);
    }
    }
}
