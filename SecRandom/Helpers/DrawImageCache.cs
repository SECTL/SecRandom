using System;
using System.Collections.Generic;
using Avalonia.Media.Imaging;

namespace SecRandom.Helpers;

/// <summary>
///     抽取结果图片的解码缓存。
///     <para>
///         抽取预览是逐帧刷新的（默认每 80ms 一帧，手动停止模式则会一直循环），而结果项每次重建都会
///         重新解码磁盘上的照片。按 <c>路径 + 最后写入时间</c> 缓存后，一轮抽取里同一张照片只解码一次，
///         省掉持续的文件读取与位图解码，也避免每帧产生一批只等 GC 回收的原生位图。
///     </para>
///     <para>
///         缓存条目**不主动 Dispose**：位图可能仍被当前显示的结果项引用，提前释放会让正在渲染的
///         图片失效。超出上限时只丢弃引用，交给 GC 回收。
///     </para>
/// </summary>
internal static class DrawImageCache
{
    /// <summary>缓存上限，足够容纳一个班级的照片，同时避免无限制保留原生位图</summary>
    private const int MaxEntries = 128;

    private static readonly object Gate = new();
    private static readonly Dictionary<string, Entry> Entries = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Queue<string> InsertionOrder = new();

    public static Bitmap? Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        long stamp;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
                return null;

            stamp = info.LastWriteTimeUtc.Ticks;
        }
        catch
        {
            return null;
        }

        lock (Gate)
        {
            if (Entries.TryGetValue(path, out var cached) && cached.Stamp == stamp)
                return cached.Bitmap;

            Bitmap bitmap;
            try
            {
                bitmap = new Bitmap(path);
            }
            catch
            {
                return null;
            }

            Store(path, stamp, bitmap);
            return bitmap;
        }
    }

    private static void Store(string path, long stamp, Bitmap bitmap)
    {
        if (Entries.ContainsKey(path))
        {
            // 文件被替换过：覆盖条目，旧位图留在原处等待 GC，不做 Dispose
            Entries[path] = new Entry(stamp, bitmap);
            return;
        }

        while (Entries.Count >= MaxEntries && InsertionOrder.Count > 0)
            Entries.Remove(InsertionOrder.Dequeue());

        Entries[path] = new Entry(stamp, bitmap);
        InsertionOrder.Enqueue(path);
    }

    private readonly record struct Entry(long Stamp, Bitmap Bitmap);
}
