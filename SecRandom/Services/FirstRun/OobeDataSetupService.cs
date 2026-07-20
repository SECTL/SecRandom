using System;
using System.Collections.Generic;
using System.IO;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Services.Config;
using SecRandom.Shared.Abstraction;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Services.FirstRun;

public sealed class OobeDataSetupService(
    MainConfigHandler configHandler,
    IProfileService profileService,
    IProfileCatalogManager catalogManager)
{
    public void SaveStudentList(string name, IReadOnlyList<Student> students)
    {
        name = ValidateName(name);
        // ReplaceStudents 负责 RecordId 规范化、排序与落盘；SetDefaultStudentList 持久化默认班级。
        catalogManager.ReplaceStudents(name, students);
        catalogManager.SetDefaultStudentList(name);
        profileService.LoadStudentProfile(name, saveCurrent: false);
    }

    public void SavePrizeList(string name, IReadOnlyList<Prize> prizes)
    {
        name = ValidateName(name);
        catalogManager.ReplacePrizes(name, prizes);
        catalogManager.SetDefaultPrizePool(name);
        profileService.LoadPrizeProfile(name, saveCurrent: false);
    }

    public void CreateStudentList(string name)
    {
        name = ValidateName(name);
        if (!catalogManager.CreateStudentList(name))
            throw new OobeDataSetupException(OobeDataSetupError.ListAlreadyExists);
    }

    public void CreatePrizeList(string name)
    {
        name = ValidateName(name);
        if (!catalogManager.CreatePrizeList(name))
            throw new InvalidOperationException("同名奖品池已存在。");
    }

    public void RenameStudentList(string oldName, string newName)
    {
        oldName = ValidateName(oldName);
        newName = ValidateName(newName);
        if (oldName == newName)
            return;
        if (File.Exists(new StudentList(newName).ConfigFilePath))
            throw new OobeDataSetupException(OobeDataSetupError.ListAlreadyExists);

        profileService.SaveProfile();
        MoveProfileFile(new StudentList(oldName), new StudentList(newName));
        MoveProfileFile(new StudentHistory(oldName), new StudentHistory(newName));
        if (configHandler.Data.RollCallSettings.DefaultClass == oldName)
            configHandler.Data.RollCallSettings.DefaultClass = newName;
        configHandler.Save();
        profileService.LoadStudentProfile(newName, saveCurrent: false);
    }

    public void RenamePrizeList(string oldName, string newName)
    {
        oldName = ValidateName(oldName);
        newName = ValidateName(newName);
        if (oldName == newName)
            return;
        if (File.Exists(new PrizeList(newName).ConfigFilePath))
            throw new InvalidOperationException("同名奖品池已存在。");

        profileService.SaveProfile();
        MoveProfileFile(new PrizeList(oldName), new PrizeList(newName));
        MoveProfileFile(new PrizeHistory(oldName), new PrizeHistory(newName));
        if (configHandler.Data.LotterySettings.DefaultPool == oldName)
            configHandler.Data.LotterySettings.DefaultPool = newName;
        configHandler.Save();
        profileService.LoadPrizeProfile(newName, saveCurrent: false);
    }

    public void DeleteStudentList(string name)
    {
        name = ValidateName(name);
        profileService.SaveProfile();
        DeleteProfileFile(new StudentList(name));
        DeleteProfileFile(new StudentHistory(name));
    }

    public void DeletePrizeList(string name)
    {
        name = ValidateName(name);
        profileService.SaveProfile();
        DeleteProfileFile(new PrizeList(name));
        DeleteProfileFile(new PrizeHistory(name));
    }

    private static string ValidateName(string name)
    {
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new OobeDataSetupException(OobeDataSetupError.ListNameInvalid);
        return name;
    }

    private static void MoveProfileFile(ProfileConfigBase oldConfig, ProfileConfigBase newConfig)
    {
        if (!File.Exists(oldConfig.ConfigFilePath))
            return;

        File.Move(oldConfig.ConfigFilePath, newConfig.ConfigFilePath);
    }

    private static void DeleteProfileFile(ProfileConfigBase config)
    {
        if (File.Exists(config.ConfigFilePath))
            File.Delete(config.ConfigFilePath);
    }
}

public enum OobeDataSetupError
{
    ListNameInvalid,
    ListAlreadyExists
}

public sealed class OobeDataSetupException(OobeDataSetupError error) : InvalidOperationException
{
    public OobeDataSetupError Error { get; } = error;
}
