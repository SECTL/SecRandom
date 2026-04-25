using System.Collections.ObjectModel;

namespace SecRandom.Core.Models.Profile;

public class StudentList
{
    public ObservableCollection<Student> Students { get; set; } = [];
}