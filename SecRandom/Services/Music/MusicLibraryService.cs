using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.Logging;
using SecRandom.Core;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Models.AttachedSettings;
using SecRandom.Core.Models.SubConfigs.Picking;
using SecRandom.Core.Services.Config;
using SecRandom.Shared;
using SecRandom.Shared.Extensions;
using SecRandom.Shared.Interfaces;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Services.Music;

public sealed class MusicLibraryService(
    MainConfigHandler configHandler,
    ILogger<MusicLibraryService> logger,
    string? musicDirectory = null,
    IProfileService? attachedSettingsProfileService = null)
{
    public const string NoMusicTrackId = "$none";
    public const string RandomTrackId = "$random";
    private static readonly Guid DrawMusicSettingsId = Guid.Parse(GlobalConstants.DrawMusicAttachedSettings);
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

    public async Task<IReadOnlyList<MusicTrack>> ImportAsync(IEnumerable<IStorageFile> sourceFiles)
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

        foreach (var sourceFile in sourceFiles)
        {
            var extension = Path.GetExtension(sourceFile.Name).ToLowerInvariant();
            if (!SupportedExtensions.Contains(extension))
                continue;

            try
            {
                var baseName = Path.GetFileNameWithoutExtension(sourceFile.Name);
                var destinationName = $"{baseName}{extension}";
                var index = 2;
                while (File.Exists(Path.Combine(MusicDirectory, destinationName)))
                    destinationName = $"{baseName} ({index++}){extension}";

                var destinationPath = Path.Combine(MusicDirectory, destinationName);
                await using var source = await sourceFile.OpenReadAsync();
                await using var destination = File.Create(destinationPath);
                await source.CopyToAsync(destination);
                imported.Add(new MusicTrack(destinationName, Path.GetFileNameWithoutExtension(destinationName),
                    new FileInfo(destinationPath).Length));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "导入音乐失败：文件={FileName}。", sourceFile.Name);
            }
        }

        Refresh();
        return imported;
    }

    public bool Delete(MusicTrack track)
    {
        var path = ResolveManagedPath(track.Id);
        if (path is null)
            return false;

        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "删除音乐失败：文件={FileName}。", track.Id);
            return false;
        }

        try
        {
            ClearReferences(track.Id);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "清除已删除音乐的引用失败：文件={FileName}。", track.Id);
        }
        finally
        {
            Refresh();
        }

        return true;
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

    private string? ResolveManagedPath(string trackId)
    {
        if (string.IsNullOrWhiteSpace(trackId) || Path.IsPathRooted(trackId))
            return null;

        var fileName = Path.GetFileName(trackId);
        if (!string.Equals(fileName, trackId, StringComparison.Ordinal) || !SupportedExtensions.Contains(Path.GetExtension(fileName)))
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

        foreach (var selection in GetAttachedMusicSelections())
            yield return selection;
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

        changed |= ClearAttachedMusicReferences(trackId);
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

    private IEnumerable<string> GetAttachedMusicSelections()
    {
        var profileService = attachedSettingsProfileService;
        if (profileService is null)
            yield break;

        foreach (var selection in GetStudentSelections(profileService.CurrentStudentList?.Students))
            yield return selection;
        foreach (var selection in GetPrizeSelections(profileService.CurrentPrizeList?.Prizes))
            yield return selection;

        if (profileService.StudentListConfig is { } studentListConfig)
        {
            foreach (var name in GetListNames("roll_call_list"))
            {
                if (name == studentListConfig.Name)
                    continue;

                foreach (var selection in TryGetStudentListSelections(name))
                    yield return selection;
            }
        }

        if (profileService.PrizeListConfig is { } prizeListConfig)
        {
            foreach (var name in GetListNames("lottery_list"))
            {
                if (name == prizeListConfig.Name)
                    continue;

                foreach (var selection in TryGetPrizeListSelections(name))
                    yield return selection;
            }
        }
    }

    private bool ClearAttachedMusicReferences(string trackId)
    {
        var profileService = attachedSettingsProfileService;
        if (profileService is null)
            return false;

        var changed = ClearStudentReferences(profileService.CurrentStudentList?.Students, trackId);
        changed |= ClearPrizeReferences(profileService.CurrentPrizeList?.Prizes, trackId);
        if (changed)
            profileService.SaveProfile();

        if (profileService.StudentListConfig is { } studentListConfig)
        {
            foreach (var name in GetListNames("roll_call_list"))
            {
                if (name != studentListConfig.Name)
                    changed |= TryClearStudentListReferences(trackId, new StudentListConfig(name));
            }
        }

        if (profileService.PrizeListConfig is { } prizeListConfig)
        {
            foreach (var name in GetListNames("lottery_list"))
            {
                if (name != prizeListConfig.Name)
                    changed |= TryClearPrizeListReferences(trackId, new PrizeListConfig(name));
            }
        }

        return changed;
    }

    private IEnumerable<string> GetListNames(string directoryName)
    {
        try
        {
            var directory = Utils.GetDirectoryPath("list", directoryName);
            return Directory.EnumerateFiles(directory, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>()
                .ToArray();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "枚举音乐引用名单失败：目录={DirectoryName}。", directoryName);
            return [];
        }
    }

    private bool TryClearStudentListReferences(string trackId, StudentListConfig config)
    {
        try
        {
            return ClearStudentListReferences(trackId, config);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "清除学生名单音乐引用失败：名单={ListName}。", config.Name);
            return false;
        }
    }

    private bool TryClearPrizeListReferences(string trackId, PrizeListConfig config)
    {
        try
        {
            return ClearPrizeListReferences(trackId, config);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "清除奖品池音乐引用失败：奖品池={ListName}。", config.Name);
            return false;
        }
    }

    private IEnumerable<string> TryGetStudentListSelections(string name)
    {
        try
        {
            return GetStudentSelections(new StudentListConfig(name).Data.Students).ToArray();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "读取学生名单音乐引用失败：名单={ListName}。", name);
            return [];
        }
    }

    private IEnumerable<string> TryGetPrizeListSelections(string name)
    {
        try
        {
            return GetPrizeSelections(new PrizeListConfig(name).Data.Prizes).ToArray();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "读取奖品池音乐引用失败：奖品池={ListName}。", name);
            return [];
        }
    }

    private static IEnumerable<string> GetStudentSelections(IEnumerable<Student>? students)
    {
        if (students is null)
            yield break;

        foreach (var student in students)
        {
            var settings = student.GetAttachedObject<DrawMusicAttachedSettings>(DrawMusicSettingsId);
            if (settings is null)
                continue;

            yield return settings.AnimationMusic;
            yield return settings.ResultMusic;
        }
    }

    private static IEnumerable<string> GetPrizeSelections(IEnumerable<Prize>? prizes)
    {
        if (prizes is null)
            yield break;

        foreach (var prize in prizes)
        {
            var settings = prize.GetAttachedObject<DrawMusicAttachedSettings>(DrawMusicSettingsId);
            if (settings is null)
                continue;

            yield return settings.AnimationMusic;
            yield return settings.ResultMusic;
        }
    }

    private static bool ClearStudentReferences(IEnumerable<Student>? students, string trackId)
    {
        if (students is null)
            return false;

        var changed = false;
        foreach (var student in students)
            changed |= ClearAttachedMusicReference(student, trackId);
        return changed;
    }

    private static bool ClearPrizeReferences(IEnumerable<Prize>? prizes, string trackId)
    {
        if (prizes is null)
            return false;

        var changed = false;
        foreach (var prize in prizes)
            changed |= ClearAttachedMusicReference(prize, trackId);
        return changed;
    }

    private static bool ClearStudentListReferences(string trackId, StudentListConfig? config)
    {
        if (config is null)
            return false;

        var changed = ClearStudentReferences(config.Data.Students, trackId);

        if (changed)
            config.Save();
        return changed;
    }

    private static bool ClearPrizeListReferences(string trackId, PrizeListConfig? config)
    {
        if (config is null)
            return false;

        var changed = ClearPrizeReferences(config.Data.Prizes, trackId);

        if (changed)
            config.Save();
        return changed;
    }

    private static bool ClearAttachedMusicReference(IAttachableSettingsObject target, string trackId)
    {
        var settings = target.GetAttachedObject<DrawMusicAttachedSettings>(DrawMusicSettingsId);
        if (settings is null)
            return false;

        var changed = false;
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

        if (changed)
            target.WriteAttachedObject(DrawMusicSettingsId, settings);
        return changed;
    }
}
