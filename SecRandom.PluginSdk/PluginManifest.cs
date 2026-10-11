namespace SecRandom.PluginSdk;

public sealed class PluginManifest
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = string.Empty;
    public string EntranceAssembly { get; set; } = string.Empty;
    public string Icon { get; set; } = "icon.png";
    public string Readme { get; set; } = "README.md";
    public string? Url { get; set; }
    public string Author { get; set; } = string.Empty;
    public List<PluginDependency> Dependencies { get; set; } = [];
    public List<string> SupportedPlatforms { get; set; } = [];

    /// <summary>
    ///     插件声明的权限（<c>manifest.yml</c> 的 <c>permissions</c>），例如 <c>[UserInterface, Storage]</c>。
    ///     <para>
    ///         这是**声明式策略**、不是安全边界：插件与宿主同进程、FullTrust 运行。用途是让插件自我约束、
    ///         让安装时看得见、让宿主能提示。<b>不写这个字段 = 未声明 = 一律允许</b>（否则所有存量插件都会被拒）；
    ///         想显式"什么都不用"就写 <c>[None]</c>。
    ///     </para>
    /// </summary>
    public List<string> Permissions { get; set; } = [];
}
