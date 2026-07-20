using Avalonia.Controls;
using Avalonia.Layout;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Icons;
using SecRandom.Mobile.Controls;
using SecRandom.Shared.Models.Profile;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;

namespace SecRandom.Mobile.Views.Settings;

/// <summary>
/// 名单管理页：成员名单 / 奖池双 surface，由 <see cref="MobileSegmentedControl"/> 切换；
/// 抽奖段按 <see cref="MobileCapabilities.IsLotteryEnabled"/> 投影禁用。添加表单卡片化，
/// 条目为带启停开关与删除按钮的设置行，空列表用 <see cref="MobileEmptyState"/>。
/// <see cref="IProfileCatalogEditor"/> 的调用语义与原实现保持一致。
/// </summary>
public sealed class MobileListManagementSettingsPage : MobileSettingsPageBase
{
    private readonly IProfileCatalogEditor _catalogEditor;
    private int _segment;

    public MobileListManagementSettingsPage(IProfileCatalogEditor catalogEditor)
    {
        _catalogEditor = catalogEditor;
        Render();
    }

    private void Render()
    {
        // 抽奖被关闭时 surface 回退到成员名单，胶囊中的奖池段同时处于禁用态。
        if (_segment == 1 && !IsLotteryEnabled)
            _segment = 0;

        var segmented = new MobileSegmentedControl();
        segmented.SetItems([
            (LR.S_StudentList, (object)0, true),
            (LR.S_PrizePool, (object)1, IsLotteryEnabled)
        ]);
        if (_segment == 1)
            segmented.Select(1);
        segmented.SelectionChanged += (_, _) =>
        {
            if (segmented.SelectedTag is int segment && segment != _segment)
            {
                _segment = segment;
                Render();
            }
        };

        var items = new List<Control> { segmented };
        items.AddRange(_segment == 0 ? BuildStudentSurface() : BuildPrizeSurface());
        Content = BuildPage(LR.S_ListManagement, LR.S_ListManagement_D, items);
    }

    private List<Control> BuildStudentSurface()
    {
        var nameBox = new TextBox { PlaceholderText = LR.W_StudentName, MinHeight = 44 };
        var idBox = new TextBox { PlaceholderText = LR.W_StudentId, MinHeight = 44 };
        var items = new List<Control>
        {
            CreateAddForm(LR.C_AddStudent, FluentIcons.PersonAddFilled, nameBox, idBox,
                () => AddStudent(nameBox, idBox))
        };

        var students = _catalogEditor.GetStudents();
        if (students.Count == 0)
        {
            items.Add(new MobileEmptyState(FluentIcons.PeopleFilled, LR.M_EmptyStudentList, LR.M_EmptyStudentListHint));
            return items;
        }

        foreach (var student in students)
            items.Add(CreateStudentRow(student));
        return items;
    }

    private List<Control> BuildPrizeSurface()
    {
        var nameBox = new TextBox { PlaceholderText = LR.W_PrizeName, MinHeight = 44 };
        var idBox = new TextBox { PlaceholderText = LR.W_PrizeId, MinHeight = 44 };
        var items = new List<Control>
        {
            CreateAddForm(LR.C_AddPrize, FluentIcons.GiftFilled, nameBox, idBox,
                () => AddPrize(nameBox, idBox))
        };

        var prizes = _catalogEditor.GetPrizes();
        if (prizes.Count == 0)
        {
            items.Add(new MobileEmptyState(FluentIcons.GiftFilled, LR.M_EmptyPrizePool, LR.M_EmptyPrizePoolHint));
            return items;
        }

        foreach (var prize in prizes)
            items.Add(CreatePrizeRow(prize));
        return items;
    }

    // 输入区卡片化表单：标题 + 名称/编号输入 + 主操作按钮。
    private static MobileCard CreateAddForm(string title, string glyph, TextBox nameBox, TextBox idBox, Action add)
    {
        var addButton = MobileUi.CreatePrimaryButton(title, true, add);
        return new MobileCard
        {
            Content = new StackPanel
            {
                Spacing = MobileTheme.FindDouble("MobileSpacingSm", 8),
                Children =
                {
                    new MobileSectionHeader(title, glyph),
                    nameBox,
                    idBox,
                    addButton
                }
            }
        };
    }

    private Control CreateStudentRow(Student student)
    {
        var active = CreateActiveToggle(student.Exists, value =>
        {
            _catalogEditor.SetStudentEnabled(student.RecordId.ToString("D"), value);
            Render();
        });
        return MobileSettingRow.Simple(MobileUi.Format(student.Id, student.Name),
            student.Exists ? LR.M_Enabled : LR.M_Disabled, active, () =>
            {
                _catalogEditor.RemoveStudent(student.RecordId.ToString("D"));
                Render();
            });
    }

    private Control CreatePrizeRow(Prize prize)
    {
        var active = CreateActiveToggle(prize.Exists, value =>
        {
            _catalogEditor.SetPrizeEnabled(prize.RecordId.ToString("D"), value);
            Render();
        });
        return MobileSettingRow.Simple(MobileUi.Format(prize.Id, prize.Name),
            prize.Exists ? LR.M_Enabled : LR.M_Disabled, active, () =>
            {
                _catalogEditor.RemovePrize(prize.RecordId.ToString("D"));
                Render();
            });
    }

    private void AddStudent(TextBox name, TextBox id)
    {
        if (string.IsNullOrWhiteSpace(name.Text) && string.IsNullOrWhiteSpace(id.Text))
            return;

        if (_catalogEditor.AddStudent(name.Text ?? string.Empty, id.Text ?? string.Empty))
            Render();
    }

    private void AddPrize(TextBox name, TextBox id)
    {
        if (string.IsNullOrWhiteSpace(name.Text) && string.IsNullOrWhiteSpace(id.Text))
            return;

        if (_catalogEditor.AddPrize(name.Text ?? string.Empty, id.Text ?? string.Empty))
            Render();
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
