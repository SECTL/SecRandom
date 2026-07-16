using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Shared.Extensions;
using SecRandom.Shared.Interfaces;

namespace SecRandom.Core.Models.AttachedSettings;

public partial class DrawMusicAttachedSettings : ObservableRecipient, IAttachedSettings
{
    [ObservableProperty] private bool _isAttachSettingsEnabled;
    [ObservableProperty] private string _animationMusic = "$none";
    [ObservableProperty] private string _resultMusic = "$none";
}

public static class DrawMusicAttachedSettingsResolver
{
    private static readonly Guid SettingsId = Guid.Parse(GlobalConstants.DrawMusicAttachedSettings);

    public static string GetAnimationMusic(IAttachableSettingsObject? target, string fallback)
    {
        var selection = GetSettings(target)?.AnimationMusic;
        return string.IsNullOrWhiteSpace(selection) ? fallback : selection;
    }

    public static string GetResultMusic(IAttachableSettingsObject? target, string fallback)
    {
        var selection = GetSettings(target)?.ResultMusic;
        return string.IsNullOrWhiteSpace(selection) ? fallback : selection;
    }

    private static DrawMusicAttachedSettings? GetSettings(IAttachableSettingsObject? target)
    {
        var settings = target?.GetAttachedObject<DrawMusicAttachedSettings>(SettingsId);
        return settings is { IsAttachSettingsEnabled: true } ? settings : null;
    }
}
