using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Services.ControlNode;

namespace SecRandom.Services.ControlNode;

/// <summary>
///     集控期望状态（<c>draw_locked</c>）在本地抽取路径上的闸门。
/// </summary>
/// <remarks>
///     只有本机允许被集控时才生效：老师关掉本机开关就等于收回远控权，
///     之前下发的锁定不应继续影响本机。
/// </remarks>
public sealed class ControlDrawGateService(IControlNodeStateStore stateStore) : IControlDrawGate
{
    public bool IsDrawLocked
    {
        get
        {
            var state = stateStore.Current;
            return state.RemoteControlEnabled && state.DrawLocked;
        }
    }
}

/// <summary>节点上报的即时状态：当前班级取当前点名学生名单名。</summary>
public sealed class ControlNodeStatusSource(IProfileService profileService) : IControlNodeStatusSource
{
    public string? CurrentClass => profileService.StudentListConfig?.Name;
}
