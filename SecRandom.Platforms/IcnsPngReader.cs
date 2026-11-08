using System.Buffers.Binary;

namespace SecRandom.Platforms;

/// <summary>
///     macOS <c>.icns</c> 容器里的 PNG 提取器。现代图标集（icp4…ic14）本身就是内嵌 PNG，
///     这里挑出体积最大的一张，避免额外引入图像解码依赖。
/// </summary>
public static class IcnsPngReader
{
    public const int MaxIconBytes = 4 * 1024 * 1024;

    private static ReadOnlySpan<byte> PngSignature =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    ///     从 ICNS 数据中找出最大的内嵌 PNG，返回剪裁到 PNG 末尾的字节；没有或格式异常时返回 <c>null</c>。
    /// </summary>
    public static byte[]? FindLargestPng(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8 || !data[..4].SequenceEqual("icns"u8))
            return null;

        var declaredLength = BinaryPrimitives.ReadUInt32BigEndian(data[4..8]);
        var end = (int)Math.Min(declaredLength, (uint)data.Length);
        if (end <= 8)
            end = data.Length;

        byte[]? best = null;
        var offset = 8;
        while (offset + 8 <= end)
        {
            var chunkLength = BinaryPrimitives.ReadUInt32BigEndian(data[(offset + 4)..(offset + 8)]);
            if (chunkLength < 8 || chunkLength > (uint)(end - offset))
                break;

            var payload = data.Slice(offset + 8, (int)chunkLength - 8);
            if (payload.Length > 8 && payload[..8].SequenceEqual(PngSignature) &&
                TryGetPngLength(payload, out var pngLength))
            {
                if (best is null || pngLength > best.Length)
                    best = payload[..pngLength].ToArray();
            }

            offset += (int)chunkLength;
        }

        if (best is { Length: > MaxIconBytes })
            return null;

        return best;
    }

    /// <summary>
    ///     按 PNG 自身的块长度算出真实结尾（ICNS 块可能带有额外填充）。
    /// </summary>
    public static bool TryGetPngLength(ReadOnlySpan<byte> png, out int length)
    {
        length = 0;
        if (png.Length < 8 || !png[..8].SequenceEqual(PngSignature))
            return false;

        var offset = 8;
        while (offset + 12 <= png.Length)
        {
            var chunkLength = BinaryPrimitives.ReadUInt32BigEndian(png[offset..(offset + 4)]);
            if (chunkLength > int.MaxValue)
                return false;

            var next = offset + 12 + (int)chunkLength;
            if (next > png.Length)
                return false;

            var type = png.Slice(offset + 4, 4);
            offset = next;
            if (type.SequenceEqual("IEND"u8))
            {
                length = offset;
                return true;
            }
        }

        return false;
    }
}
