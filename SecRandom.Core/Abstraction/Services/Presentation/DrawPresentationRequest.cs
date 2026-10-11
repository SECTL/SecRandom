using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Abstraction.Services.Presentation;

/// <summary>
///     Immutable, read-only snapshot of one draw round handed to <see cref="IDrawResultPresenter" />
///     implementations. The committed result itself is never exposed for mutation: a presenter may decide how
///     the round looks, never what the round was.
/// </summary>
public sealed record DrawPresentationRequest
{
    /// <summary>Channel the round came from.</summary>
    public required DrawPresentationChannel Channel { get; init; }

    /// <summary>Phase being presented.</summary>
    public required DrawPresentationPhase Phase { get; init; }

    /// <summary>Drawn students (roll call / quick draw / mobile). Empty for lottery rounds.</summary>
    public IReadOnlyList<Student> Students { get; init; } = [];

    /// <summary>Drawn prizes (lottery). Empty for roll-call style rounds.</summary>
    public IReadOnlyList<Prize> Prizes { get; init; } = [];

    /// <summary>Students assigned to the drawn prizes (lottery).</summary>
    public IReadOnlyList<Student> AssignedStudents { get; init; } = [];

    /// <summary>
    ///     Item captions in the same order as <see cref="Students" /> / <see cref="Prizes" />, already formatted with the
    ///     host's result settings（点名/闪抽按「显示格式」= 编号和名称 / 仅名称 / 仅编号，抽奖按抽奖显示模板）。
    ///     A presenter that captions one item per card should prefer these over composing its own text, so what it shows
    ///     matches the host's built-in result cards. Empty when the host has nothing to caption or is too old to send it.
    /// </summary>
    public IReadOnlyList<string> DisplayTitles { get; init; } = [];

    /// <summary>Student list the round was drawn from.</summary>
    public string ListName { get; init; } = string.Empty;

    /// <summary>Prize pool name (lottery).</summary>
    public string PrizeListName { get; init; } = string.Empty;

    /// <summary>Group scope the round was restricted to; empty means "all".</summary>
    public string GroupScope { get; init; } = string.Empty;

    /// <summary>Gender scope the round was restricted to; empty means "all".</summary>
    public string GenderScope { get; init; } = string.Empty;

    /// <summary>Linked course name, when the host is driven by a linked platform.</summary>
    public string CourseName { get; init; } = string.Empty;

    /// <summary>How many items the caller asked for (may exceed <see cref="ItemCount" />).</summary>
    public int RequestedCount { get; init; }

    /// <summary>Verification proof id of the committed round, when the channel issued one.</summary>
    public Guid ProofId { get; init; }

    /// <summary>Draw round id assigned by the host's commit service.</summary>
    public string DrawRoundId { get; init; } = string.Empty;

    /// <summary>Commit time of the round.</summary>
    public DateTime DrawTime { get; init; } = DateTime.Now;

    /// <summary>
    ///     用户在"动画样式"里选的样式：<see cref="DrawAnimationSelection.Host" /> 表示宿主内置动画，
    ///     其他值是插件呈现器的 id，空字符串表示没人告诉宿主用户选了什么（保持插件自动接管的旧行为）。
    /// </summary>
    public string PluginAnimationId { get; init; } = string.Empty;

    /// <summary><c>true</c> for the rolling phase.</summary>
    public bool IsPreview => Phase == DrawPresentationPhase.Preview;

    /// <summary>Number of presented items (students for roll call, prizes for lottery).</summary>
    public int ItemCount => Students.Count > 0 ? Students.Count : Prizes.Count;

    /// <summary>
    ///     Name of the round's subject, for display: the student list name, the prize pool name (lottery),
    ///     or an empty string when neither is known.
    /// </summary>
    public string ScopeName => !string.IsNullOrWhiteSpace(PrizeListName) ? PrizeListName : ListName;
}
