using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SecRandom.Shared.Models.Verification;

/// <summary>
///     Human-readable context for one draw, committed by the v2 proof hash so a receipt covers it.
/// </summary>
/// <remarks>
///     <para>
///         Every field below is either an enumeration member, a count, or a digest. Nothing here may carry
///         user-typed text: roster names, course names, group names, gender values, file paths, host names, or
///         device identifiers are excluded by contract, because this document is routinely shared with people who
///         never attended the class and with the public proof page.
///     </para>
///     <para>
///         The block exists because a v1 proof can be replayed but cannot be read: which channel drew, how big the
///         pool was, whether a filter narrowed it, and which round of an arranged schedule ran were all outside the
///         file. Those facts are now inside it, and — because they are inside the attested hash — editing them after
///         the fact invalidates the server receipt instead of silently rewriting the story.
///     </para>
/// </remarks>
public sealed record class DrawProofContext
{
    [JsonPropertyName("channel")]
    public string Channel { get; init; } = string.Empty;

    // Empty for a number draw, which has no draw-settings page behind it.
    [JsonPropertyName("settingsType")]
    public string SettingsType { get; init; } = string.Empty;

    [JsonPropertyName("drawMode")]
    public string DrawMode { get; init; } = string.Empty;

    [JsonPropertyName("samplingMode")]
    public string SamplingMode { get; init; } = string.Empty;

    [JsonPropertyName("algorithmProfile")]
    public string AlgorithmProfile { get; init; } = string.Empty;

    [JsonPropertyName("requestedCount")]
    public int RequestedCount { get; init; }

    [JsonPropertyName("pool")]
    public DrawProofPool Pool { get; init; } = new();

    [JsonPropertyName("scope")]
    public DrawProofScope Scope { get; init; } = new();

    // Present only for an arranged (orchestrated) draw.
    [JsonPropertyName("orchestration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DrawProofOrchestration? Orchestration { get; init; }

    [JsonPropertyName("roster")]
    public DrawProofRoster Roster { get; init; } = new();

    [JsonPropertyName("environment")]
    public DrawProofEnvironment Environment { get; init; } = new();

    /// <summary>SHA-256 commitment to the candidate pool used for this draw.</summary>
    [JsonPropertyName("candidatePoolCommitment")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CandidatePoolCommitment { get; init; }

    /// <summary>
    ///     One random identifier per draw, so several proofs saved in one lesson can be recognised as the same
    ///     session locally. It is deliberately not derived from the device, the profile, or the clock, and it is
    ///     never persisted anywhere else.
    /// </summary>
    [JsonPropertyName("sessionId")]
    public Guid SessionId { get; init; }
}

public sealed record class DrawProofPool
{
    [JsonPropertyName("candidateCount")]
    public int CandidateCount { get; init; }

    [JsonPropertyName("drawableCount")]
    public int DrawableCount { get; init; }

    [JsonPropertyName("zeroWeightCount")]
    public int ZeroWeightCount { get; init; }

    [JsonPropertyName("guaranteedCount")]
    public int GuaranteedCount { get; init; }

    [JsonPropertyName("excludedCount")]
    public int ExcludedCount { get; init; }
}

public sealed record class DrawProofScope
{
    /// <summary>
    ///     Which slice of profile history the per-candidate figures were read from: <c>global</c>, <c>course</c>,
    ///     <c>history-group</c>, or <c>course-and-history-group</c>. Only the axis is named; the course and the
    ///     group are user-typed text and never leave the device.
    /// </summary>
    [JsonPropertyName("history")]
    public string History { get; init; } = string.Empty;

    /// <summary>
    ///     Which candidate filters were applied, as kinds (<c>group</c>, <c>gender</c>, <c>course</c>, <c>tag</c>).
    ///     The filter values themselves are never emitted.
    /// </summary>
    [JsonPropertyName("filterKinds")]
    public IReadOnlyList<string> FilterKinds { get; init; } = [];
}

public sealed record class DrawProofOrchestration
{
    [JsonPropertyName("round")]
    public int Round { get; init; }

    [JsonPropertyName("roundCount")]
    public int RoundCount { get; init; }
}

public sealed record class DrawProofRoster
{
    /// <summary>
    ///     <c>student</c>, <c>prize</c>, or <c>number</c> for the synthetic range tickets.
    /// </summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    // Zero while the drawn list is not the active profile, i.e. exactly when Digest is empty.
    [JsonPropertyName("entryCount")]
    public int EntryCount { get; init; }

    /// <summary>
    ///     <c>v3</c> for the original unsalted digest, <c>v4</c> for a digest salted with <see cref="DigestSalt" />.
    ///     Empty exactly when <see cref="Digest" /> is empty (the drawn list was not the active profile).
    /// </summary>
    [JsonPropertyName("digestFormat")]
    public string DigestFormat { get; init; } = string.Empty;

    [JsonPropertyName("digest")]
    public string Digest { get; init; } = string.Empty;

    /// <summary>
    ///     Per-proof random salt, published so the digest stays recomputable by someone who already holds the
    ///     roster. Its purpose is unlinkability, not secrecy: without it, two unrelated proofs taken from the same
    ///     list carry the same digest, which tells an observer that both came from one roster even when neither
    ///     proof names it. Absent for <c>v3</c> digests and for a proof with no roster commitment.
    /// </summary>
    [JsonPropertyName("digestSalt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DigestSalt { get; init; }
}

/// <summary>
///     Coarse execution environment. Deliberately not an identity: the build version, the platform family, and the
///     UTC offset only. Host name, user name, device UUID, IP address, and the time-zone id (which names a region)
///     stay out.
/// </summary>
public sealed record class DrawProofEnvironment
{
    [JsonPropertyName("appVersion")]
    public string AppVersion { get; init; } = string.Empty;

    [JsonPropertyName("platformFamily")]
    public string PlatformFamily { get; init; } = string.Empty;

    [JsonPropertyName("timeZoneOffsetMinutes")]
    public int TimeZoneOffsetMinutes { get; init; }
}
