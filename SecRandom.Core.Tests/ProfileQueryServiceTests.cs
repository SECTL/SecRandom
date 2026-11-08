using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Services;
using SecRandom.Services.Profiles;

namespace SecRandom.Core.Tests;

public class ProfileQueryServiceTests
{
    [Fact]
    public void MissingProfile_IsNotCreatedByReadOnlyQuery()
    {
        var name = $"protocol-query-{Guid.NewGuid():N}";
        var path = Path.Combine(AppContext.BaseDirectory, "data", "list", "roll_call_list", $"{name}.json");
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddCoreRuntimeServices();
        using var provider = services.BuildServiceProvider();
        var service = new ProfileQueryService(provider.GetRequiredService<IHistoryQueryService>());

        Assert.False(File.Exists(path));
        Assert.Null(service.LoadStudentList(name));
        Assert.False(File.Exists(path));
    }
}
