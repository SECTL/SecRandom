using System.IO;
using SecRandom.Services.Profiles;

namespace SecRandom.Core.Tests;

public class ProfileQueryServiceTests
{
    [Fact]
    public void MissingProfile_IsNotCreatedByReadOnlyQuery()
    {
        var name = $"protocol-query-{Guid.NewGuid():N}";
        var path = Path.Combine(AppContext.BaseDirectory, "data", "list", "roll_call_list", $"{name}.json");
        var service = new ProfileQueryService();

        Assert.False(File.Exists(path));
        Assert.Null(service.LoadStudentList(name));
        Assert.False(File.Exists(path));
    }
}
