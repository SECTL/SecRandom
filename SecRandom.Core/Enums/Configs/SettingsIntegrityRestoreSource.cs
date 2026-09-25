namespace SecRandom.Core.Enums.Configs;

/// <summary>
///     自动恢复设置文件时依次尝试的备份来源。
/// </summary>
public enum SettingsIntegrityRestoreSource
{
    /// <summary>只使用本机 data/backup 中的备份。</summary>
    Local,

    /// <summary>先本地备份，本地没有包含设置文件的可用备份时再使用云端备份。</summary>
    LocalThenCloud,

    /// <summary>先云端备份，未登录或云端不可用时再使用本地备份。</summary>
    CloudThenLocal
}
