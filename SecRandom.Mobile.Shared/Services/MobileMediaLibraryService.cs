using Avalonia.Platform.Storage;
using SecRandom.Core;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Models.AttachedSettings;
using SecRandom.Core.Services.Config;
using SecRandom.Shared;
using SecRandom.Shared.Extensions;

namespace SecRandom.Mobile.Services;

/// <summary>
/// Owns mobile-private music files and removes dangling track references before a
/// managed file disappears. The platform player only receives resolved local paths.
/// </summary>
public sealed class MobileMediaLibraryService(
    IProfileCatalogManager catalogManager,
    MainConfigHandler configHandler)
{
    internal const string NoMusicTrackId = "$none";
    internal const string RandomMusicTrackId = "$random";

    private static readonly Guid DrawMusicSettingsId = Guid.Parse(GlobalConstants.DrawMusicAttachedSettings);
    private static readonly HashSet<string> SupportedMusicExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".flac"
    };
    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"
    };

    internal IReadOnlyList<MobileMediaTrack> GetTracks()
    {
        try
        {
            return Directory
                .EnumerateFiles(MusicDirectory)
                .Where(path => SupportedMusicExtensions.Contains(Path.GetExtension(path)))
                .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase)
                .Select(path => new MobileMediaTrack(Path.GetFileName(path), GetDisplayName(path)))
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    internal IReadOnlyList<MobileMediaSelection> GetSelections()
    {
        var selections = new List<MobileMediaSelection>
        {
            new(NoMusicTrackId, Langs.Mobile.Resources.O_NoMusic),
            new(RandomMusicTrackId, Langs.Mobile.Resources.O_RandomMusic)
        };
        selections.AddRange(GetTracks().Select(track => new MobileMediaSelection(track.Id, track.DisplayName)));
        return selections;
    }

    internal async Task<string?> ImportMusicAsync(IStorageFile file, CancellationToken cancellationToken = default) =>
        await ImportFileAsync(file, MusicDirectory, SupportedMusicExtensions, cancellationToken).ConfigureAwait(false);

    internal async Task<string?> ImportImageAsync(IStorageFile file, CancellationToken cancellationToken = default)
    {
        var imageId = await ImportFileAsync(file, ImageDirectory, SupportedImageExtensions, cancellationToken)
            .ConfigureAwait(false);
        return imageId is null ? null : Path.Combine(ImageDirectory, imageId);
    }

    internal string? ResolveMusicPath(string selection)
    {
        if (string.IsNullOrWhiteSpace(selection) || selection is NoMusicTrackId or RandomMusicTrackId)
            return null;

        var fileName = Path.GetFileName(selection);
        if (!string.Equals(fileName, selection, StringComparison.Ordinal) ||
            !SupportedMusicExtensions.Contains(Path.GetExtension(fileName)))
            return null;

        var path = Path.Combine(MusicDirectory, fileName);
        return File.Exists(path) ? path : null;
    }

    internal string? ResolveRandomMusicPath()
    {
        var tracks = GetTracks();
        return tracks.Count == 0 ? null : Path.Combine(MusicDirectory, tracks[Random.Shared.Next(tracks.Count)].Id);
    }

    internal bool DeleteMusic(string trackId)
    {
        var path = ResolveMusicPath(trackId);
        if (path is null)
            return false;

        try
        {
            ClearTrackReferences(trackId);
            File.Delete(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal void DeleteImage(string path)
    {
        if (!Path.IsPathRooted(path) || !IsUnderDirectory(path, ImageDirectory) || !File.Exists(path))
            return;

        try { File.Delete(path); }
        catch { }
    }

    internal void DeleteImageIfUnreferenced(string path)
    {
        if (!Path.IsPathRooted(path) || !IsUnderDirectory(path, ImageDirectory) || !File.Exists(path))
            return;

        if (IsImageReferenced(catalogManager.GetStudentListNames(), catalogManager.LoadStudentList, list => list.Students, path) ||
            IsImageReferenced(catalogManager.GetPrizeListNames(), catalogManager.LoadPrizeList, list => list.Prizes, path))
            return;

        try { File.Delete(path); }
        catch { }
    }

    private string MusicDirectory => Utils.GetDirectoryPath("audio", "music");
    private string ImageDirectory => Utils.GetDirectoryPath("images");

    private static async Task<string?> ImportFileAsync(
        IStorageFile file,
        string directory,
        ISet<string> allowedExtensions,
        CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(file.Name).ToLowerInvariant();
        if (!allowedExtensions.Contains(extension))
            return null;

        var stem = MakeSafeFileStem(Path.GetFileNameWithoutExtension(file.Name));
        var destination = Path.Combine(directory, $"{Guid.NewGuid():N}_{stem}{extension}");
        var temporary = $"{destination}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using var source = await file.OpenReadAsync().ConfigureAwait(false);
            await using (var target = File.Create(temporary))
            {
                await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
                await target.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporary, destination, overwrite: true);
            return Path.GetFileName(destination);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private void ClearTrackReferences(string trackId)
    {
        var configChanged = ClearTrackReferences(configHandler.Data.DefaultDrawSettings, trackId)
                            | ClearTrackReferences(configHandler.Data.RollCallSettings, trackId)
                            | ClearTrackReferences(configHandler.Data.QuickDrawSettings, trackId)
                            | ClearTrackReferences(configHandler.Data.LotterySettings, trackId);
        if (configChanged)
            configHandler.Save();

        foreach (var name in catalogManager.GetStudentListNames())
        {
            var list = catalogManager.LoadStudentList(name);
            if (list is null || !ClearTrackReferences(list.Students, trackId))
                continue;
            catalogManager.SaveStudentList(list);
        }

        foreach (var name in catalogManager.GetPrizeListNames())
        {
            var list = catalogManager.LoadPrizeList(name);
            if (list is null || !ClearTrackReferences(list.Prizes, trackId))
                continue;
            catalogManager.SavePrizeList(list);
        }
    }

    private static bool ClearTrackReferences<T>(IEnumerable<T> records, string trackId)
        where T : SecRandom.Shared.Interfaces.IAttachableSettingsObject
    {
        var changed = false;
        foreach (var record in records)
        {
            var settings = record.GetAttachedObject<DrawMusicAttachedSettings>(DrawMusicSettingsId);
            if (settings is null)
                continue;
            var recordChanged = false;
            if (string.Equals(settings.AnimationMusic, trackId, StringComparison.Ordinal))
            {
                settings.AnimationMusic = NoMusicTrackId;
                recordChanged = true;
            }
            if (string.Equals(settings.ResultMusic, trackId, StringComparison.Ordinal))
            {
                settings.ResultMusic = NoMusicTrackId;
                recordChanged = true;
            }
            if (recordChanged)
            {
                record.WriteAttachedObject(DrawMusicSettingsId, settings);
                changed = true;
            }
        }
        return changed;
    }

    private static bool ClearTrackReferences(SecRandom.Core.Models.SubConfigs.Picking.DrawSettingsConfigBase settings, string trackId)
    {
        var changed = false;
        if (string.Equals(settings.AnimationMusic, trackId, StringComparison.Ordinal))
        {
            settings.AnimationMusic = NoMusicTrackId;
            changed = true;
        }
        if (string.Equals(settings.ResultMusic, trackId, StringComparison.Ordinal))
        {
            settings.ResultMusic = NoMusicTrackId;
            changed = true;
        }
        return changed;
    }

    private static bool IsImageReferenced<TList, TRecord>(
        IEnumerable<string> names,
        Func<string, TList?> load,
        Func<TList, IEnumerable<TRecord>> records,
        string path)
        where TRecord : SecRandom.Shared.Interfaces.IAttachableSettingsObject
        where TList : class
    {
        foreach (var name in names)
        {
            var list = load(name);
            if (list is null)
                continue;
            if (records(list).Any(record => string.Equals(
                    record.GetAttachedObject<DrawImageAttachedSettings>(Guid.Parse(GlobalConstants.DrawImageAttachedSettings))?.ImagePath,
                    path,
                    StringComparison.Ordinal)))
                return true;
        }
        return false;
    }

    private static bool IsUnderDirectory(string path, string directory)
    {
        var fullPath = Path.GetFullPath(path);
        var fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(fullDirectory, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal);
    }

    private static string MakeSafeFileStem(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var stem = new string(value
            .Where(character => !invalidCharacters.Contains(character) && !char.IsControl(character))
            .ToArray())
            .Trim();
        return string.IsNullOrWhiteSpace(stem) ? "media" : stem[..Math.Min(stem.Length, 80)];
    }

    private static string GetDisplayName(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        return fileName.Length > 33 && fileName[32] == '_'
            ? fileName[33..]
            : fileName;
    }
}

internal sealed record MobileMediaTrack(string Id, string DisplayName);

internal sealed record MobileMediaSelection(string Id, string DisplayName);
