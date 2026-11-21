using SecRandom.Core.Enums.Configs;

namespace SecRandom.Core.Models.SubConfigs.Picking;

/// <summary>
///     能被切进 / 切出编排抽取的通道抽取配置：点名、闪抽、抽奖三份配置各自声明
///     <see cref="DrawMode" />，共用同一个「进入编排前先记住重复策略」的语义
///     （<c>DrawModeSelection.SelectOrchestrated</c> / <c>DrawModeSelection.LeaveOrchestrated</c>）。
/// </summary>
/// <remarks>
///     <see cref="DrawMode" /> 故意留在各通道配置上而不是提到共享基类：默认抽取设置没有抽取方式，
///     不该被这个接口卷进来。
/// </remarks>
public interface IOrchestratedDrawSettings
{
    /// <summary>抽取模式（重复策略）；值为 <see cref="DrawMode.Orchestrated" /> 时该通道走编排名单。</summary>
    DrawMode DrawMode { get; set; }

    /// <summary>进入编排抽取之前生效的重复策略，改回随机 / 公平抽取时恢复。</summary>
    DrawMode SampledDrawMode { get; set; }
}