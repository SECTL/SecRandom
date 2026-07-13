using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Shared.Converters;

namespace SecRandom.Shared.Models.Profile;

public partial class Student : AttachableSettingsObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCandidate))]
    private bool _exists = true;
    [ObservableProperty] private string _gender = string.Empty;
    [ObservableProperty] private string _group = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCandidate))]
    private string _id = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCandidate))]
    private string _name = string.Empty;
    [ObservableProperty]
    [property: JsonConverter(typeof(LenientGuidJsonConverter))]
    private Guid _recordId;
    [ObservableProperty] private string _tags = string.Empty;

    [JsonIgnore]
    public bool IsCandidate => Exists &&
                               (!string.IsNullOrWhiteSpace(Id) || !string.IsNullOrWhiteSpace(Name));
}
