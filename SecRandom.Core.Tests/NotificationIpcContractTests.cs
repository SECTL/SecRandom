using System.Reflection;
using dotnetCampus.Ipc.CompilerServices.Attributes;
using SecRandom4Ci.Interface.Enums;
using SecRandom4Ci.Interface.Models;
using SecRandom4Ci.Interface.Services;

namespace SecRandom.Core.Tests;

public class NotificationIpcContractTests
{
    [Fact]
    public void NotificationData_ExposesAnimationWithoutGroupFields()
    {
        var data = new NotificationData();

        Assert.True(data.Animation);
        Assert.Null(typeof(NotificationItem).GetProperty("HasGroup"));
        Assert.Null(typeof(NotificationItem).GetProperty("GroupName"));
    }

    [Fact]
    public void ResultType_ContainsPartialValuesForDrawAnimations()
    {
        Assert.True(Enum.IsDefined(ResultType.PartialRollCall));
        Assert.True(Enum.IsDefined(ResultType.PartialQuickDraw));
        Assert.True(Enum.IsDefined(ResultType.PartialLottery));
    }

    [Fact]
    public void ShowNotification_WaitsForRemoteDeliveryAndDoesNotIgnoreIpcFailures()
    {
        IpcMethodAttribute? attribute = typeof(ISecRandomService)
            .GetMethod(nameof(ISecRandomService.ShowNotification), [typeof(NotificationData)])
            ?.GetCustomAttribute<IpcMethodAttribute>();

        Assert.NotNull(attribute);
        Assert.True(attribute.WaitsVoid);
        Assert.False(attribute.IgnoresIpcException);
    }
}
