using dotnetCampus.Ipc.CompilerServices.Attributes;
using SecRandom4Ci.Interface.Models;

namespace SecRandom4Ci.Interface.Services;

[IpcPublic(IgnoresIpcException = true)]
public interface ISecRandomService
{
    void NotifyResult(CallResult result);

    // Delivery failures must reach SecRandom so its configured built-in fallback can run.
    [IpcMethod(WaitsVoid = true, IgnoresIpcException = false)]
    void ShowNotification(NotificationData data);

    Version GetPluginVersion();
    string IsAlive();
}
