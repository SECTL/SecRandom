using System;
using System.Collections.Generic;
using System.Linq;

namespace SecRandom.Core.Services.Profiles;

/// <summary>
///     把一个抽取界面「默认记录组」的配置值解析成本次抽取实际使用的组范围。
/// </summary>
/// <param name="Group">
///     实际使用的组范围：<see langword="null" /> = 不做组隔离（看全部历史），
///     空串 = 默认记录组（只算未分组的记录），其余 = 该具名组。
/// </param>
/// <param name="ConfiguredGroupMissing">配置里那个组已经不存在，本次回落到默认记录组。</param>
/// <remarks>
///     <para>
///         三态在这里收口，因为点名、抽奖、闪抽三个界面读的是同一套含义：配置里的空串就是
///         「默认记录组」，不是「不分组」。功能关闭时才会退化成真正的「不分组」——那时选择器
///         根本不显示，没人能看出当前按哪个范围算权重，所以只能按全部历史。
///     </para>
///     <para>
///         <paramref name="availableGroupNames" /> 传 <see langword="null" /> 表示目录这次读不到：
///         此时不根据一个未知的清单去判断组名是否失效，而是按「不做组隔离」处理，因为把权重
///         静默收窄到一个可能不存在的组，比暂时按全部历史更糟。
///     </para>
///     <para>
///         组名已被删除时回落到默认记录组而不是全部历史：删除组只会把它的记录移回默认组，
///         回落过去仍然是用户原本在用的那批历史。
///     </para>
/// </remarks>
public readonly record struct HistoryGroupScope(string? Group, bool ConfiguredGroupMissing)
{
    /// <summary>解析默认记录组配置。</summary>
    /// <param name="enabled">历史记录组总开关。</param>
    /// <param name="configuredGroup">配置里保存的组名（空串 = 默认记录组）。</param>
    /// <param name="availableGroupNames">当前档案里可用的组名；<see langword="null" /> = 这次读不到。</param>
    public static HistoryGroupScope Resolve(
        bool enabled,
        string? configuredGroup,
        IReadOnlyList<string>? availableGroupNames)
    {
        if (!enabled)
            return new HistoryGroupScope(null, false);

        if (string.IsNullOrWhiteSpace(configuredGroup))
            return new HistoryGroupScope(string.Empty, false);

        if (availableGroupNames is null)
            return new HistoryGroupScope(null, false);

        var name = configuredGroup.Trim();
        return availableGroupNames.Contains(name, StringComparer.Ordinal)
            ? new HistoryGroupScope(name, false)
            : new HistoryGroupScope(string.Empty, true);
    }
}
