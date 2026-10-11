using SecRandom.Core.Abstraction.Services.Capabilities;

namespace SecRandom.PluginSdk;

/// <summary>
///     清单 <c>permissions</c> 字段的名字表：在"人写的字符串"与
///     <see cref="PluginPermissions" /> 位标志之间转换，并把不认识的名字挡在外面。
///     <para>
///         比较时忽略大小写，也忽略名字里的 <c>_</c>、<c>-</c>、<c>.</c>，
///         所以 <c>ClipboardRead</c>、<c>clipboard_read</c>、<c>clipboard.read</c> 等价。
///     </para>
/// </summary>
public static class PluginPermissionNames
{
    private static readonly Dictionary<string, PluginPermissions> Lookup = BuildLookup();

    /// <summary>全部合法的权限名字（规范化形式，按顺序）。</summary>
    public static IReadOnlyList<string> All { get; } =
        Enum.GetValues<PluginPermissions>()
            .Where(static value => value != PluginPermissions.None)
            .Select(static value => value.ToString())
            .ToArray();

    /// <summary>把一个名字解析成权限位；不认识时返回 false。</summary>
    public static bool TryParse(string? name, out PluginPermissions permission)
    {
        permission = PluginPermissions.None;

        if (string.IsNullOrWhiteSpace(name))
            return false;

        return Lookup.TryGetValue(NormalizeKey(name), out permission);
    }

    /// <summary>
    ///     把清单里的一串名字收敛成"合法、去重、按枚举顺序"的名字表；
    ///     不认识的名字会被丢掉（插件在旧宿主上声明了新权限时不该因此装不上）。
    /// </summary>
    public static IReadOnlyList<string> Normalize(IEnumerable<string>? names)
    {
        if (names is null)
            return [];

        var flags = PluginPermissions.None;
        foreach (var name in names)
        {
            if (TryParse(name, out var permission))
                flags |= permission;
        }

        return Describe(flags);
    }

    /// <summary>把清单里的一串名字解析成权限位；不认识的名字被忽略。</summary>
    public static PluginPermissions Parse(IEnumerable<string>? names)
    {
        if (names is null)
            return PluginPermissions.None;

        var flags = PluginPermissions.None;
        foreach (var name in names)
        {
            if (TryParse(name, out var permission))
                flags |= permission;
        }

        return flags;
    }

    /// <summary>把权限位翻回规范化的名字表（<see cref="PluginPermissions.None" /> 返回空表）。</summary>
    public static IReadOnlyList<string> Describe(PluginPermissions permissions)
    {
        if (permissions == PluginPermissions.None)
            return [];

        return Enum.GetValues<PluginPermissions>()
            .Where(value => value != PluginPermissions.None && permissions.HasFlag(value))
            .Select(static value => value.ToString())
            .ToArray();
    }

    private static Dictionary<string, PluginPermissions> BuildLookup()
    {
        var lookup = new Dictionary<string, PluginPermissions>(StringComparer.OrdinalIgnoreCase);

        foreach (var value in Enum.GetValues<PluginPermissions>())
        {
            if (value == PluginPermissions.None)
            {
                // "None" 是合法写法（显式声明"什么都不用"）。
                lookup[NormalizeKey(nameof(PluginPermissions.None))] = PluginPermissions.None;
                continue;
            }

            lookup[NormalizeKey(value.ToString())] = value;
        }

        return lookup;
    }

    private static string NormalizeKey(string name) =>
        name.Replace("_", string.Empty).Replace("-", string.Empty).Replace(".", string.Empty);
}
