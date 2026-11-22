using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Models.Orchestration;
using SecRandom.Core.Services.Profiles;
using PR = SecRandom.Langs.SettingsPages.Picking.Resources;

namespace SecRandom.Views.SettingsPages.Picking;

/// <summary>
///     右侧抽屉里创建或编辑一个编排名单：名称、抽取模式、来源名单和轮次数。
/// </summary>
/// <remarks>
///     The drawer owns the draft and only hands a finished result back through the page's callback, so cancelling
///     leaves the stored profile untouched. The name may stay blank: the page then names the list after its source
///     list and channel, exactly like the old inline form did.
/// </remarks>
public partial class OrchestrationEditorDrawer : UserControl, INotifyPropertyChanged
{
    private readonly Action<OrchestrationEditorResult> _apply;
    private readonly IProfileCatalogManager _catalog = IAppHost.GetService<IProfileCatalogManager>();
    private readonly OrchestrationProfile? _profile;
    private int _selectedTargetIndex;
    private string _selectedSourceList = string.Empty;
    private string _profileName = string.Empty;
    private double _roundCount = 1;
    private event PropertyChangedEventHandler? NotifyPropertyChanged;

    public OrchestrationEditorDrawer(OrchestrationProfile? profile, Action<OrchestrationEditorResult> apply)
    {
        _profile = profile;
        _apply = apply;
        if (profile is not null)
        {
            _profileName = profile.Name;
            _selectedTargetIndex = profile.Target == OrchestrationTarget.Lottery ? 1 : 0;
            _selectedSourceList = profile.SourceListName;
            _roundCount = profile.EffectiveRoundCount;
        }

        DataContext = this;
        InitializeComponent();
        RefreshSourceLists();
    }

    public ObservableCollection<string> TargetModes { get; } =
        [PR.O_Orchestration_TargetRollCall, PR.O_Orchestration_TargetLottery];

    public ObservableCollection<string> SourceLists { get; } = [];

    public bool IsEditing => _profile is not null;

    public string Title => IsEditing ? PR.S_Orchestration_ProfileEdit : PR.S_Orchestration_ProfileNew;

    public string PrimaryButtonText => IsEditing ? PR.C_Orchestration_Save : PR.C_Orchestration_Create;

    public bool HasAnySourceList => SourceLists.Count > 0;

    public string ProfileName
    {
        get => _profileName;
        set
        {
            if (_profileName == value)
                return;
            _profileName = value ?? string.Empty;
            OnPropertyChanged();
        }
    }

    /// <summary>0 = 点名, 1 = 抽奖; the source picker follows the channel.</summary>
    public int SelectedTargetIndex
    {
        get => _selectedTargetIndex;
        set
        {
            if (value is < 0 or > 1)
                // ComboBox 在重建 ItemsSource 时会推一个 -1 回来，忽略它，免得把抽奖清单切成点名清单。
                return;
            if (_selectedTargetIndex == value)
                return;
            _selectedTargetIndex = value;
            OnPropertyChanged();
            RefreshSourceLists();
        }
    }

    public string SelectedSourceList
    {
        get => _selectedSourceList;
        set
        {
            if (_selectedSourceList == value)
                return;
            _selectedSourceList = value ?? string.Empty;
            OnPropertyChanged();
        }
    }

    public double RoundCount
    {
        get => _roundCount;
        set
        {
            var clamped = Math.Clamp(value, 1, OrchestrationProfile.MaxRounds);
            if (Math.Abs(clamped - _roundCount) < 0.0001)
                return;
            _roundCount = clamped;
            OnPropertyChanged();
        }
    }

    // AvaloniaObject 已经有一个 PropertyChanged，所以实现接口时改用私有事件转发。
    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => NotifyPropertyChanged += value;
        remove => NotifyPropertyChanged -= value;
    }

    private void RefreshSourceLists()
    {
        var previous = SelectedSourceList;
        SourceLists.Clear();
        var names = _selectedTargetIndex == 1 ? _catalog.GetPrizeListNames() : _catalog.GetStudentListNames();
        foreach (var name in names)
            SourceLists.Add(name);

        OnPropertyChanged(nameof(HasAnySourceList));
        // 换通道后旧的来源名单可能不存在于新通道，这时落到该通道的第一份名单。重建 ItemsSource 时
        // ComboBox 会把第一项推回来，所以「原来的名单还在」这一支必须再写一次原值，否则一进编辑
        // 就把来源名单悄悄换成第一份，保存下去等于换了名单。
        if (SourceLists.Contains(previous))
            SelectedSourceList = previous;
        else if (SourceLists.Count > 0)
            SelectedSourceList = SourceLists[0];
        else
            SelectedSourceList = string.Empty;
    }

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e) => SettingsView.Current?.CloseDrawer();

    private void ConfirmButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SelectedSourceList))
        {
            this.ShowWarningToast(PR.M_Orchestration_SourceRequired);
            return;
        }

        var target = SelectedTargetIndex == 1 ? OrchestrationTarget.Lottery : OrchestrationTarget.RollCall;
        var name = string.IsNullOrWhiteSpace(ProfileName)
            ? $"{SelectedSourceList} | {TargetModes[Math.Clamp(SelectedTargetIndex, 0, 1)]}"
            : ProfileName.Trim();
        _apply(new OrchestrationEditorResult(name, target, SelectedSourceList,
            (int)Math.Clamp(RoundCount, 1, OrchestrationProfile.MaxRounds)));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>What the 编排名单抽屉 hands back once the user confirms.</summary>
public sealed record OrchestrationEditorResult(string Name, OrchestrationTarget Target, string SourceListName, int RoundCount);