using SecRandom.Core.Abstraction.Services;
using SecRandom.Shared.Extensions;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services.Profiles;

internal sealed class ProfileCatalogEditor(IProfileService profileService) : IProfileCatalogEditor
{
    public IReadOnlyList<Student> GetStudents() =>
        (profileService.CurrentStudentList?.Students ?? []).OrderForList().ToArray();

    public IReadOnlyList<Prize> GetPrizes() =>
        (profileService.CurrentPrizeList?.Prizes ?? []).OrderForList().ToArray();

    public bool AddStudent(string name, string id)
    {
        if (!HasDisplayValue(name, id) || profileService.CurrentStudentList is null)
            return false;

        var student = new Student { Name = name.Trim(), Id = id.Trim() };
        ProfileRecordIdentity.EnsureRecordId(student);
        profileService.CurrentStudentList.Students.Add(student);
        profileService.SaveProfile();
        return true;
    }

    public bool AddPrize(string name, string id)
    {
        if (!HasDisplayValue(name, id) || profileService.CurrentPrizeList is null)
            return false;

        var prize = new Prize { Name = name.Trim(), Id = id.Trim() };
        ProfileRecordIdentity.EnsureRecordId(prize);
        profileService.CurrentPrizeList.Prizes.Add(prize);
        profileService.SaveProfile();
        return true;
    }

    public bool SetStudentEnabled(string recordId, bool enabled)
    {
        var student = profileService.CurrentStudentList?.Students.FirstOrDefault(item => ProfileRecordIdentity.EnsureRecordId(item) == recordId);
        if (student is null)
            return false;

        student.Exists = enabled;
        profileService.SaveProfile();
        return true;
    }

    public bool SetPrizeEnabled(string recordId, bool enabled)
    {
        var prize = profileService.CurrentPrizeList?.Prizes.FirstOrDefault(item => ProfileRecordIdentity.EnsureRecordId(item) == recordId);
        if (prize is null)
            return false;

        prize.Exists = enabled;
        profileService.SaveProfile();
        return true;
    }

    public bool RemoveStudent(string recordId)
    {
        var student = profileService.CurrentStudentList?.Students.FirstOrDefault(item => ProfileRecordIdentity.EnsureRecordId(item) == recordId);
        return student is not null && profileService.CurrentStudentList!.Students.Remove(student) && Save();
    }

    public bool RemovePrize(string recordId)
    {
        var prize = profileService.CurrentPrizeList?.Prizes.FirstOrDefault(item => ProfileRecordIdentity.EnsureRecordId(item) == recordId);
        return prize is not null && profileService.CurrentPrizeList!.Prizes.Remove(prize) && Save();
    }

    private bool Save()
    {
        profileService.SaveProfile();
        return true;
    }

    private static bool HasDisplayValue(string name, string id) =>
        !string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(id);
}
