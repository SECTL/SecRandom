namespace SecRandom.Core.Services.ControlNode;

/// <summary>
///     解析上报给控制台的**设备显示名**。
/// </summary>
/// <remarks>
///     <para>
///         名字由用户在这台机器上填写（见设置页 <c>settings.general.control</c>）；
///         **留空则回落到主机名**——一台机器在控制台里不该只显示一串随机 <c>node_id</c>，
///         而教室里几十台机器长得一模一样，没有名字就无法辨认哪台是哪台。
///     </para>
///     <para>
///         这里只做两件客户端必须自己负责的事：去掉首尾空白、截断到协议上限
///         （截断时不留下半个代理对）。更细的规范化（零宽字符、内部空白折叠）由服务端
///         在入口统一完成——两边各写一份实现，迟早会漂移成"界面显示的与服务端存的不一样"。
///     </para>
/// </remarks>
public static class ControlNodeDisplayName
{
    /// <summary>协议上限（UTF-16 码元），与服务端 <c>NodeFrame.MaxDisplayNameLength</c> 一致。</summary>
    public const int MaxLength = 64;

    /// <summary>
    ///     用户填的名字优先，留空回落到主机名。
    /// </summary>
    /// <remarks>
    ///     两个来源都为空时返回 <c>null</c>，调用方应当让 <c>display_name</c> **整帧缺席**。
    ///     返回空串在这里是没有意义的：协议里空串是"清除名字"，会把管理端预置的名字抹掉，
    ///     而"本机取不到任何名字"根本不是用户要求清除。
    /// </remarks>
    public static string? Resolve(string? configured, string? hostName) =>
        Normalize(configured) ?? Normalize(hostName);

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (trimmed.Length <= MaxLength)
            return trimmed;

        // 截断点正好落在高位代理上就少取一个码元：否则会留下孤立代理，
        // 服务端会把它丢掉，控制台里看到的名字就平白少了一个字符。
        var length = MaxLength;
        if (char.IsHighSurrogate(trimmed[length - 1]))
            length--;

        return trimmed[..length];
    }
}
