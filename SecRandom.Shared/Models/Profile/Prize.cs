using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Shared.Converters;

namespace SecRandom.Shared.Models.Profile;

public partial class Prize : AttachableSettingsObject
{
    [ObservableProperty] private int _count = 1;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCandidate))]
    private bool _exists = true;
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
    [ObservableProperty] private double _weight = 1;

    [JsonIgnore]
    public bool IsCandidate => Exists &&
                               (!string.IsNullOrWhiteSpace(Id) || !string.IsNullOrWhiteSpace(Name));
}
