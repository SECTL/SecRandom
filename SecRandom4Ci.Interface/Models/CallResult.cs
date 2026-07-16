using CommunityToolkit.Mvvm.ComponentModel;

namespace SecRandom4Ci.Interface.Models;

public partial class CallResult : ObservableRecipient
{
    [ObservableProperty] private string _className = string.Empty;
    [ObservableProperty] private List<Student> _selectedStudents = [];
    [ObservableProperty] private int _drawCount = 1;
    [ObservableProperty] private double _displayDuration = 5.0;
}
