namespace SecRandom.Core.Models.Notification;

/// <summary>
///     文案库里的一条模板。
/// </summary>
/// <remarks>
///     使用次数挂在 <see cref="Id" /> 上，**绝不**挂文本或数组下标：文本可编辑、可重排，
///     整库导入还会换掉全部条目；挂文本会让"改一个字"把统计清零，挂下标会在删改后
///     把计数错位到另一句话上。因此 <see cref="Id" /> 生成后终身不变。
/// </remarks>
public sealed class TextLibraryEntry
{
    /// <summary>稳定标识（<see cref="Guid" /> 的 N 格式），由文案库服务生成，不来自用户输入。</summary>
    public string Id { get; set; } = "";

    /// <summary>模板文本，应恰好含一个 <c>{result}</c>。</summary>
    public string Text { get; set; } = "";

    /// <summary>是否参与轮换。停用只是从候选里排除，条目本身与计数都保留。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>本机累计使用次数。</summary>
    public int UseCount { get; set; }

    /// <summary>最近一次被选中的时间（UTC），从未使用为 <see langword="null" />。</summary>
    public DateTimeOffset? LastUsedUtc { get; set; }
}

/// <summary>
///     <c>data/texts/library.json</c> 的磁盘形态。
/// </summary>
/// <remarks>
///     <see cref="FormatVersion" /> 只用于"新版写、旧版读"的保护：读到高于当前支持的版本时
///     只读不写回，避免旧版本把新字段抹掉。属性名由 <c>JsonNamingPolicy.SnakeCaseLower</c>
///     序列化为 <c>format_version</c> / <c>texts</c> / <c>last_picked_id</c>。
/// </remarks>
public sealed class TextLibraryDocument
{
    /// <summary>当前格式版本。文案库新增字段时递增，且必须同时扩写只读判定。</summary>
    public int FormatVersion { get; set; } = 1;

    /// <summary>全部文案条目，顺序即用户的列表顺序。</summary>
    public List<TextLibraryEntry> Texts { get; set; } = new();

    /// <summary>上一次被选中的条目 id，仅用于"尽量不连续两次同一条"。</summary>
    public string? LastPickedId { get; set; }
}
