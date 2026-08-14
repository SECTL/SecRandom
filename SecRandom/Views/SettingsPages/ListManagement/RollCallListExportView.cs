using SecRandom.Services.RosterTransfer;
using SecRandom.Shared.Models.Profile;
using SecRandom.Langs.SettingsPages.ListManagement.RosterTransfer;
using LR = SecRandom.Langs.SettingsPages.ListManagement.RollCallList.Resources;

namespace SecRandom.Views.SettingsPages.ListManagement;

public sealed class RollCallListExportView : RosterListExportView
{
    public RollCallListExportView(string listName, IReadOnlyList<Student> students)
        : base(listName,
            new RosterTransferDocument(1, RosterTransferKind.Students, $"{listName}.secrandom-roster.json", students.Select(student =>
                new RosterTransferRow(student.Exists, student.Id, student.Name, student.Gender, student.Group, student.Tags)).ToArray()),
            students.Select(student => new Dictionary<string, object?>
            {
                [GetField("C_StudentId")] = student.Id,
                [GetField("C_Name")] = student.Name,
                [GetField("C_Gender")] = student.Gender,
                [GetField("C_Group")] = student.Group,
                [GetField("C_Tags")] = student.Tags,
                [GetField("C_Exists")] = student.Exists
            }).ToArray(),
            RosterTransferText.Get)
    {
    }

    private static string GetField(string name) => LR.ResourceManager.GetString(name, LR.Culture) ?? name;
}
