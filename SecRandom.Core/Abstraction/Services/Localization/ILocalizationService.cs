namespace SecRandom.Core.Abstraction.Services.Localization;

/// <summary>
///     本地化：插件可以读宿主的界面语言，并注册自己的词条，跟随语言切换。
///     <para>
///         插件自己的词条用 <see cref="RegisterStrings" /> 注册（每个语言一份），
///         之后用 <see cref="GetForPlugin" /> 取；宿主内置词条用 <see cref="Get" /> 取。
///     </para>
/// </summary>
public interface ILocalizationService
{
    /// <summary>当前语言（如 <c>zh-CN</c> / <c>en-US</c>）。</summary>
    string CurrentCulture { get; }

    /// <summary>宿主内置的全部语言。</summary>
    IReadOnlyList<string> AvailableCultures { get; }

    /// <summary>语言切换。插件应在此重刷自己的文案。</summary>
    event EventHandler? Changed;

    /// <summary>取宿主内置词条；没有时返回 <paramref name="fallback" />。</summary>
    string Get(string key, string fallback = "");

    /// <summary>取宿主内置词条并格式化（<c>{0}</c> 占位）。</summary>
    string Format(string key, string fallback, params object?[] args);

    /// <summary>取插件自己注册的词条；没有时返回 <paramref name="fallback" />。</summary>
    string GetForPlugin(string pluginId, string key, string fallback = "");

    /// <summary>
    ///     注册插件词条。键建议用 <c>"&lt;culture&gt;:&lt;key&gt;"</c> 形式（如 <c>en-US:MyPlugin.Title</c>），
    ///     也可以只给 <c>"&lt;key&gt;"</c> 表示对所有语言生效（可被具体语言覆盖）。
    /// </summary>
    void RegisterStrings(string pluginId, IReadOnlyDictionary<string, string> strings);
}
