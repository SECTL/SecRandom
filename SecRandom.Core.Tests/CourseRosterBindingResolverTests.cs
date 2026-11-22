using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.Linkage;
using SecRandom.Core.Services.Linkage;

namespace SecRandom.Core.Tests;

public sealed class CourseRosterBindingResolverTests
{
    [Theory]
    [InlineData(CourseDrawChannel.RollCall, "roll-call")]
    [InlineData(CourseDrawChannel.QuickDraw, "quick-draw")]
    [InlineData(CourseDrawChannel.Lottery, "lottery")]
    public void Resolve_ReturnsTheChannelSpecificList(CourseDrawChannel channel, string expected)
    {
        var binding = new CourseRosterBinding
        {
            DataSource = LinkageDataSource.ClassIsland,
            CourseName = "Math",
            RollCallStudentListName = "roll-call",
            QuickDrawStudentListName = "quick-draw",
            LotteryPrizeListName = "lottery"
        };

        var result = CourseRosterBindingResolver.Resolve(
            [binding], LinkageDataSource.ClassIsland, "Math", channel);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Resolve_ReturnsNullForDifferentSourceCourseOrEmptyMapping()
    {
        var binding = new CourseRosterBinding
        {
            DataSource = LinkageDataSource.Cses,
            CourseName = "Math",
            RollCallStudentListName = "class-a"
        };

        Assert.Null(CourseRosterBindingResolver.Resolve([binding], LinkageDataSource.ClassIsland, "Math", CourseDrawChannel.RollCall));
        Assert.Null(CourseRosterBindingResolver.Resolve([binding], LinkageDataSource.Cses, "English", CourseDrawChannel.RollCall));
        Assert.Null(CourseRosterBindingResolver.Resolve([binding], LinkageDataSource.Cses, "Math", CourseDrawChannel.QuickDraw));
    }
}
