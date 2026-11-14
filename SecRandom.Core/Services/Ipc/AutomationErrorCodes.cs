namespace SecRandom.Core.Services.Ipc;

/// <summary>
/// Stable v2 error codes. Keep these values byte-compatible with
/// docs/ipc-url-protocol.md; v1 codes are separate and must not change.
/// </summary>
public static class AutomationErrorCodes
{
    public const string InvalidRequest = "invalid_request";
    public const string UnsupportedVersion = "unsupported_version";
    public const string UnsupportedMethod = "unsupported_method";
    public const string InvalidParameter = "invalid_parameter";
    public const string MissingParameter = "missing_parameter";
    public const string NotFound = "not_found";
    public const string PageNotFound = "page_not_found";
    public const string PageNotAvailable = "page_not_available";
    public const string InvalidState = "invalid_state";
    public const string FeatureDisabled = "feature_disabled";
    public const string AuthorizationDenied = "authorization_denied";
    public const string LocalVerificationRequired = "local_verification_required";
    public const string OobeRequired = "oobe_required";
    public const string IntegrityConfirmationRequired = "integrity_confirmation_required";
    public const string IpcDisabled = "ipc_disabled";
    public const string UrlActivationForbidden = "url_activation_forbidden";
    public const string RateLimited = "rate_limited";
    public const string Busy = "busy";
    public const string Timeout = "timeout";
    public const string InternalError = "internal_error";
    public const string PipeUnavailable = "pipe_unavailable";
}

public static class AutomationScopes
{
    public const string ReadState = "read.state";
    public const string ReadRoster = "read.roster";
    public const string ReadSettings = "read.settings";
    public const string ReadHistory = "read.history";
    public const string ReadEvents = "read.events";
    public const string WriteWindow = "write.window";
    public const string WriteSettings = "write.settings";
    public const string WriteRoster = "write.roster";
    public const string WriteDraw = "write.draw";
    public const string Lifecycle = "lifecycle";
}