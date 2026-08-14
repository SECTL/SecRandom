using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SecRandom.Shared;

namespace SecRandom.Services.Voice;

/// <summary>
/// Keeps the one MiMo voice-clone reference recording in the private voice-config directory.
/// The recording is never serialized into settings, logs, IPC payloads, or backup archives.
/// </summary>
public sealed class MiMoVoiceReferenceStore
{
    private const string ReferenceFileName = "mimo-voice-reference.wav";
    private readonly object _gate = new();

    private string FilePath => Utils.GetFilePath("config", "voice", ReferenceFileName);

    public bool HasReference()
    {
        lock (_gate)
        {
            return File.Exists(FilePath) && new FileInfo(FilePath).Length > 0;
        }
    }

    public async Task<string> ReplaceAsync(Stream source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var filePath = FilePath;
        var temporaryPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var header = new byte[12];
            var headerLength = 0;
            await using (var target = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 81920,
                             useAsync: true))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    hash.AppendData(buffer, 0, read);
                    if (headerLength < header.Length)
                    {
                        var headerBytes = Math.Min(read, header.Length - headerLength);
                        Buffer.BlockCopy(buffer, 0, header, headerLength, headerBytes);
                        headerLength += headerBytes;
                    }
                }

                if (target.Length == 0)
                    throw new InvalidOperationException("The voice-clone reference audio is empty.");
                if (headerLength < header.Length ||
                    !header.AsSpan(0, 4).SequenceEqual(Encoding.ASCII.GetBytes("RIFF")) ||
                    !header.AsSpan(8, 4).SequenceEqual(Encoding.ASCII.GetBytes("WAVE")))
                    throw new InvalidDataException("The voice-clone reference audio must be a WAV file.");

                await target.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            lock (_gate)
            {
                File.Move(temporaryPath, filePath, overwrite: true);
            }

            return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public async Task<string> GetDataUriAsync(CancellationToken cancellationToken = default)
    {
        var filePath = FilePath;
        if (!HasReference())
            throw new InvalidOperationException("MiMo voice-clone reference audio is not configured.");

        var audio = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
        if (audio.Length == 0)
            throw new InvalidOperationException("MiMo voice-clone reference audio is empty.");

        return $"data:audio/wav;base64,{Convert.ToBase64String(audio)}";
    }

    public bool Clear()
    {
        lock (_gate)
        {
            if (!File.Exists(FilePath))
                return false;

            File.Delete(FilePath);
            return true;
        }
    }
}
