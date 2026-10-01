using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using SecRandom.Shared.Abstraction;
using SecRandom.Shared.Interfaces;

namespace SecRandom.Shared.Models.Profile;

public class StudentList : ProfileConfigBase, IAttachableSettingsObject
{
    public StudentList()
    {
    }

    public StudentList(string name)
    {
        Name = name;
    }

    [JsonIgnore] public sealed override string Name { get; set; } = string.Empty;

    [JsonIgnore]
    public override string ConfigFilePath =>
        GetProfileFilePath("list", "roll_call_list");

    public ObservableCollection<Student> Students { get; set; } = [];

    public Dictionary<Guid, object?> AttachedObjects { get; set; } = [];
}