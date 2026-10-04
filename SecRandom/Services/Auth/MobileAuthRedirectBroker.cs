using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using SecRandom.Mobile;
using SecRandom.Shared;

namespace SecRandom.Services.Auth;

/// <summary>Creates the mobile redirect broker over the one shared callback router.</summary>
public sealed class MobileAuthRedirectBrokerFactory(MobileAuthCallbackRouter router) : IAuthRedirectBrokerFactory
{
    public IAuthRedirectBroker Create() => new MobileAuthRedirectBroker(router);
}

/// <summary>
///     Mobile redirect broker: the redirect comes back through the app's custom URL scheme, so the
///     attempt (PKCE verifier, state, redirect URI) is recorded on disk before the system browser
///     opens. A phone may reclaim the backgrounded process while the user is signing in, and the
///     redirect then arrives as a cold start; that record is what lets the redirect still be exchanged.
///     The record lives beside the token file under <c>data/config</c>, which no archive root carries.
/// </summary>
public sealed class MobileAuthRedirectBroker(MobileAuthCallbackRouter router) : IAuthRedirectBroker
{
    /// <summary>
    ///     How long a recorded attempt stays usable. It also bounds how long the sign-in call waits:
    ///     a phone user leaves the app to authorize and comes back.
    /// </summary>
    internal static readonly TimeSpan PendingLifetime = TimeSpan.FromMinutes(10);

    private const string PendingFileName = "sectl-auth-pending.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public TimeSpan WaitTimeout => PendingLifetime;

    public string CreateRedirectUri() => MobileAuthCallbackRouter.RedirectUri;

    public Task PrepareAsync(AuthRedirectAttempt attempt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        WritePending(new PendingAuthorization(attempt.RedirectUri, attempt.State, attempt.CodeVerifier,
            DateTimeOffset.UtcNow));
        return Task.CompletedTask;
    }

    public async Task<AuthRedirectResult> WaitForRedirectAsync(AuthRedirectAttempt attempt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        var redirect = await router.WaitAsync(WaitTimeout, cancellationToken).ConfigureAwait(false);
        var result = ToResult(redirect, attempt);
        // Only the redirect of this attempt consumes the record; a stale URL keeps it for a resume.
        if (string.Equals(result.State, attempt.State, StringComparison.Ordinal))
            DeletePending();

        return result;
    }

    public Task<AuthRedirectResult?> TakePendingRedirectAsync(CancellationToken cancellationToken = default)
    {
        var pending = ReadPending();
        if (pending is null)
            return Task.FromResult<AuthRedirectResult?>(null);
        if (!router.TryTakeBuffered(out var redirect) || redirect is null)
            return Task.FromResult<AuthRedirectResult?>(null);

        var result = ToResult(redirect, new AuthRedirectAttempt(pending.RedirectUri, pending.State,
            pending.CodeVerifier));
        if (!string.Equals(result.State, pending.State, StringComparison.Ordinal))
        {
            // A redirect from another attempt can never complete this record; keep the record so a
            // late redirect that does match can still resume it.
            return Task.FromResult<AuthRedirectResult?>(null);
        }

        DeletePending();
        return Task.FromResult<AuthRedirectResult?>(result);
    }

    public void Dispose()
    {
        // Nothing to release: the router outlives the attempt and holds no per-attempt state.
    }

    private static AuthRedirectResult ToResult(Uri redirect, AuthRedirectAttempt attempt) =>
        new(attempt.RedirectUri,
            ReadQueryValue(redirect, "state") ?? string.Empty,
            attempt.CodeVerifier,
            ReadQueryValue(redirect, "code"),
            ReadQueryValue(redirect, "error_description") ?? ReadQueryValue(redirect, "error"));

    private static string PendingPath => Utils.GetFilePath("config", PendingFileName);

    private static void WritePending(PendingAuthorization pending)
    {
        var path = PendingPath;
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            var content = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(pending, JsonOptions));
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryDelete(temporary);
            throw;
        }
    }

    private static PendingAuthorization? ReadPending()
    {
        try
        {
            var path = PendingPath;
            if (!File.Exists(path))
                return null;

            var pending = JsonSerializer.Deserialize<PendingAuthorization>(File.ReadAllText(path), JsonOptions);
            if (pending is null || string.IsNullOrWhiteSpace(pending.CodeVerifier) ||
                string.IsNullOrWhiteSpace(pending.RedirectUri))
                return null;
            if (DateTimeOffset.UtcNow - pending.CreatedAt > PendingLifetime)
            {
                DeletePending();
                return null;
            }

            return pending;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // An unreadable record is the same as no record: the user simply authorizes again.
            return null;
        }
    }

    private static void DeletePending()
    {
        try
        {
            var path = PendingPath;
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover record expires on its own and can never outlive one authorization attempt.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string? ReadQueryValue(Uri uri, string name)
    {
        var query = uri.Query;
        if (query.Length <= 1)
            return null;

        foreach (var pair in query[1..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var key = separator < 0 ? pair : pair[..separator];
            if (!string.Equals(Uri.UnescapeDataString(key), name, StringComparison.Ordinal))
                continue;

            var value = separator < 0 ? string.Empty : pair[(separator + 1)..];
            return Uri.UnescapeDataString(value.Replace('+', ' '));
        }

        return null;
    }

    /// <summary>
    ///     The recorded authorization attempt. It is written before the browser opens, so the redirect
    ///     can be exchanged even when the process that started the sign-in is gone.
    /// </summary>
    private sealed record PendingAuthorization(
        string RedirectUri,
        string State,
        string CodeVerifier,
        DateTimeOffset CreatedAt);
}
