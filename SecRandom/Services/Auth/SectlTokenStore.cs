using System.Text;
using System.Text.Json;
using SecRandom.Shared;

namespace SecRandom.Services.Auth;

/// <summary>
///     Owns the one copy of the SECTL OAuth access/refresh pair on disk.
///     Refresh tokens are single-use and rotate on every successful refresh, so an update must reach
///     the disk before the caller uses the new access token, and it must never be written in place:
///     a crash between "old refresh token invalidated" and "new refresh token persisted" kills the
///     session permanently. Every save therefore writes a temporary file, flushes it to the storage
///     device, and only then replaces the target in one rename.
/// </summary>
public sealed class SectlTokenStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public SectlTokenStore()
        : this(Utils.GetFilePath("config", "sectl-auth.json"))
    {
    }

    public SectlTokenStore(string tokenPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenPath);
        Path = System.IO.Path.GetFullPath(tokenPath);
        LockPath = Path + ".lock";
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
    }

    /// <summary>Absolute path of the token file.</summary>
    public string Path { get; }

    /// <summary>
    ///     Sibling lock file used to serialize refresh rotations that share this token file across
    ///     processes. The file is never deleted (an empty marker cannot be mistaken for a token) and
    ///     it is not part of any archive root, so it cannot travel with a backup.
    /// </summary>
    public string LockPath { get; }

    public async Task<SectlToken?> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(Path))
                return null;
            var json = await File.ReadAllTextAsync(Path, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<SectlToken>(json, JsonOptions);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // An unreadable or corrupt token file is the same as no session: the next successful
            // authorization rewrites it, and a refresh must never run against a half-read pair.
            return null;
        }
    }

    /// <summary>
    ///     Replaces the token file atomically. The temporary file is flushed to disk before the
    ///     rename so a power loss cannot expose a truncated or renamed-but-unwritten pair.
    /// </summary>
    public async Task SaveAsync(SectlToken token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        var directory = System.IO.Path.GetDirectoryName(Path)!;
        Directory.CreateDirectory(directory);
        var temporary = $"{Path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var content = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(token, JsonOptions));
                await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, Path, overwrite: true);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    public void Delete()
    {
        try
        {
            if (File.Exists(Path))
                File.Delete(Path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Clearing the in-memory session is what signs the account out; a locked file only
            // leaves a stale pair behind for the next successful authorization to overwrite.
        }
    }

    /// <summary>
    ///     Takes the cross-process refresh lock, waiting up to <paramref name="timeout" />. Returns
    ///     <see langword="null" /> when another process keeps the lock or the filesystem refuses it;
    ///     callers then continue with the in-process single-flight guarantee only.
    /// </summary>
    public async Task<IDisposable?> TryAcquireLockAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (true)
        {
            try
            {
                return new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                // Another process holds the lock; wait for its rotation to land on disk.
                if (DateTimeOffset.UtcNow >= deadline)
                    return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
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
}
