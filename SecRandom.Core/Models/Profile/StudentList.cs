using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using SecRandom.Core.Abstraction;

namespace SecRandom.Core.Models.Profile;

public class StudentList(string name) : ConfigBase
{
    [JsonIgnore] public override string ConfigFilePath => Utils.GetFilePath("data", "list", "roll_call_list", name);
    
    public ObservableCollection<Student> Students { get; set; } = [];
}