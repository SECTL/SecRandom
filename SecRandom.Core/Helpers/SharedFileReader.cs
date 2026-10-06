using System.IO;
using System.Text;

namespace SecRandom.Core.Helpers;

/// <summary>
///     Reads files that SecRandom itself may still hold open for writing. The live log file is the
///     important case: <see cref="Services.Logging.FileLoggerProvider" /> keeps it open as
///     <c>FileAccess.ReadWrite</c> for the whole session, and a plain <c>File.OpenRead</c> /
///     <c>File.ReadAllText</c> (share mode <c>Read</c> only) is refused by Windows with a sharing
///     violation. Opening with <c>ReadWrite</c> + <c>Delete</c> also keeps this reader from blocking
///     a concurrent writer or delete, so exports and diagnostics can always read application data.
/// </summary>
public static class SharedFileReader
{
    private const int BufferSize = 81920;

    public static FileStream OpenRead(string path)
    {
        return new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, BufferSize, FileOptions.SequentialScan);
    }

    public static string ReadAllText(string path)
    {
        using var stream = OpenRead(path);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
