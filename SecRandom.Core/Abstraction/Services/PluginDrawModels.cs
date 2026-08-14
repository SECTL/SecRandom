using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Abstraction.Services;

/// <summary>
///     Plugin point-call draw request. <see cref="ListName"/> selects the student list; empty group/gender
///     scopes mean "all". Count is clamped by the host to the eligible remaining candidates.
/// </summary>
public sealed record PluginStudentDrawRequest(
    string ListName,
    string Group = "",
    string Gender = "",
    int Count = 1,
    string CourseName = "");

/// <summary>
///     Plugin lottery draw request. <see cref="PrizePoolName"/> selects the prize pool and an optional
///     student list may be assigned winners. Count is clamped by the host to the eligible remaining prizes.
/// </summary>
public sealed record PluginLotteryDrawRequest(
    string PrizePoolName,
    string StudentListName = "",
    string Group = "",
    string Gender = "",
    int Count = 1,
    string CourseName = "");

/// <summary>
///     Point-call draw result for plugins. <see cref="ProofId"/> and <see cref="DrawRoundId"/> come from the
///     host verification/commit pipeline so plugin draws stay fair and reproducible.
/// </summary>
public sealed record PluginStudentDrawResult(
    bool IsSuccess,
    IReadOnlyList<Student> Winners,
    Guid ProofId,
    string DrawRoundId);

/// <summary>
///     Lottery draw result for plugins. Prize and assigned-student history items share the returned
///     <see cref="DrawRoundId"/>; <see cref="PrizeProofId"/> identifies the prize verification proof.
/// </summary>
public sealed record PluginLotteryDrawResult(
    bool IsSuccess,
    IReadOnlyList<Prize> Winners,
    IReadOnlyList<Student> AssignedStudents,
    Guid PrizeProofId,
    string DrawRoundId);
