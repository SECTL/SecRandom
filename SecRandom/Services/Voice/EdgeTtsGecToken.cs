using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SecRandom.Services.Voice;

/// <summary>
///     生成 Edge 合成接口要求的 <c>Sec-MS-GEC</c> 令牌。
/// </summary>
/// <remarks>
///     <para>
///         微软的 readaloud 接口只接受"当前 5 分钟窗口 + v2 令牌"：令牌形如
///         <c>&lt;16 位 CS64&gt;-&lt;64 位摘要&gt;</c>，并与 <c>Sec-MS-GEC-Version=2-&lt;Chromium 版本&gt;</c> 配对。
///         老式"直接 SHA-256"的 v1 令牌现在一律被 403 拒绝，这正是 Edge TTS 语音播报失效的原因。
///     </para>
///     <para>
///         这里的摘要<strong>不是</strong>标准 SHA-256：上游 edge-tts 的 <c>drm.py</c> 故意改了一个轮常量、
///         预置了首块状态并使用自造的 schedule 种子，所以用 <see cref="System.Security.Cryptography.SHA256" />
///         顶替会得到另一个值并被服务端拒绝。请保持与
///         <c>https://github.com/rany2/edge-tts/blob/master/src/edge_tts/drm.py</c> 逐位一致，
///         改动后必须用 <c>EdgeTtsGecTokenTests</c> 里的参考向量复核。
///     </para>
///     <para>
///         令牌只依赖本机时钟：窗口按 5 分钟向下取整，因此系统时间偏差超过一个窗口时同样会被拒绝，
///         与账号、地区、密钥都无关。
///     </para>
/// </remarks>
public static class EdgeTtsGecToken
{
    private const string TrustedClientToken = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";

    // 5 分钟，以 100 纳秒为单位（file time tick）。
    private const long WindowTicks = 3_000_000_000;

    /// <summary>为给定时刻生成一个 v2 令牌。</summary>
    public static string Create(DateTimeOffset utcNow)
    {
        var ticks = utcNow.UtcDateTime.ToFileTimeUtc();
        ticks -= ticks % WindowTicks;

        var payload = Encoding.ASCII.GetBytes(
            ticks.ToString(CultureInfo.InvariantCulture) + TrustedClientToken);
        var digest = EdgeTtsModifiedSha256.Hash(payload);
        var (first, second) = EdgeTtsCs64Digest.Compute(payload, digest);

        var prefix = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(prefix, first);
        BinaryPrimitives.WriteUInt32LittleEndian(prefix.AsSpan(4), second);
        return $"{Convert.ToHexString(prefix)}-{Convert.ToHexString(digest)}";
    }
}

/// <summary>
///     edge-tts <c>drm.py</c> 中与标准 SHA-256 不同的一版压缩函数。
/// </summary>
/// <remarks>
///     形状仍是 SHA-256 的 Merkle–Damgård 结构，但首块的状态被预计算、第 14 号轮常量被替换
///     （<c>0xC9DC06A7</c>）、后续块的 schedule 种子也另有一套，因此结果与
///     <see cref="System.Security.Cryptography.SHA256" /> 不同。
/// </remarks>
internal static class EdgeTtsModifiedSha256
{
    private static readonly uint[] InitialState =
    [
        0x6A09E667, 0xBB67AE85, 0x3C6EF372, 0xA54FF53A, 0x510E527F, 0x9B05688C, 0x1F83D9AB, 0x5BE0CD19,
    ];

    private static readonly uint[] FirstBlockState =
    [
        0xFD9FBEA2, 0x91243CE7, 0xBE499094, 0xCD2E1E0A, 0x6C006F49, 0x04BD35AB, 0xFEB0A7E4, 0x03EFFBD2,
    ];

    private static readonly uint[] ScheduleSeeds =
    [
        0xEB24BF9E, 0x1558691F, 0xA0C09EE2, 0x0CFA32BA, 0xA622EE8C, 0xD1DD9491, 0xC0DD25A0,
        0xF9E70AB6, 0xF0C31573, 0x7058CE4E, 0x7188E606, 0x00E5AB33, 0xD258B060, 0x72766564,
    ];

    private static readonly uint[] RoundConstants =
    [
        0x428A2F98, 0x71374491, 0xB5C0FBCF, 0xE9B5DBA5, 0x3956C25B, 0x59F111F1, 0x923F82A4, 0xAB1C5ED5,
        0xD807AA98, 0x12835B01, 0x243185BE, 0x550C7DC3, 0x72BE5D74, 0x80DEB1FE, 0x9BDC06A7, 0xC19BF174,
        0xE49B69C1, 0xEFBE4786, 0x0FC19DC6, 0x240CA1CC, 0x2DE92C6F, 0x4A7484AA, 0x5CB0A9DC, 0x76F988DA,
        0x983E5152, 0xA831C66D, 0xB00327C8, 0xBF597FC7, 0xC6E00BF3, 0xD5A79147, 0x06CA6351, 0x14292967,
        0x27B70A85, 0x2E1B2138, 0x4D2C6DFC, 0x53380D13, 0x650A7354, 0x766A0ABB, 0x81C2C92E, 0x92722C85,
        0xA2BFE8A1, 0xA81A664B, 0xC24B8B70, 0xC76C51A3, 0xD192E819, 0xD6990624, 0xF40E3585, 0x106AA070,
        0x19A4C116, 0x1E376C08, 0x2748774C, 0x34B0BCB5, 0x391C0CB3, 0x4ED8AA4A, 0x5B9CCA4F, 0x682E6FF3,
        0x748F82EE, 0x78A5636F, 0x84C87814, 0x8CC70208, 0x90BEFFFA, 0xA4506CEB, 0xBEF9A3F7, 0xC67178F2,
    ];

    private static readonly uint[] FirstBlockConstants = BuildFirstBlockConstants();

    public static byte[] Hash(byte[] data) => Hash(data, 0, data.Length);

    private static byte[] Hash(byte[] data, int offset, int count)
    {
        // 首块由前 7 个字节折叠出状态，后面的字节按常规分块压缩。
        var state = InitFirstBlock(data.AsSpan(offset, 7));

        var totalLength = 57 + count;
        var zeroRun = ((55 - totalLength) % 64 + 64) % 64;

        var block = new byte[64];
        var filled = 0;
        var cursor = 0;
        var tail = data.AsSpan(offset + 7, count - 7);
        while (cursor < tail.Length)
        {
            var take = Math.Min(64 - filled, tail.Length - cursor);
            tail.Slice(cursor, take).CopyTo(block.AsSpan(filled));
            filled += take;
            cursor += take;
            if (filled != 64)
                continue;
            state = Compress(state, block, 0, RoundConstants);
            filled = 0;
        }

        block[filled++] = 0x80;
        filled += zeroRun;
        BinaryPrimitives.WriteUInt64BigEndian(block.AsSpan(filled), (ulong)totalLength * 8);
        state = Compress(state, block, 0, RoundConstants);

        var digest = new byte[32];
        for (var i = 0; i < 8; i++)
            BinaryPrimitives.WriteUInt32BigEndian(digest.AsSpan(i * 4), state[i]);
        return digest;
    }

    private static uint[] InitFirstBlock(ReadOnlySpan<byte> head)
    {
        var words = new uint[64];
        words[14] = ((uint)head[0] << 16) | ((uint)head[1] << 8) | head[2];
        words[15] = ((uint)head[3] << 24) | ((uint)head[4] << 16) | ((uint)head[5] << 8) | head[6];

        for (var index = 16; index <= 29; index++)
        {
            var value = ScheduleSeeds[index - 16];
            value = unchecked(value + (index == 16
                ? Sigma1(words[index - 2]) ^ 0x000B92C0u
                : Sigma1(words[index - 2])));
            value = unchecked(value + words[index - 7]);
            value = unchecked(value + (index == 29
                ? Sigma0(words[index - 15]) ^ 0x059C0B80u
                : Sigma0(words[index - 15])));
            words[index] = value;
        }

        words[30] = unchecked(Sigma1(words[28]) + words[23] + Sigma0(words[15]) + words[14]
                              + 0xC9DC06A7u - RoundConstants[14]);
        Expand(words, 31);

        var working = Rounds(FirstBlockState, words, 14, FirstBlockConstants);
        var state = new uint[8];
        for (var i = 0; i < 8; i++)
            state[i] = unchecked(InitialState[i] + working[i]);
        return state;
    }

    private static uint[] Compress(uint[] state, byte[] block, int firstRound, uint[] constants)
    {
        var words = new uint[64];
        for (var i = 0; i < 16; i++)
            words[i] = BinaryPrimitives.ReadUInt32BigEndian(block.AsSpan(i * 4));
        Expand(words, 16);

        var working = Rounds(state, words, firstRound, constants);
        var next = new uint[8];
        for (var i = 0; i < 8; i++)
            next[i] = unchecked(state[i] + working[i]);
        return next;
    }

    private static void Expand(uint[] words, int firstWord)
    {
        for (var index = firstWord; index < 64; index++)
        {
            words[index] = unchecked(Sigma1(words[index - 2]) + words[index - 7]
                                     + Sigma0(words[index - 15]) + words[index - 16]);
        }
    }

    private static uint[] Rounds(uint[] state, uint[] words, int firstRound, uint[] constants)
    {
        uint a = state[0], b = state[1], c = state[2], d = state[3];
        uint e = state[4], f = state[5], g = state[6], h = state[7];
        for (var index = firstRound; index < 64; index++)
        {
            var t1 = unchecked(h + (Rotr(e, 6) ^ Rotr(e, 11) ^ Rotr(e, 25)) + (g ^ (e & (f ^ g)))
                               + constants[index] + words[index]);
            var t2 = unchecked((Rotr(a, 2) ^ Rotr(a, 13) ^ Rotr(a, 22)) + ((a & b) | (c & (a | b))));
            h = g;
            g = f;
            f = e;
            e = unchecked(d + t1);
            d = c;
            c = b;
            b = a;
            a = unchecked(t1 + t2);
        }

        return [a, b, c, d, e, f, g, h];
    }

    private static uint[] BuildFirstBlockConstants()
    {
        var constants = (uint[])RoundConstants.Clone();
        constants[14] = 0xC9DC06A7u;
        return constants;
    }

    private static uint Sigma0(uint value) =>
        unchecked((value >> 7) ^ (value << 25) ^ (value >> 18) ^ (value << 14) ^ (value >> 3));

    private static uint Sigma1(uint value) =>
        unchecked((value >> 17) ^ (value << 15) ^ (value >> 19) ^ (value << 13) ^ (value >> 10));

    private static uint Rotr(uint value, int count) => (value >> count) | (value << (32 - count));
}

/// <summary>
///     令牌前半段：以摘要前两个小端字为密钥的 CS64 混合摘要。
/// </summary>
internal static class EdgeTtsCs64Digest
{
    private static readonly (uint Coefficient, uint Offset)[] Coefficients =
    [
        (0xE82E91B1, 0xB32700D0), (0x44DE24C6, 0x88DFF51D), (0x3BEB65BC, 0x29514038),
        (0xC89C5933, 0xB328CBDF), (0x8D1EC211, 0xD405B4D5), (0x314EF5D5, 0x26E55F3C),
        (0xE92491B1, 0x607801C6), (0xF0A5B9D5, 0x82B2C1FB), (0x06064CF3, 0xFA51A3DA),
        (0xDA79FA60, 0x207CC464), (0xBED20560, 0x3C3C74F3), (0x89F9C9F8, 0xD2D14B61),
        (0x35FA38C6, 0xBB04A2C1), (0xCB49E3C6, 0xC5626167),
    ];

    public static (uint First, uint Second) Compute(byte[] data, byte[] digest)
    {
        var keyFirst = BinaryPrimitives.ReadUInt32LittleEndian(digest) | 1u;
        var keySecond = BinaryPrimitives.ReadUInt32LittleEndian(digest.AsSpan(4)) | 1u;
        var factorFirst = unchecked(keyFirst * 0x82893933u);
        var factorSecond = unchecked(keySecond * 0x82893933u);

        var terms = new List<uint>(64);
        var offsets = new List<uint>(64);
        foreach (var (coefficient, offset) in Coefficients)
        {
            terms.Add(unchecked(coefficient * (terms.Count % 2 == 0 ? factorFirst : factorSecond)));
            offsets.Add(offset);
        }

        var position = 0;
        foreach (var word in LittleEndianWords(data))
        {
            var key = position % 2 == 0 ? keyFirst : keySecond;
            uint term = unchecked(word * key);
            uint offset = unchecked(Rol16(word) * (position % 2 == 0 ? 0xEF0569FBu : 0xC31713DBu));
            if (position == 0)
            {
                term = unchecked(term + 0xF2F90B1Au * factorFirst);
                offset = unchecked(offset + 0x0B1A0000u);
            }

            terms.Add(term);
            offsets.Add(offset);
            position++;
        }

        uint swapAccumulator = 0, swapLast = 0, state = 0, accumulator = 0;
        for (var index = 0; index < terms.Count; index += 2)
        {
            var x = terms[index];
            var y = terms[index + 1];

            var t = Rol16(unchecked(offsets[index] + x));
            var q = unchecked(t * 0x76F50F34u);
            swapAccumulator = unchecked(swapAccumulator + unchecked(Rol16(q) * 0xC3EFEA97u) + q);

            t = Rol16(unchecked(offsets[index + 1] + y));
            q = unchecked(t * 0x1881CE3Cu);
            swapLast = unchecked(unchecked(Rol16(q) * 0x35BD1EC9u) + q);
            swapAccumulator = unchecked(swapAccumulator + swapLast);

            state = unchecked(state * keyFirst + x);
            state = unchecked(Rol16(state) * 0xCF98B111u);
            state = unchecked(Rol16(state) * 0x87085B9Fu);
            state = unchecked(Rol16(state) * 0x12CEB96Du);
            state = unchecked(Rol16(state) * 0x257E1D83u);
            accumulator = unchecked(accumulator + state);

            t = unchecked(state * keySecond + y);
            t = unchecked(Rol16(t) * 0xA27416F5u);
            t = unchecked(Rol16(t) * 0xD38396FFu);
            state = unchecked(Rol16(unchecked(Rol16(t) * 0x7C932B89u)) * 0xBFA49F69u);
            accumulator = unchecked(accumulator + state);
        }

        return (unchecked(swapLast ^ state), unchecked(swapAccumulator ^ accumulator));
    }

    // 对应 drm.py 的 _words_le4_even(b"\x00" + data)：前置一个零字节、补齐到 4 字节、
    // 再按小端拆字，且字数为奇数时补一个零字。
    private static List<uint> LittleEndianWords(byte[] data)
    {
        var prefixed = new byte[data.Length + 1];
        Buffer.BlockCopy(data, 0, prefixed, 1, data.Length);

        var padded = new byte[prefixed.Length + ((4 - (prefixed.Length % 4)) % 4)];
        Buffer.BlockCopy(prefixed, 0, padded, 0, prefixed.Length);

        var words = new List<uint>(padded.Length / 4);
        for (var i = 0; i < padded.Length; i += 4)
            words.Add(BinaryPrimitives.ReadUInt32LittleEndian(padded.AsSpan(i)));
        if (words.Count % 2 != 0)
            words.Add(0);
        return words;
    }

    private static uint Rol16(uint value) => unchecked((value << 16) | (value >> 16));
}
