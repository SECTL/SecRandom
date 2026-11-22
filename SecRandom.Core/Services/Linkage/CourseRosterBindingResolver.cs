using System;
using System.Collections.Generic;
using System.Linq;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.Linkage;

namespace SecRandom.Core.Services.Linkage;

public enum CourseDrawChannel
{
    RollCall,
    QuickDraw,
    Lottery
}

public static class CourseRosterBindingResolver
{
    public static string? Resolve(
        IEnumerable<CourseRosterBinding>? bindings,
        LinkageDataSource dataSource,
        string? courseName,
        CourseDrawChannel channel)
    {
        if (string.IsNullOrWhiteSpace(courseName) || bindings is null)
            return null;

        var binding = bindings.FirstOrDefault(item =>
            item.DataSource == dataSource
            && string.Equals(item.CourseName, courseName, StringComparison.Ordinal));
        if (binding is null)
            return null;

        var listName = channel switch
        {
            CourseDrawChannel.RollCall => binding.RollCallStudentListName,
            CourseDrawChannel.QuickDraw => binding.QuickDrawStudentListName,
            CourseDrawChannel.Lottery => binding.LotteryPrizeListName,
            _ => throw new ArgumentOutOfRangeException(nameof(channel))
        };
        return string.IsNullOrWhiteSpace(listName) ? null : listName;
    }
}
