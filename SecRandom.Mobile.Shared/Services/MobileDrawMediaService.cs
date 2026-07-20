using SecRandom.Core;
using SecRandom.Core.Models.AttachedSettings;
using SecRandom.Core.Models.SubConfigs.Picking;
using SecRandom.Shared.Extensions;
using SecRandom.Shared.Interfaces;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Mobile.Services;

/// <summary>
/// Applies record-level music and voice settings around a completed mobile draw.
/// Media failures are deliberately non-fatal because the draw commit is already authoritative.
/// </summary>
public sealed class MobileDrawMediaService(
    IMobileMediaPlayer player,
    MobileMediaLibraryService mediaLibrary)
{
    private static readonly Guid DrawMusicSettingsId = Guid.Parse(GlobalConstants.DrawMusicAttachedSettings);
    private static readonly Guid SpecificVoiceSettingsId = Guid.Parse(GlobalConstants.SpecificAnnouncementAttachedSettings);

    internal bool IsSupported => player.IsSupported;

    internal Task StartAnimationAsync(
        IAttachableSettingsObject? record,
        DrawSettingsConfigBase settings,
        CancellationToken cancellationToken = default) =>
        PlayAsync(ResolveAnimationMusic(record, settings.AnimationMusic), settings.AnimationMusicVolume,
            settings.AnimationMusicLoop, cancellationToken);

    internal async Task PlayResultAsync(
        IAttachableSettingsObject? record,
        DrawSettingsConfigBase settings,
        CancellationToken cancellationToken = default)
    {
        await player.StopAsync().ConfigureAwait(false);
        await PlayAsync(ResolveResultMusic(record, settings.ResultMusic), settings.ResultMusicVolume, false, cancellationToken)
            .ConfigureAwait(false);
    }

    internal Task StopAsync() => player.StopAsync();

    internal async Task PreviewAsync(string selection, CancellationToken cancellationToken = default)
    {
        await player.StopAsync().ConfigureAwait(false);
        await PlayAsync(selection, 100, false, cancellationToken).ConfigureAwait(false);
    }

    internal Task SpeakStudentsAsync(
        IEnumerable<Student> students,
        bool announceId,
        bool announceName,
        int volume,
        int rate,
        CancellationToken cancellationToken = default) =>
        SpeakAsync(students.Select(student => BuildAnnouncement(student, announceId, announceName)), volume, rate, cancellationToken);

    internal Task SpeakPrizesAsync(
        IEnumerable<Prize> prizes,
        bool announceId,
        bool announceName,
        int volume,
        int rate,
        CancellationToken cancellationToken = default) =>
        SpeakAsync(prizes.Select(prize => BuildAnnouncement(prize, announceId, announceName)), volume, rate, cancellationToken);

    private async Task PlayAsync(string selection, int volume, bool loop, CancellationToken cancellationToken)
    {
        if (!player.IsSupported)
            return;

        var path = selection == MobileMediaLibraryService.RandomMusicTrackId
            ? mediaLibrary.ResolveRandomMusicPath()
            : mediaLibrary.ResolveMusicPath(selection);
        if (path is not null)
            await player.PlayAsync(path, volume, loop, cancellationToken).ConfigureAwait(false);
    }

    private async Task SpeakAsync(IEnumerable<string> parts, int volume, int rate, CancellationToken cancellationToken)
    {
        if (!player.IsSupported)
            return;

        var text = string.Join("，", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        if (!string.IsNullOrWhiteSpace(text))
            await player.SpeakAsync(text, volume, rate, cancellationToken).ConfigureAwait(false);
    }

    private static string ResolveAnimationMusic(IAttachableSettingsObject? record, string fallback) =>
        record?.GetAttachedObject<DrawMusicAttachedSettings>(DrawMusicSettingsId) is { IsAttachSettingsEnabled: true } settings &&
        !string.IsNullOrWhiteSpace(settings.AnimationMusic)
            ? settings.AnimationMusic
            : fallback;

    private static string ResolveResultMusic(IAttachableSettingsObject? record, string fallback) =>
        record?.GetAttachedObject<DrawMusicAttachedSettings>(DrawMusicSettingsId) is { IsAttachSettingsEnabled: true } settings &&
        !string.IsNullOrWhiteSpace(settings.ResultMusic)
            ? settings.ResultMusic
            : fallback;

    private static string BuildAnnouncement(Student student, bool announceId, bool announceName) =>
        BuildAnnouncement(student, student.Id, student.Name, announceId, announceName);

    private static string BuildAnnouncement(Prize prize, bool announceId, bool announceName) =>
        BuildAnnouncement(prize, prize.Id, prize.Name, announceId, announceName);

    private static string BuildAnnouncement(
        IAttachableSettingsObject record,
        string id,
        string name,
        bool announceId,
        bool announceName)
    {
        var specific = record.GetAttachedObject<SpecificAnnouncementAttachedSettings>(SpecificVoiceSettingsId);
        var enabled = specific?.IsAttachSettingsEnabled == true;
        var parts = new List<string>();
        AddIfNotBlank(parts, enabled ? specific?.Prefix : null);
        if (announceId)
            AddIfNotBlank(parts, id);
        if (announceName)
            AddIfNotBlank(parts, enabled && !string.IsNullOrWhiteSpace(specific?.TtsAlias) ? specific.TtsAlias : name);
        AddIfNotBlank(parts, enabled ? specific?.Suffix : null);
        return parts.Count == 0 ? name.Trim() : string.Join(" ", parts);
    }

    private static void AddIfNotBlank(ICollection<string> values, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            values.Add(value.Trim());
    }
}
