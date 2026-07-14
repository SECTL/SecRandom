using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Models.SubConfigs.Picking;
using SecRandom.Core.Services.Config;
using SecRandom.Shared;

namespace SecRandom.Services.Music;

public sealed class MusicLibraryService(
    MainConfigHandler configHandler,
    ILogger<MusicLibraryService> logger,
    string? musicDirectory = null)
{
    public const string NoMusicTrackId = "$none";
    public const string RandomTrackId = "$random";
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".flac"
    };

    public ObservableCollection<MusicTrack> Tracks { get; } = [];
    public ObservableCollection<MusicSelection> Selections { get; } = [];
    public string MusicDirectory { get; } = musicDirectory ?? Utils.GetDirectoryPath("audio", "music");

    public void Refresh()
    {
        NormalizeNoMusicSelections();

        List<MusicTrack> tracks;
        try
        {
            Directory.CreateDirectory(MusicDirectory);
            tracks = Directory.EnumerateFiles(MusicDirectory)
                .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
                .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase)
                .Select(path => new MusicTrack(Path.GetFileName(path), Path.GetFileNameWithoutExtension(path),
                    new FileInfo(path).Length))
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "刷新音乐库失败。");
            tracks = [];
        }

        Tracks.Clear();
        foreach (var track in tracks)
            Tracks.Add(track);

        Selections.Clear();
        Selections.Add(new MusicSelection(NoMusicTrackId, Langs.SettingsPages.Picking.Resources.O_NoMusic));
        Selections.Add(new MusicSelection(RandomTrackId, Langs.SettingsPages.Picking.Resources.O_RandomMusic));
        foreach (var track in tracks)
            Selections.Add(new MusicSelection(track.Id, track.DisplayName));

        AddLegacySelections();
    }

    public IReadOnlyList<MusicTrack> Import(IEnumerable<string> sourcePaths)
    {
        var imported = new List<MusicTrack>();
        try
        {
            Directory.CreateDirectory(MusicDirectory);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "创建音乐库目录失败。");
            return imported;
        }

        foreach (var sourcePath in sourcePaths)
        {
            if (!File.Exists(sourcePath) || !SupportedExtensions.Contains(Path.GetExtension(sourcePath)))
                continue;

            try
            {
                var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
                var baseName = Path.GetFileNameWithoutExtension(sourcePath);
                var destinationName = $"{baseName}{extension}";
                var index = 2;
                while (File.Exists(Path.Combine(MusicDirectory, destinationName)))
                    destinationName = $"{baseName} ({index++}){extension}";

                var destinationPath = Path.Combine(MusicDirectory, destinationName);
                File.Copy(sourcePath, destinationPath);
                imported.Add(new MusicTrack(destinationName, Path.GetFileNameWithoutExtension(destinationName),
                    new FileInfo(destinationPath).Length));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "导入音乐失败：文件={FileName}。", Path.GetFileName(sourcePath));
            }
        }

        Refresh();
        return imported;
    }

    public bool Delete(MusicTrack track)
    {
        var path = ResolvePath(track.Id);
        if (path is null)
            return false;

        try
        {
            File.Delete(path);
            ClearReferences(track.Id);
            Refresh();
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "删除音乐失败：文件={FileName}。", track.Id);
            return false;
        }
    }

    public string? ResolvePath(string selection)
    {
        if (string.IsNullOrWhiteSpace(selection) || selection is NoMusicTrackId or RandomTrackId)
            return null;

        if (Path.IsPathRooted(selection))
            return SupportedExtensions.Contains(Path.GetExtension(selection)) && File.Exists(selection) ? selection : null;

        var fileName = Path.GetFileName(selection);
        if (!string.Equals(fileName, selection, StringComparison.Ordinal) || !SupportedExtensions.Contains(Path.GetExtension(fileName)))
            return null;

        var path = Path.Combine(MusicDirectory, fileName);
        return File.Exists(path) ? path : null;
    }

    public string? ResolveRandomPath()
    {
        if (Tracks.Count == 0)
            return null;
        return ResolvePath(Tracks[Random.Shared.Next(Tracks.Count)].Id);
    }

    private void AddLegacySelections()
    {
        foreach (var selection in GetConfiguredSelections()
                      .Where(selection => !string.IsNullOrWhiteSpace(selection))
                      .Where(selection => selection != NoMusicTrackId)
                      .Where(selection => selection != RandomTrackId)
                     .Where(selection => Selections.All(item => item.Id != selection)))
        {
            var displayName = Path.GetFileName(selection);
            var isAvailable = ResolvePath(selection) is not null;
            Selections.Add(new MusicSelection(
                selection,
                string.Format(
                    isAvailable
                        ? Langs.SettingsPages.Picking.Resources.O_MusicExternal
                        : Langs.SettingsPages.Picking.Resources.O_MusicUnavailable,
                    string.IsNullOrWhiteSpace(displayName) ? selection : displayName),
                isAvailable));
        }
    }

    private IEnumerable<string> GetConfiguredSelections()
    {
        foreach (var settings in GetAllDrawSettings())
        {
            yield return settings.AnimationMusic;
            yield return settings.ResultMusic;
        }
    }

    private IEnumerable<DrawSettingsConfigBase> GetAllDrawSettings()
    {
        yield return configHandler.Data.DefaultDrawSettings;
        yield return configHandler.Data.RollCallSettings;
        yield return configHandler.Data.QuickDrawSettings;
        yield return configHandler.Data.LotterySettings;
    }

    private void ClearReferences(string trackId)
    {
        var changed = false;
        foreach (var settings in GetAllDrawSettings())
        {
            if (settings.AnimationMusic == trackId)
            {
                settings.AnimationMusic = NoMusicTrackId;
                changed = true;
            }
            if (settings.ResultMusic == trackId)
            {
                settings.ResultMusic = NoMusicTrackId;
                changed = true;
            }
        }

        if (changed)
            configHandler.Save();
    }

    private void NormalizeNoMusicSelections()
    {
        var changed = false;
        foreach (var settings in GetAllDrawSettings())
        {
            if (string.IsNullOrWhiteSpace(settings.AnimationMusic))
            {
                settings.AnimationMusic = NoMusicTrackId;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(settings.ResultMusic))
            {
                settings.ResultMusic = NoMusicTrackId;
                changed = true;
            }
        }

        if (changed)
            configHandler.Save();
    }
}
