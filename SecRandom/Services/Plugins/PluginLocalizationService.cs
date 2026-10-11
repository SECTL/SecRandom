using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Threading;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction.Services.Localization;

namespace SecRandom.Services.Plugins;

/// <summary>
///     <see cref="ILocalizationService" /> 的宿主实现。
///     <para>
///         宿主没有统一的“按 key 查词条”入口：本地化是每个页面一套 <c>Resources.resx</c> 生成的强类型类，
///         内部用 <see cref="ResourceManager" /> + <see cref="CultureInfo.CurrentUICulture" /> 取词条。
///         因此这里从内置 Langs 资源集（<c>SecRandom.Langs.**.Resources</c>）反射出全部 <see cref="ResourceManager" />
///         实例，按 key 依次查找（先 <c>Common</c>，其余按名字排序），命中即返回，全部未命中返回 fallback。
///         这样不需要改宿主其它文件；限制是“同名 key 以排序靠前的资源集为准”，且不做反向 key 校验。
///     </para>
///     <para>
///         <see cref="CurrentCulture" /> 读 <see cref="CultureInfo.CurrentUICulture" />（宿主
///         <c>App.InitializeLanguages</c> 切换语言时就是改这个）；<see cref="AvailableCultures" /> 列出宿主内置的三种语言。
///         插件自有词条由本类自己维护，键支持 <c>"&lt;culture&gt;:&lt;key&gt;"</c> 与 <c>"&lt;key&gt;"</c>（具体语言优先）。
///     </para>
///     <para>
///         宿主没有语言切换事件（<c>App.InitializeLanguages</c> 是静态方法，调用点分散在启动流程与设置页），
///         所以 <see cref="Changed" /> 的触发入口做成公开方法 <see cref="NotifyCultureChanged" />，由宿主接线方在语言切换后调用。
///     </para>
/// </summary>
public sealed class PluginLocalizationService : ILocalizationService
{
    /// <summary>宿主中文资源是中性 resx，界面上对应的文化名是 <c>zh-Hans</c>（见 <c>App.axaml.cs</c> 语言映射）。</summary>
    private const string DefaultCultureName = "zh-Hans";

    /// <summary>优先查找的词条资源集（宿主通用词条）。</summary>
    private const string CommonResourceName = "SecRandom.Langs.Common.Resources";

    /// <summary>宿主内置语言。</summary>
    private static readonly string[] HostCultures = ["zh-Hans", "en-US", "ja-JP"];

    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _pluginStrings =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ILogger<PluginLocalizationService>? _logger;

    private readonly Lazy<IReadOnlyList<ResourceManager>> _hostResources;

    /// <summary>
    ///     初始化 <see cref="PluginLocalizationService" />。
    /// </summary>
    /// <param name="logger">日志服务；宿主已 <c>AddLogging</c>，因此 DI 可直接解析，省略时为 null。</param>
    public PluginLocalizationService(ILogger<PluginLocalizationService>? logger = null)
    {
        _logger = logger;
        _hostResources = new Lazy<IReadOnlyList<ResourceManager>>(CreateHostResourceManagers, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <summary>宿主当前界面语言；宿主只会设成内置三种之一，中文统一归一化成 <c>zh-Hans</c>。</summary>
    public string CurrentCulture
    {
        get
        {
            var name = CultureInfo.CurrentUICulture.Name;
            if (string.IsNullOrEmpty(name))
                return DefaultCultureName;

            if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                return DefaultCultureName;

            return name;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> AvailableCultures => HostCultures;

    /// <summary>
    ///     可选的宿主词条解析器；替换后优先于内置 Langs 资源集查找。
    ///     宿主以后若有统一的词条入口，接线方可以直接把它接在这里。
    /// </summary>
    public Func<string, string?>? StringResolver { get; set; }

    /// <inheritdoc />
    public string Get(string key, string fallback = "")
    {
        if (string.IsNullOrWhiteSpace(key))
            return fallback;

        var value = ResolveHostString(key);
        return string.IsNullOrEmpty(value) ? fallback : value;
    }

    /// <inheritdoc />
    public string Format(string key, string fallback, params object?[] args)
    {
        var template = Get(key, fallback);
        if (args is null || args.Length == 0)
            return template;

        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, args);
        }
        catch (FormatException)
        {
            // 占位符与参数不匹配：按契约返回 fallback。
            return fallback;
        }
        catch (ArgumentNullException)
        {
            return fallback;
        }
    }

    /// <inheritdoc />
    public string GetForPlugin(string pluginId, string key, string fallback = "")
    {
        if (string.IsNullOrWhiteSpace(pluginId) || string.IsNullOrWhiteSpace(key))
            return fallback;

        if (!_pluginStrings.TryGetValue(pluginId, out var strings))
            return fallback;

        foreach (var culture in EnumerateCultureCandidates(CurrentCulture))
        {
            if (strings.TryGetValue($"{culture}:{key}", out var localized) && !string.IsNullOrEmpty(localized))
                return localized;
        }

        if (strings.TryGetValue(key, out var wildcard) && !string.IsNullOrEmpty(wildcard))
            return wildcard;

        return fallback;
    }

    /// <inheritdoc />
    public void RegisterStrings(string pluginId, IReadOnlyDictionary<string, string> strings)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
            throw new ArgumentException("插件 id 不能为空。", nameof(pluginId));

        ArgumentNullException.ThrowIfNull(strings);

        var target = _pluginStrings.GetOrAdd(
            pluginId,
            static _ => new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        foreach (var pair in strings)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
                continue;

            target[pair.Key.Trim()] = pair.Value ?? string.Empty;
        }
    }

    /// <summary>移除某个插件的全部自有词条（插件卸载时由接线方调用，不属于契约接口）。</summary>
    /// <param name="pluginId">插件 id。</param>
    public void UnregisterStrings(string pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
            return;

        _pluginStrings.TryRemove(pluginId, out _);
    }

    /// <summary>
    ///     触发 <see cref="Changed" />：宿主语言切换后由接线方调用（宿主没有语言切换事件，见类注释）。
    /// </summary>
    public void NotifyCultureChanged()
    {
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "通知插件语言变更失败。");
        }
    }

    /// <summary>先问注入的解析器，再用内置 Langs 资源集兜底。</summary>
    private string? ResolveHostString(string key)
    {
        if (StringResolver is { } resolver)
        {
            try
            {
                var resolved = resolver(key);
                if (!string.IsNullOrEmpty(resolved))
                    return resolved;
            }
            catch (Exception exception)
            {
                _logger?.LogWarning(exception, "自定义本地化解析器处理 key {Key} 失败。", key);
            }
        }

        var culture = CultureInfo.CurrentUICulture;

        foreach (var manager in _hostResources.Value)
        {
            try
            {
                var value = manager.GetString(key, culture);
                if (!string.IsNullOrEmpty(value))
                    return value;
            }
            catch (MissingManifestResourceException exception)
            {
                _logger?.LogDebug(exception, "Langs 资源集缺少清单，已跳过。");
            }
        }

        return null;
    }

    /// <summary>枚举宿主内置 Langs 资源集对应的 <see cref="ResourceManager" />，<c>Common</c> 优先。</summary>
    private static IReadOnlyList<ResourceManager> CreateHostResourceManagers()
    {
        var assembly = typeof(SecRandom.Langs.Common.Resources).Assembly;

        var names = assembly.GetManifestResourceNames()
            .Where(static name => name.StartsWith("SecRandom.Langs.", StringComparison.Ordinal)
                                  && name.EndsWith(".resources", StringComparison.Ordinal))
            .Select(static name => name[..^".resources".Length])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static name => string.Equals(name, CommonResourceName, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(static name => name, StringComparer.Ordinal)
            .ToList();

        var managers = new List<ResourceManager>(names.Count);
        foreach (var name in names)
        {
            try
            {
                managers.Add(new ResourceManager(name, assembly));
            }
            catch (Exception)
            {
                // 个别资源集没有可用清单时跳过，不影响其它资源集。
            }
        }

        return managers;
    }

    /// <summary>语言候选：具体文化名 → 主语言 → 中文别名（zh-Hans / zh-CN 互通）。</summary>
    private static IEnumerable<string> EnumerateCultureCandidates(string culture)
    {
        yield return culture;

        var separator = culture.IndexOf('-');
        if (separator > 0)
            yield return culture[..separator];

        if (string.Equals(culture, "zh-Hans", StringComparison.OrdinalIgnoreCase))
            yield return "zh-CN";
        else if (string.Equals(culture, "zh-CN", StringComparison.OrdinalIgnoreCase))
            yield return "zh-Hans";
    }
}
