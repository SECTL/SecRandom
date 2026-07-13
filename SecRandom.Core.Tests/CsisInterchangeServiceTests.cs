using SecRandom.Services.Seating;
using SecRandom.Shared;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Tests;

public sealed class CsisInterchangeServiceTests
{
    private readonly CsisInterchangeService _service = new();

    [Fact]
    public void ReadStudentLists_MapsClassesAndStudents()
    {
        const string json = """
        {
          "version": 1,
          "classes": [{
            "name": "七年级一班", "class": 1, "grade": 7,
            "students": [{ "id": 1, "number": 1001, "name": "小明", "gender": "male", "group": "A", "tags": ["班长", "课代表:数学"] }]
          }]
        }
        """;

        var result = _service.ReadStudentLists(json);

        var item = Assert.Single(result.Classes);
        var student = Assert.Single(item.Students);
        Assert.Equal("七年级一班", item.Name);
        Assert.Equal("1", student.Id);
        Assert.Equal("小明", student.Name);
        Assert.Equal("班长 课代表:数学", student.Tags);
        Assert.NotEqual(Guid.Empty, student.RecordId);
    }

    [Fact]
    public void ReadSeatingChart_MapsCoordinatesToExistingStudents()
    {
        var student = new Student { Id = "1", Name = "小明" };
        const string json = """
        {
          "version": 1,
          "rotation": { "enabled": false },
          "deskmate": false,
          "students": [{ "id": 1, "name": "小明", "position": [2, 3] }]
        }
        """;

        var result = _service.ReadSeatingChart(json, [student]);

        var seat = Assert.Single(result.Chart.Seats);
        Assert.Empty(result.Issues);
        Assert.Equal(3, seat.Row);
        Assert.Equal(2, seat.Column);
        Assert.Equal(student.RecordId.ToString(), seat.StudentRecordId);
        Assert.Equal(4, result.Chart.Rows);
        Assert.Equal(3, result.Chart.Columns);
    }

    [Fact]
    public void ReadSeatingChart_RejectsExtendedPlacement()
    {
        const string json = """
        {
          "version": 1,
          "rotation": { "enabled": false },
          "deskmate": false,
          "students": [{ "id": 1, "name": "小明", "position": [0, 0], "ruleset": [] }]
        }
        """;

        var exception = Assert.Throws<InvalidDataException>(() => _service.ReadSeatingChart(json, []));

        Assert.Contains("ESPS", exception.Message);
    }

    [Fact]
    public void WriteSeatingChart_RejectsStudentWithoutNumericId()
    {
        var student = new Student { Id = "", Name = "小明" };
        ProfileRecordIdentity.EnsureRecordId(student);
        var chart = new SeatingChart
        {
            Seats = [new SeatingChartSeat { Row = 0, Column = 0, StudentRecordId = student.RecordId.ToString() }]
        };

        Assert.Throws<InvalidDataException>(() => _service.WriteSeatingChart(chart, [student]));
    }
}
