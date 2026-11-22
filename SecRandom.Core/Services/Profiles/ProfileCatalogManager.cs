using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.HistoryStore;
using SecRandom.Core.Services.Stats;
using SecRandom.Shared;
using SecRandom.Shared.Abstraction;
using SecRandom.Shared.ComponentModels;
using SecRandom.Shared.Extensions;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services.Profiles;

internal sealed class ProfileCatalogManager(
    ConfigServiceBase configService,
    IProfileService profileService,
    IHistoryGroupCatalog historyGroupCatalog,
    MainConfigHandler configHandler,
    IOverviewStatisticsService overviewStatistics,
    ILogger<ProfileCatalogManager> logger) : IProfileCatalogManager
{
    public IReadOnlyList<string> GetStudentListNames() => GetListNames("roll_call_list");

    public IReadOnlyList<string> GetPrizeListNames() => GetListNames("lottery_list");

    public bool StudentListExists(string name) => ContainsName(GetStudentListNames(), name);

    public bool PrizeListExists(string name) => ContainsName(GetPrizeListNames(), name);

    public bool CreateStudentList(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        name = name.Trim();
        if (!ProfileConfigBase.IsValidProfileName(name) || StudentListExists(name))
            return false;

        new StudentListConfig(name, logger, configService).Save();
        logger.LogInformation("已创建点名名单：名单={ListName}。", name);
        return true;
    }

    public bool CreatePrizeList(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        name = name.Trim();
        if (!ProfileConfigBase.IsValidProfileName(name) || PrizeListExists(name))
            return false;

        new PrizeListConfig(name, logger, configService).Save();
        logger.LogInformation("已创建奖品池：奖品池={ListName}。", name);
        return true;
    }

    public bool RenameStudentList(string oldName, string newName) =>
        RenameList(oldName, newName, "roll_call_record_", isStudent: true);

    public bool RenamePrizeList(string oldName, string newName) =>
        RenameList(oldName, newName, "lottery_record_", isStudent: false);

    public bool DeleteStudentList(string name, bool deleteHistory)
    {
        var resolvedName = ResolveExistingName(GetStudentListNames(), name);
        if (resolvedName is null)
            return false;

        name = resolvedName;
        ProfileRosterDatabase.Delete(name, ProfileRosterKind.RollCall, logger);
        if (deleteHistory)
            ProfileHistoryPaths.Delete(name, HistoryGroupKind.RollCall);
        overviewStatistics.RemoveStudent(name);

        if (string.Equals(profileService.StudentListConfig?.Name, name, StringComparison.Ordinal))
        {
            // 删掉的正好是活动名单：有下一份就切过去，没有就把活动档案真正清空（删除最后一份名单是合法的）。
            profileService.LoadStudentProfile(GetStudentListNames().FirstOrDefault() ?? string.Empty, saveCurrent: false);
        }

        logger.LogInformation("已删除点名名单：名单={ListName}，连带历史={DeleteHistory}。", name, deleteHistory);
        return true;
    }

    public bool DeletePrizeList(string name, bool deleteHistory)
    {
        var resolvedName = ResolveExistingName(GetPrizeListNames(), name);
        if (resolvedName is null)
            return false;

        name = resolvedName;
        ProfileRosterDatabase.Delete(name, ProfileRosterKind.Lottery, logger);
        if (deleteHistory)
            ProfileHistoryPaths.Delete(name, HistoryGroupKind.Lottery);
        overviewStatistics.RemovePrize(name);

        if (string.Equals(profileService.PrizeListConfig?.Name, name, StringComparison.Ordinal))
        {
            // 与 DeleteStudentList 同理：没有剩余奖品池时清空活动档案，而不是继续指向已删除的文件。
            profileService.LoadPrizeProfile(GetPrizeListNames().FirstOrDefault() ?? string.Empty, saveCurrent: false);
        }

        logger.LogInformation("已删除奖品池：奖品池={ListName}，连带历史={DeleteHistory}。", name, deleteHistory);
        return true;
    }

    public StudentList? LoadStudentList(string name)
    {
        name = ResolveExistingName(GetStudentListNames(), name) ?? string.Empty;
        if (name.Length == 0)
            return null;

        try
        {
            var data = new StudentListConfig(name, logger, configService).Data;
            if (SortInPlace(data.Students))
                ProfileRosterDatabase.SaveStudentList(data, logger);
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
        name = ResolveExistingName(GetPrizeListNames(), name) ?? string.Empty;
        if (name.Length == 0)
            return null;

        try
        {
            var data = new PrizeListConfig(name, logger, configService).Data;
            if (SortInPlace(data.Prizes))
                ProfileRosterDatabase.SavePrizeList(data, logger);
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
        ProfileRosterDatabase.SaveStudentList(list, logger);
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
        ProfileRosterDatabase.SavePrizeList(list, logger);
        SyncActivePrizeProfile(list.Name);
        return true;
    }

    public bool ReplaceStudents(string name, IReadOnlyList<Student> students)
    {
        ArgumentNullException.ThrowIfNull(students);
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var list = LoadStudentList(name) ?? new StudentList(name);

        // 导入是整表替换，但「同一个人」不能跟着旧对象一起丢掉：按「学号 + 姓名」精确认领，
        // 复用 RecordId（点名历史与公平性统计挂在它上面）和成员级附加设置。
        // 学号或姓名有一项变了就不认领——不靠姓名模糊兜底，避免重名错认，这类成员交给 Normalize 发新 Id。
        // 同一个控件键如果本次导入已显式给出，则以导入值为准（集控 replace 载荷会携带特殊语音）。
        foreach (var student in students)
        {
            var existing = list.Students.FirstOrDefault(x =>
                string.Equals(x.Id, student.Id, StringComparison.Ordinal) &&
                string.Equals(x.Name, student.Name, StringComparison.Ordinal));
            if (existing is null || existing.RecordId == Guid.Empty)
                continue;

            student.RecordId = existing.RecordId;
            foreach (var (controlId, value) in existing.AttachedObjects)
                student.AttachedObjects.TryAdd(controlId, value);
        }

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
        // 空名字代表"没有默认名单"：删掉最后一份名单后必须能把默认设置真正清掉，
        // 否则重启会留下一个指向已删除名单的默认值。
        var normalized = string.IsNullOrWhiteSpace(name) ? string.Empty : name;
        if (configHandler.Data.RollCallSettings.DefaultClass == normalized)
            return;

        configHandler.Data.RollCallSettings.DefaultClass = normalized;
        configHandler.Save();
    }

    public void SetDefaultPrizePool(string name)
    {
        var normalized = string.IsNullOrWhiteSpace(name) ? string.Empty : name;
        if (configHandler.Data.LotterySettings.DefaultPool == normalized)
            return;

        configHandler.Data.LotterySettings.DefaultPool = normalized;
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

        if (!ProfileHistoryPaths.Exists(name, HistoryGroupKind.RollCall))
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

        if (!ProfileHistoryPaths.Exists(name, HistoryGroupKind.Lottery))
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

    public int MoveStudentHistoryGroup(
        string profileName,
        IReadOnlyCollection<string> recordIds,
        string targetGroup,
        string? sourceGroup = null) =>
        MoveHistoryGroup(HistoryGroupKind.RollCall, profileName, recordIds, targetGroup, sourceGroup);

    public int MovePrizeHistoryGroup(
        string profileName,
        IReadOnlyCollection<string> recordIds,
        string targetGroup,
        string? sourceGroup = null) =>
        MoveHistoryGroup(HistoryGroupKind.Lottery, profileName, recordIds, targetGroup, sourceGroup);

    /// <summary>
    ///     Re-marks the given records of one profile's history file as belonging to <paramref name="targetGroup" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Marker only.</b> A move is a correction of where a record belongs, not a new draw: it never appends a
    ///         <see cref="HistoryItem" />, never creates a <see cref="HistoryItem.DrawRoundId" />, and never touches
    ///         <see cref="History.TotalCount" />, <see cref="History.LastDrawnTime" />, <see cref="History.RoundsMissed" />
    ///         or <see cref="HistoryItem.Weight" />. Those are the fairness inputs; editing them here would let a
    ///         regrouping operation rewrite the draw weights it is supposed to be orthogonal to.
    ///     </para>
    ///     <para>
    ///         <b>A group filter narrows the move.</b> <paramref name="sourceGroup" /> is the record group the caller's
    ///         view is currently filtered to (<see langword="null" /> = no filter). Passing it keeps the operation
    ///         consistent with what the user selected: moving a member whose records are spread across several groups
    ///         must not silently drag the group they are not looking at along. Without a filter every record of the
    ///         named members moves, which is the pre-filter behaviour.
    ///     </para>
    ///     <para>
    ///         <b>One file, saved once, and only when something changed.</b> The save goes through the profile's own
    ///         history handler (<see cref="IProfileService.StudentHistoryConfig" /> / <see cref="IProfileService.PrizeHistoryConfig" />
    ///         → the atomic temp-file-and-replace write in <c>FileConfigService</c>), exactly like
    ///         <see cref="HistoryGroupCatalog" />; never <see cref="IProfileService.SaveProfile" />, which would also
    ///         rewrite the roster and the other history file for an operation that cannot have changed them. A call
    ///         that matches no record returns 0 and writes nothing.
    ///     </para>
    ///     <para>
    ///         <b>Active profile vs. a detached one.</b> When the name matches the active profile, the in-memory
    ///         instance is mutated and saved, so the UI's observable collections see the new markers immediately.
    ///         Otherwise the history file is loaded through a detached <see cref="StudentHistoryConfig" /> /
    ///         <see cref="PrizeHistoryConfig" /> (the same path <see cref="ClearStudentHistory" /> uses) and saved
    ///         without switching the active profile — a move must not make the moved list the active one.
    ///     </para>
    /// </remarks>
    private int MoveHistoryGroup(
        HistoryGroupKind kind,
        string profileName,
        IReadOnlyCollection<string> recordIds,
        string targetGroup,
        string? sourceGroup)
    {
        ArgumentNullException.ThrowIfNull(recordIds);
        // 档案名会参与拼路径：非法名字在这里就以 ArgumentException 拒绝，绝不落到不安全的路径拼接上。
        ProfileConfigBase.ValidateProfileName(profileName);

        // 空串是默认组，不是「名为空的组」：先短路，否则移回默认组永远过不了命名校验。
        var target = HistoryGroupName.Normalize(targetGroup);
        if (target.Length > 0 && !HistoryGroupName.Validate(target, out target, out var nameError))
            throw new ArgumentException(nameError, nameof(targetGroup));

        var ids = new HashSet<string>(
            recordIds.Where(id => !string.IsNullOrEmpty(id)),
            StringComparer.Ordinal);
        if (ids.Count == 0)
            return 0;

        var activeName = kind == HistoryGroupKind.RollCall
            ? profileService.StudentHistoryConfig?.Name
            : profileService.PrizeHistoryConfig?.Name;
        var isActive = activeName is not null && string.Equals(activeName, profileName, StringComparison.Ordinal);

        var view = isActive
            ? ResolveActiveHistory(kind)
            : LoadHistorySnapshot(kind, profileName);
        if (view is null)
            throw new InvalidOperationException(HistoryGroupName.ErrorProfileUnavailable);

        var moved = MoveIntoGroup(view.Records, ids, target, sourceGroup);
        if (moved == 0)
            return 0;

        if (target.Length > 0)
        {
            if (isActive)
            {
                historyGroupCatalog.Touch(kind, target);
            }
            else
            {
                // IHistoryGroupCatalog.Touch 解析的是**活动**档案的历史，对另一个档案调用会把组记到错的
                // 文件上（下一次保存就多出一个没有记录的空组）。这里直接登记在即将保存的那份历史上，
                // 语义与 Touch 的自愈分支一致：已登记就更新 LastUsedAt，未登记就带着创建时间登记。
                RegisterGroup(view.Groups, target);
            }
        }

        try
        {
            view.Save();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "保存历史记录组移动失败：类型={Kind}，档案={ProfileName}。", kind, profileName);
            throw new InvalidOperationException(HistoryGroupName.ErrorSaveFailed, exception);
        }

        // 默认组没有登记项，只记条数，不记组名。
        logger.LogInformation(
            "已移动历史记录到记录组：类型={Kind}，档案={ProfileName}，记录数={MovedCount}，目标组={GroupName}。",
            kind, profileName, moved, target.Length == 0 ? "(默认组)" : target);

        return moved;
    }

    /// <summary>One history file's live view: its registered groups, its records, and how it is saved.</summary>
    private sealed record HistoryView(
        ObservableDictionary<string, HistoryGroupInfo> Groups,
        ObservableDictionary<string, History> Records,
        Action Save);

    private HistoryView? ResolveActiveHistory(HistoryGroupKind kind)
    {
        switch (kind)
        {
            case HistoryGroupKind.RollCall:
            {
                var history = profileService.CurrentStudentHistory;
                var config = profileService.StudentHistoryConfig;
                return history is null || config is null
                    ? null
                    : new HistoryView(history.HistoryGroups, history.Students, config.Save);
            }

            case HistoryGroupKind.Lottery:
            {
                var history = profileService.CurrentPrizeHistory;
                var config = profileService.PrizeHistoryConfig;
                return history is null || config is null
                    ? null
                    : new HistoryView(history.HistoryGroups, history.Prizes, config.Save);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown history group kind.");
        }
    }

    /// <summary>
    ///     Loads another profile's history file without switching the active profile. Returns null when the file does
    ///     not exist — checked before the handler is constructed, because constructing it on a missing file would
    ///     write a fallback history file as a side effect.
    /// </summary>
    private HistoryView? LoadHistorySnapshot(HistoryGroupKind kind, string profileName)
    {
        try
        {
            if (!ProfileHistoryPaths.Exists(profileName, kind))
                return null;

            if (kind == HistoryGroupKind.RollCall)
            {
                var studentConfig = new StudentHistoryConfig(profileName, logger, configService);
                return new HistoryView(
                    studentConfig.Data.HistoryGroups, studentConfig.Data.Students, studentConfig.Save);
            }

            var prizeConfig = new PrizeHistoryConfig(profileName, logger, configService);
            return new HistoryView(
                prizeConfig.Data.HistoryGroups, prizeConfig.Data.Prizes, prizeConfig.Save);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "读取历史档案失败：类型={Kind}，档案={ProfileName}。", kind, profileName);
            return null;
        }
    }

    private static int MoveIntoGroup(
        ObservableDictionary<string, History> records,
        HashSet<string> recordIds,
        string target,
        string? sourceGroup)
    {
        var moved = 0;
        foreach (var pair in records)
        {
            if (!recordIds.Contains(pair.Key))
                continue;

            foreach (var item in pair.Value.Histories)
            {
                // 视图按某个组过滤时，移动只能改这个组里的记录；否则用户没看见的其它组的记录
                // 会一起被拖走，选中行与实际改动就对不上了。
                if (sourceGroup is not null
                    && !string.Equals(item.HistoryGroup ?? string.Empty, sourceGroup, StringComparison.Ordinal))
                    continue;

                if (string.Equals(item.HistoryGroup ?? string.Empty, target, StringComparison.Ordinal))
                    continue;

                item.HistoryGroup = target;
                moved++;
            }
        }

        return moved;
    }

    private static void RegisterGroup(ObservableDictionary<string, HistoryGroupInfo> groups, string name)
    {
        var now = DateTime.Now;
        if (groups.TryGetValue(name, out var info))
        {
            info.LastUsedAt = now;
            return;
        }

        groups[name] = new HistoryGroupInfo { CreatedAt = now, LastUsedAt = now };
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

    private IReadOnlyList<string> GetListNames(string directory)
    {
        var kind = directory.Equals("roll_call_list", StringComparison.Ordinal)
            ? ProfileRosterKind.RollCall
            : ProfileRosterKind.Lottery;
        return ProfileRosterDatabase.GetProfileNames(kind, logger);
    }

    private static bool ContainsName(IReadOnlyList<string> names, string name) =>
        !string.IsNullOrWhiteSpace(name) && names.Contains(name, StringComparer.OrdinalIgnoreCase);

    private static string? ResolveExistingName(IReadOnlyList<string> names, string? requestedName)
    {
        if (string.IsNullOrWhiteSpace(requestedName))
            return null;

        var trimmed = requestedName.Trim();
        return names.FirstOrDefault(name => string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase));
    }

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

    private bool RenameList(string oldName, string newName, string temporaryPrefix, bool isStudent)
    {
        oldName = ResolveExistingName(isStudent ? GetStudentListNames() : GetPrizeListNames(), oldName) ?? string.Empty;
        newName = newName.Trim();
        if (string.IsNullOrWhiteSpace(oldName) || !ProfileConfigBase.IsValidProfileName(newName) ||
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

        var historyKind = isStudent ? HistoryGroupKind.RollCall : HistoryGroupKind.Lottery;
        var rosterKind = isStudent ? ProfileRosterKind.RollCall : ProfileRosterKind.Lottery;
        var moves = new[]
        {
            (Source: Utils.GetFilePath("TEMP", $"{temporaryPrefix}{NormalizeFileComponent(oldName)}.json"),
             Target: Utils.GetFilePath("TEMP", $"{temporaryPrefix}{NormalizeFileComponent(newName)}.json"))
        };

        var moved = new List<(string Source, string Target)>();
        var rosterRenamed = false;
        var historyRenamed = false;
        try
        {
            foreach (var move in moves)
            {
                if (!File.Exists(move.Source))
                    continue;

                File.Move(move.Source, move.Target);
                moved.Add(move);
            }

            ProfileRosterDatabase.Rename(oldName, newName, rosterKind, logger);
            rosterRenamed = true;
            ProfileHistoryPaths.Rename(oldName, newName, historyKind);
            historyRenamed = true;

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
            if (isStudent)
                overviewStatistics.RenameStudent(oldName, newName);
            else
                overviewStatistics.RenamePrize(oldName, newName);
            logger.LogInformation("已重命名{ListType}：旧名称={OldName}，新名称={NewName}。",
                isStudent ? "点名名单" : "奖品池", oldName, newName);
            return true;
        }
        catch
        {
            if (historyRenamed)
            {
                try
                {
                    ProfileHistoryPaths.Rename(newName, oldName, historyKind);
                }
                catch (Exception rollbackException)
                {
                    logger.LogWarning(rollbackException, "回滚历史档案名称失败：{OldName} -> {NewName}", newName, oldName);
                }
            }
            if (rosterRenamed)
            {
                try
                {
                    ProfileRosterDatabase.Rename(newName, oldName, rosterKind, logger);
                }
                catch (Exception rollbackException)
                {
                    logger.LogWarning(rollbackException, "回滚名单档案名称失败：{OldName} -> {NewName}", newName, oldName);
                }
            }

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
