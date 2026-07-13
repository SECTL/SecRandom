using System.Text.Json.Serialization;
using SecRandom.Shared.Abstraction;

namespace SecRandom.Shared.Models.Profile;

public sealed class SeatingChartCollection : ProfileConfigBase
{
    public SeatingChartCollection()
    {
    }

    public SeatingChartCollection(string name)
    {
        Name = name;
    }

    [JsonIgnore] public sealed override string Name { get; set; } = string.Empty;

    [JsonIgnore]
    public override string ConfigFilePath => Utils.GetFilePath("list", "seating_charts", $"{Name}.json");

    public List<SeatingChart> Charts { get; set; } = [];
}

public sealed class SeatingChart
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public int Rows { get; set; } = 6;
    public int Columns { get; set; } = 8;
    public bool ShowTeacherDesk { get; set; } = true;
    public bool IsDeskmateLayout { get; set; }
    public SeatingChartRotation Rotation { get; set; } = new();
    public List<SeatingChartSeat> Seats { get; set; } = [];
}

public sealed class SeatingChartRotation
{
    public bool Enabled { get; set; }
    public bool ToLeft { get; set; }
    public int CycleDays { get; set; }
    public bool CycleInColumns { get; set; }
}

public sealed class SeatingChartSeat
{
    public int Row { get; set; }
    public int Column { get; set; }
    public bool IsDisabled { get; set; }
    public string? StudentRecordId { get; set; }
    public string? DeskmatePosition { get; set; }
}
