namespace SecRandom.Core.Services.SingleInstance;

/// <summary>
///     单实例 IPC 命令常量
/// </summary>
public static class SingleInstanceCommand
{
    /// <summary>通知第一实例显示主界面</summary>
    public const string ShowMainWindow = "ShowMainWindow";

    /// <summary>通知第一实例重启</summary>
    public const string Restart = "Restart";

    /// <summary>通知第一实例处理受控的 secrandom:// 协议激活。</summary>
    public const string UrlPrefix = "Url:";
}
