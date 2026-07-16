using CommunityToolkit.Mvvm.ComponentModel;

namespace SecRandom4Ci.Interface.Models;

public partial class Student : ObservableRecipient
{
    [ObservableProperty] private int _studentId;
    [ObservableProperty] private string _studentName = string.Empty;
    [ObservableProperty] private bool _exists = true;
}
