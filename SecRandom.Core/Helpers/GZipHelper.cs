using System.IO.Compression;

namespace SecRandom.Core.Helpers;

public static class GZipHelper
{
    public static void CompressFileAndDelete(string path)
    {
        var compressedPath = path + ".gz";
        // 先写临时文件再原子替换：日志查看器与诊断导出随时可能来读这个 .gz，
        // File.Create 的 FileShare.None 会让它们读到占用错误或半成品
        var temporaryPath = compressedPath + ".tmp";
        try
        {
            using (var originalFileStream = SharedFileReader.OpenRead(path))
            using (var compressedFileStream = File.Create(temporaryPath))
            using (var compressor = new GZipStream(compressedFileStream, CompressionMode.Compress))
            {
                originalFileStream.CopyTo(compressor);
            }

            File.Move(temporaryPath, compressedPath, overwrite: true);
            File.Delete(path);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // 清理临时文件失败不影响调用方；下一次压缩会覆盖它
        }
    }
}
