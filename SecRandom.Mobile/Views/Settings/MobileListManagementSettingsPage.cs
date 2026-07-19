using Avalonia.Controls;
using Avalonia.Layout;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Shared.Extensions;
using SecRandom.Shared.Models.Profile;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;

namespace SecRandom.Mobile.Views.Settings;

internal sealed class MobileListManagementSettingsPage : UserControl
{
    private readonly IProfileService _profileService;
    private readonly Action _refresh;

    internal MobileListManagementSettingsPage(IProfileService profileService, Action goBack, Action refresh)
    {
        _profileService = profileService;
        _refresh = refresh;

        var studentName = new TextBox { PlaceholderText = LR.W_StudentName, MinHeight = 44 };
        var studentId = new TextBox { PlaceholderText = LR.W_StudentId, MinHeight = 44 };
        var prizeName = new TextBox { PlaceholderText = LR.W_PrizeName, MinHeight = 44 };
        var prizeId = new TextBox { PlaceholderText = LR.W_PrizeId, MinHeight = 44 };
        var studentRows = new StackPanel { Spacing = 8 };
        var prizeRows = new StackPanel { Spacing = 8 };
        foreach (var student in (_profileService.CurrentStudentList?.Students ?? []).OrderForList())
            studentRows.Children.Add(CreateStudentRow(student));
        foreach (var prize in (_profileService.CurrentPrizeList?.Prizes ?? []).OrderForList())
            prizeRows.Children.Add(CreatePrizeRow(prize));

        Content = MobileUi.CreateSettingsScroll(LR.S_ListManagement, LR.S_ListManagement_D, goBack, [
            MobileUi.CreateLabel(LR.S_StudentList),
            MobileUi.CreateTitle(LR.C_AddStudent),
            studentName,
            studentId,
            MobileUi.CreatePrimaryButton(LR.C_AddStudent, true, () => AddStudent(studentName, studentId)),
            studentRows,
            MobileUi.CreateLabel(LR.S_PrizePool),
            MobileUi.CreateTitle(LR.C_AddPrize),
            prizeName,
            prizeId,
            MobileUi.CreatePrimaryButton(LR.C_AddPrize, true, () => AddPrize(prizeName, prizeId)),
            prizeRows
        ]);
    }

    private Control CreateStudentRow(Student student)
    {
        var active = CreateActiveToggle(student.Exists, value =>
        {
            student.Exists = value;
            _profileService.SaveProfile();
            _refresh();
        });
        return MobileUi.CreateRow(MobileUi.Format(student.Id, student.Name), student.Exists ? LR.M_Enabled : LR.M_Disabled, active, () =>
        {
            _profileService.CurrentStudentList?.Students.Remove(student);
            _profileService.SaveProfile();
            _refresh();
        });
    }

    private Control CreatePrizeRow(Prize prize)
    {
        var active = CreateActiveToggle(prize.Exists, value =>
        {
            prize.Exists = value;
            _profileService.SaveProfile();
            _refresh();
        });
        return MobileUi.CreateRow(MobileUi.Format(prize.Id, prize.Name), prize.Exists ? LR.M_Enabled : LR.M_Disabled, active, () =>
        {
            _profileService.CurrentPrizeList?.Prizes.Remove(prize);
            _profileService.SaveProfile();
            _refresh();
        });
    }

    private void AddStudent(TextBox name, TextBox id)
    {
        if (string.IsNullOrWhiteSpace(name.Text) && string.IsNullOrWhiteSpace(id.Text))
            return;

        var student = new Student
        {
            Name = name.Text?.Trim() ?? string.Empty,
            Id = id.Text?.Trim() ?? string.Empty
        };
        ProfileRecordIdentity.EnsureRecordId(student);
        _profileService.CurrentStudentList?.Students.Add(student);
        _profileService.SaveProfile();
        _refresh();
    }

    private void AddPrize(TextBox name, TextBox id)
    {
        if (string.IsNullOrWhiteSpace(name.Text) && string.IsNullOrWhiteSpace(id.Text))
            return;

        var prize = new Prize
        {
            Name = name.Text?.Trim() ?? string.Empty,
            Id = id.Text?.Trim() ?? string.Empty
        };
        ProfileRecordIdentity.EnsureRecordId(prize);
        _profileService.CurrentPrizeList?.Prizes.Add(prize);
        _profileService.SaveProfile();
        _refresh();
    }

    private static ToggleSwitch CreateActiveToggle(bool enabled, Action<bool> setEnabled)
    {
        var active = new ToggleSwitch
        {
            IsChecked = enabled,
            OnContent = string.Empty,
            OffContent = string.Empty,
            VerticalAlignment = VerticalAlignment.Center
        };
        active.IsCheckedChanged += (_, _) => setEnabled(active.IsChecked == true);
        return active;
    }
}
