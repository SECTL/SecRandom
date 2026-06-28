using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Shared.Converters;

namespace SecRandom.Shared.Models.Profile;

public partial class Prize : AttachableSettingsObject
{
    [ObservableProperty] private int _count = 1;
    [ObservableProperty] private bool _exists = true;
    [ObservableProperty] private string _id = string.Empty;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty]
    [property: JsonConverter(typeof(LenientGuidJsonConverter))]
    private Guid _recordId;
    [ObservableProperty] private string _tags = string.Empty;
    [ObservableProperty] private double _weight = 1;
}
