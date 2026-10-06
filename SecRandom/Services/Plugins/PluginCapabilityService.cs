using System;
using System.Collections.Generic;
using System.Linq;
using SecRandom.Core.Abstraction.Services.Capabilities;
using SecRandom.PluginSdk;

namespace SecRandom.Services.Plugins;

/// <summary>
///     桌面实现：把"宿主当前提供哪些扩展点"与"某个插件声明了哪些权限"汇总起来给插件查询。
///     <para>
///         它不拦截任何调用——权限是声明式策略，真正的执行点在各个扩展点自己身上；
///         插件在用到某个扩展点之前先 <see cref="Has" /> 一下，在支持权限声明的宿主上再问一句
///         <see cref="IsAllowed" />，就能在老宿主上优雅降级。
///     </para>
/// </summary>
public sealed class PluginCapabilityService : ICapabilityService
{
    private readonly PluginManager _pluginManager;
    private readonly IReadOnlyCollection<string> _capabilities;

    public PluginCapabilityService(PluginManager pluginManager)
    {
        _pluginManager = pluginManager ?? throw new ArgumentNullException(nameof(pluginManager));
        _capabilities = HostCapabilities.All;
    }

    /// <inheritdoc />
    public Version HostApiVersion => PluginApiVersions.Current;

    /// <inheritdoc />
    public IReadOnlyCollection<string> Capabilities => _capabilities;

    /// <inheritdoc />
    public bool Has(string capabilityId)
    {
        if (string.IsNullOrWhiteSpace(capabilityId))
            return false;

        return _capabilities.Contains(capabilityId.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public PluginPermissionSet GetPermissions(string pluginId)
    {
        var id = pluginId?.Trim() ?? string.Empty;
        if (id.Length == 0)
            return Undeclared(id);

        var manifest = _pluginManager.Plugins
            .FirstOrDefault(plugin => string.Equals(plugin.Manifest.Id, id, StringComparison.OrdinalIgnoreCase))
            ?.Manifest;

        if (manifest is null)
            return Undeclared(id);

        var declared = manifest.Permissions;
        if (declared is null || declared.Count == 0)
            return Undeclared(id);

        return new PluginPermissionSet(id, PluginPermissionNames.Parse(declared), IsDeclared: true);
    }

    /// <inheritdoc />
    public bool IsAllowed(string pluginId, PluginPermissions permission) =>
        GetPermissions(pluginId).Allows(permission);

    /// <summary>清单里没写 <c>permissions</c>（或插件还不认识）——按约定一律允许。</summary>
    private static PluginPermissionSet Undeclared(string pluginId) =>
        new(pluginId, PluginPermissions.None, IsDeclared: false);
}
