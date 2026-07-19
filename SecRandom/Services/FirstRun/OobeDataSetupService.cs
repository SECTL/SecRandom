using System;
using System.Collections.Generic;
using System.IO;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Services.Config;
using SecRandom.Shared.Abstraction;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Services.FirstRun;

public sealed class OobeDataSetupService(MainConfigHandler configHandler, IProfileService profileService)
{
    public void SaveStudentList(string name, IReadOnlyList<Student> students)
    {
        name = ValidateName(name);
        var listConfig = new StudentListConfig(name);
        listConfig.Data.Students.Clear();
        foreach (var student in students)
        {
            student.RecordId = student.RecordId == Guid.Empty ? Guid.NewGuid() : student.RecordId;
            listConfig.Data.Students.Add(student);
        }
        listConfig.Save();
        configHandler.Data.RollCallSettings.DefaultClass = name;
        configHandler.Save();
        profileService.LoadStudentProfile(name, saveCurrent: false);
    }

    public void SavePrizeList(string name, IReadOnlyList<Prize> prizes)
    {
        name = ValidateName(name);
        var listConfig = new PrizeListConfig(name);
        listConfig.Data.Prizes.Clear();
        foreach (var prize in prizes)
        {
            prize.RecordId = prize.RecordId == Guid.Empty ? Guid.NewGuid() : prize.RecordId;
            listConfig.Data.Prizes.Add(prize);
        }
        listConfig.Save();
        configHandler.Data.LotterySettings.DefaultPool = name;
        configHandler.Save();
        profileService.LoadPrizeProfile(name, saveCurrent: false);
    }

    public void CreateStudentList(string name)
    {
        name = ValidateName(name);
        if (File.Exists(new StudentList(name).ConfigFilePath))
            throw new OobeDataSetupException(OobeDataSetupError.ListAlreadyExists);

        new StudentListConfig(name).Save();
    }

    public void CreatePrizeList(string name)
    {
        name = ValidateName(name);
        if (File.Exists(new PrizeList(name).ConfigFilePath))
            throw new InvalidOperationException("同名奖品池已存在。");

        new PrizeListConfig(name).Save();
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
