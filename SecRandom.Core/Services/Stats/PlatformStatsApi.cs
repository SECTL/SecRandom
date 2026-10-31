namespace SecRandom.Core.Services.Stats;

/// <summary>
///     The SECTL platform-statistics API this client reports to, and the one reporting entrance it uses.
///     Every reporting channel posts to <see cref="ReportUri" /> — the aggregate entrance — instead of a per-type
///     endpoint. The service decides the report type from the fields present and answers `400` when it cannot
///     judge; the four old per-type endpoints (`/api/stats/online`, `/api/stats/usage/increment`,
///     `/api/stats/version`, `/api/fields/values`) are compatibility-only. Posting the right fields to the wrong
///     old endpoint does not fail, it silently books the number under another figure — a version string counted as
///     an increment, a heartbeat counted as a version — which is exactly what the aggregate entrance prevents.
///     The judgment runs in this order and stops at the first hit, so a payload must never carry a field of an
///     earlier rule than the one it wants to match:
///     1. `field_key` together with `value` → field (last-value report);
///     2. `version` or `app_version` → version (deduplicated per installation);
///     3. `metrics` / `increments` / `usage` / `delta` / `metric_key` → usage (increment);
///     4. anything else, i.e. only `platform_id` / `device_uuid` / `device_type` → online (heartbeat).
/// </summary>
public static class PlatformStatsApi
{
    public const string BaseUrl = "https://appwrite.sectl.cn";
    public const string PlatformId = "69c8cd6a0012dd3ea10a";

    /// <summary>Public reporting entrance: the report type is inferred from the payload fields.</summary>
    public static readonly Uri ReportUri = new($"{BaseUrl}/api/stats/report");
}
