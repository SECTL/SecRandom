using System.Text.Json.Serialization;

namespace SecRandom.Core.Models.Linkage;

/// <summary>Associates a course name with the rosters used by the draw channels.</summary>
public sealed class CourseRosterBinding
{
    public SecRandom.Core.Enums.Configs.LinkageDataSource DataSource { get; set; }
    public string CourseName { get; set; } = string.Empty;
    public bool IsCustomCourse { get; set; }
    public string RollCallStudentListName { get; set; } = string.Empty;
    public string QuickDrawStudentListName { get; set; } = string.Empty;
    public string LotteryPrizeListName { get; set; } = string.Empty;

    [JsonIgnore]
    public bool IsConfigured => !string.IsNullOrWhiteSpace(CourseName);
}
