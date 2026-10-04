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
