namespace SecRandom.Core.Enums.Configs;

/// <summary>
///     设置文件防篡改校验在启动时发现改动后的处理方式。
/// </summary>
public enum SettingsIntegrityAction
{
    /// <summary>要求用安全密码确认当前设置文件之后才能继续启动，这是原有行为。</summary>
    Confirm,

    /// <summary>自动用最近一次备份中的设置文件覆盖当前文件，然后重启应用。</summary>
    AutoRestore
}
