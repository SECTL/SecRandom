using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using SecRandom.Shared.Abstraction;

namespace SecRandom.Shared.Models.Profile;

public class StudentList : ProfileConfigBase
{
    [JsonIgnore] public sealed override string Name { get; set; } = string.Empty;
    [JsonIgnore] public override string ConfigFilePath =>
        Utils.GetFilePath("data", "list", "roll_call_list", $"{Name}.json");
    
    public ObservableCollection<Student> Students { get; set; } = [];
    
    public StudentList() { }
    
    public StudentList(string name)
    {
        Name = name;
    }
}