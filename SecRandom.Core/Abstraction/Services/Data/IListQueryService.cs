namespace SecRandom.Core.Abstraction.Services.Data;

/// <summary>名单里一个学生的只读快照。</summary>
public sealed record StudentSnapshot(
    string Name,
    string Group,
    string Gender,
    string Id,
    Guid RecordId,
    string Tags,
    bool Exists,
    bool IsCandidate);

/// <summary>奖池里一个奖品的只读快照。</summary>
public sealed record PrizeSnapshot(
    string Name,
    string Id,
    Guid RecordId,
    string Tags,
    double Weight,
    int Count,
    bool Exists,
    bool IsCandidate);

/// <summary>学生名单的只读快照。</summary>
public sealed record StudentListSnapshot(string Name, IReadOnlyList<StudentSnapshot> Students, bool IsCurrent)
{
    /// <summary>可直接参与抽签的学生（<c>IsCandidate</c>）。</summary>
    public IReadOnlyList<StudentSnapshot> Candidates =>
        Students.Where(static student => student.IsCandidate).ToArray();
}

/// <summary>奖池的只读快照。</summary>
public sealed record PrizeListSnapshot(string Name, IReadOnlyList<PrizeSnapshot> Prizes, bool IsCurrent)
{
    /// <summary>可直接参与抽奖的奖品（<c>IsCandidate</c>）。</summary>
    public IReadOnlyList<PrizeSnapshot> Candidates =>
        Prizes.Where(static prize => prize.IsCandidate).ToArray();
}

/// <summary>名单发生变化（切换当前名单、编辑保存、外部改动刷新）。</summary>
public sealed class ListChangedEventArgs(string listName, bool isPrizeList) : EventArgs
{
    /// <summary>发生变化的名单名；<c>""</c> 表示"当前名单"整体变化。</summary>
    public string ListName { get; } = listName;

    /// <summary>true 表示奖池，false 表示学生名单。</summary>
    public bool IsPrizeList { get; } = isPrizeList;
}

/// <summary>
///     名单/奖池的**只读**查询扩展点：插件可以读学生、奖品、名单名，但不能改（改名、增删请走宿主界面或
///     <c>IProfileCatalogManager</c> 这类面向宿主的功能）。
///     <para>
///         查询不会切换宿主当前正在使用的名单——<see cref="GetStudentList" /> 只读磁盘上的那一份。
///     </para>
/// </summary>
public interface IListQueryService
{
    /// <summary>磁盘上全部学生名单名。</summary>
    IReadOnlyList<string> GetStudentListNames();

    /// <summary>磁盘上全部奖池名。</summary>
    IReadOnlyList<string> GetPrizeListNames();

    /// <summary>按名字读学生名单；不存在时返回 null。</summary>
    StudentListSnapshot? GetStudentList(string name);

    /// <summary>按名字读奖池；不存在时返回 null。</summary>
    PrizeListSnapshot? GetPrizeList(string name);

    /// <summary>宿主当前选中的学生名单。</summary>
    StudentListSnapshot? GetCurrentStudentList();

    /// <summary>宿主当前选中的奖池。</summary>
    PrizeListSnapshot? GetCurrentPrizeList();

    /// <summary>名单/奖池整体变化。回调在 UI 线程上触发。</summary>
    event EventHandler<ListChangedEventArgs>? Changed;

    /// <summary>让宿主重新读一遍磁盘并触发 <see cref="Changed" />。</summary>
    void Refresh();
}
