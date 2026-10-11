using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Abstraction.Services.Data;
using SecRandom.Core.Abstraction.Services.Messaging;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services.Data;

/// <summary>
///     名单/奖池只读查询。所有读取都走 <see cref="IProfileCatalogManager" /> 的"分离快照"入口，
///     因此**不会切换宿主当前正在用的名单**。
/// </summary>
public sealed class ListQueryService : IListQueryService
{
    private readonly IProfileService _profileService;
    private readonly IProfileCatalogManager _catalogManager;
    private readonly IPluginEventBus? _eventBus;
    private readonly ILogger<ListQueryService>? _logger;

    public ListQueryService(
        IProfileService profileService,
        IProfileCatalogManager catalogManager,
        IPluginEventBus? eventBus = null,
        ILogger<ListQueryService>? logger = null)
    {
        _profileService = profileService;
        _catalogManager = catalogManager;
        _eventBus = eventBus;
        _logger = logger;
    }

    /// <inheritdoc />
    public event EventHandler<ListChangedEventArgs>? Changed;

    /// <inheritdoc />
    public IReadOnlyList<string> GetStudentListNames() => SafeNames(_catalogManager.GetStudentListNames);

    /// <inheritdoc />
    public IReadOnlyList<string> GetPrizeListNames() => SafeNames(_catalogManager.GetPrizeListNames);

    /// <inheritdoc />
    public StudentListSnapshot? GetStudentList(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        try
        {
            var list = _catalogManager.LoadStudentList(name);
            return list is null ? null : ToSnapshot(list);
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "读取学生名单 {Name} 失败。", name);
            return null;
        }
    }

    /// <inheritdoc />
    public PrizeListSnapshot? GetPrizeList(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        try
        {
            var list = _catalogManager.LoadPrizeList(name);
            return list is null ? null : ToSnapshot(list);
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "读取奖池 {Name} 失败。", name);
            return null;
        }
    }

    /// <inheritdoc />
    public StudentListSnapshot? GetCurrentStudentList()
    {
        var current = _profileService.CurrentStudentList;
        return current is null ? null : ToSnapshot(current);
    }

    /// <inheritdoc />
    public PrizeListSnapshot? GetCurrentPrizeList()
    {
        var current = _profileService.CurrentPrizeList;
        return current is null ? null : ToSnapshot(current);
    }

    /// <inheritdoc />
    public void Refresh()
    {
        Changed?.Invoke(this, new ListChangedEventArgs(string.Empty, false));
        _eventBus?.Publish(new HostEvents.ProfileChanged(string.Empty, false));
    }

    private IReadOnlyList<string> SafeNames(Func<IReadOnlyList<string>> factory)
    {
        try
        {
            return factory();
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "枚举名单失败。");
            return [];
        }
    }

    private StudentListSnapshot ToSnapshot(StudentList list)
    {
        var students = list.Students
            .Select(static student => new StudentSnapshot(
                student.Name,
                student.Group,
                student.Gender,
                student.Id,
                student.RecordId,
                student.Tags,
                student.Exists,
                student.IsCandidate))
            .ToArray();

        var isCurrent = string.Equals(_profileService.CurrentStudentList?.Name, list.Name, StringComparison.Ordinal);
        return new StudentListSnapshot(list.Name, students, isCurrent);
    }

    private PrizeListSnapshot ToSnapshot(PrizeList list)
    {
        var prizes = list.Prizes
            .Select(static prize => new PrizeSnapshot(
                prize.Name,
                prize.Id,
                prize.RecordId,
                prize.Tags,
                prize.Weight,
                prize.Count,
                prize.Exists,
                prize.IsCandidate))
            .ToArray();

        var isCurrent = string.Equals(_profileService.CurrentPrizeList?.Name, list.Name, StringComparison.Ordinal);
        return new PrizeListSnapshot(list.Name, prizes, isCurrent);
    }
}
