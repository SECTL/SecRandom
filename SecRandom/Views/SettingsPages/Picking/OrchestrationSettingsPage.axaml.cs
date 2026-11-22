using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.Orchestration;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Draw;
using SecRandom.Core.Services.Orchestration;
using SecRandom.Core.Services.Profiles;
using SecRandom.Shared.Models.Profile;
using PR = SecRandom.Langs.SettingsPages.Picking.Resources;

namespace SecRandom.Views.SettingsPages.Picking;

/// <summary>
///     编排抽取总览：一份编排名单 + 它的五份名单（排斥 / 权重 / 延后 / 顺序 / 必中）。
/// </summary>
/// <remarks>
///     <para>
///         This page owns only what is common to the five lists: which arranged profile is being edited, which
///         round it is being edited for, whether the bound channel actually draws in orchestrated mode, and which
///         lists are switched on. It deliberately carries no member table — mixing five unrelated lists into one
///         grid was what made 「顺序」 and 「必中」 look like they could be set on the same member at once.
///     </para>
///     <para>
///         Each list has its own settings page (<see cref="OrchestrationEditingSession" /> carries the profile and
///         round across), and every edit is written straight back through <see cref="IOrchestrationStore" />,
///         because the draw reads the file, not this page. The page therefore reloads the selected profile when it
///         is attached again, so returning from a list page always shows the numbers that were just edited.
///     </para>
/// </remarks>
[PageInfo("settings.picking.orchestration", FluentIcons.ClipboardBulletListFilled, "settings.picking")]
public partial class OrchestrationSettingsPage : UserControl, INotifyPropertyChanged
{
    private readonly IOrchestrationStore _store = IAppHost.GetService<IOrchestrationStore>();
    private readonly IProfileCatalogManager _catalog = IAppHost.GetService<IProfileCatalogManager>();
    private readonly MainConfigHandler _config = IAppHost.GetService<MainConfigHandler>();

    private OrchestrationProfile? _profile;
    private OrchestrationProfileItem? _selectedProfileItem;
    private int _selectedRound = 1;
    private int _runtimeRound = 1;
    private double _roundCount = 1;
    private event PropertyChangedEventHandler? NotifyPropertyChanged;

    public OrchestrationSettingsPage()
    {
        DataContext = this;
        InitializeComponent();
        LoadProfiles();
    }

    public ObservableCollection<OrchestrationProfileItem> Profiles { get; } = [];

    public ObservableCollection<int> RoundOptions { get; } = [];

    public bool HasAnyProfile => Profiles.Count > 0;

    public bool HasProfile => _profile is not null;

    public OrchestrationProfileItem? SelectedProfileItem
    {
        get => _selectedProfileItem;
        set
        {
            if (ReferenceEquals(_selectedProfileItem, value))
                return;
            _selectedProfileItem = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentLabel));
            LoadSelectedProfile();
        }
    }

    public string CurrentLabel => SelectedProfileItem?.DisplayName ?? PR.C_Orchestration_Empty;

    public string ActivateLabel =>
        _profile?.IsActive == true ? PR.C_Orchestration_Disable : PR.C_Orchestration_Enable;

    /// <summary>「点名 | 一班 | 45 人」：名单绑定和目标通道一眼可见，不必打开名单设置才知道。</summary>
    public string ProfileSummary
    {
        get
        {
            if (_profile is null)
                return string.Empty;
            var target = _profile.Target == OrchestrationTarget.Lottery
                ? PR.O_Orchestration_TargetLottery
                : PR.O_Orchestration_TargetRollCall;
            return string.Format(PR.C_Orchestration_ProfileSummary, target, _profile.SourceListName,
                _profile.Members.Count);
        }
    }

    /// <summary>
    ///     抽取进行到第几轮。它和上面「编辑哪一轮」是两件事：抽取端推进的是运行时游标，
    ///     用户在这里翻看的是要修改的那一轮。
    /// </summary>
    public string ProgressText => string.Format(PR.S_Orchestration_Progress, _runtimeRound);

    /// <summary>
    ///     这份名单对应的抽取通道现在是不是编排抽取。抽取端读的是 <c>Effective*Settings</c>（简洁模式下按默认值），
    ///     所以这里也用同一个入口，页面上说的和真正抽取时用的一致。
    /// </summary>
    private DrawMode ChannelDrawMode => _profile?.Target == OrchestrationTarget.Lottery
        ? _config.Data.EffectiveLotterySettings.DrawMode
        : _config.Data.EffectiveRollCallSettings.DrawMode;

    private bool ChannelIsOrchestrated => DrawModeSelection.IsOrchestrated(ChannelDrawMode);

    public bool CanSwitchChannel => _profile is not null && !ChannelIsOrchestrated;

    public bool IsProfileInactive => _profile is { IsActive: false };

    /// <summary>「点名抽取方式：已是编排抽取…」——把通道状态和名单绑定状态一起说清楚，而不是只让按钮亮着。</summary>
    public string ChannelStatusText
    {
        get
        {
            if (_profile is null)
                return string.Empty;

            var channel = _profile.Target == OrchestrationTarget.Lottery
                ? PR.S_Orchestration_ChannelLottery
                : PR.S_Orchestration_ChannelRollCall;
            var state = ChannelIsOrchestrated
                ? PR.S_Orchestration_ChannelOn
                : PR.S_Orchestration_ChannelOff;
            return $"{channel}：{state}";
        }
    }

    public double RoundCount
    {
        get => _roundCount;
        set
        {
            var clamped = Math.Clamp(value, 1, OrchestrationProfile.MaxRounds);
            if (Math.Abs(clamped - _roundCount) < 0.0001 && _profile is null)
            {
                _roundCount = clamped;
                return;
            }

            _roundCount = clamped;
            OnPropertyChanged();
            if (_profile is null)
                return;

            _profile.RoundCount = (int)clamped;
            SaveProfile();
            RefreshRoundOptions();
            if (_selectedRound > _profile.EffectiveRoundCount)
                SelectedRound = _profile.EffectiveRoundCount;
            else
                RefreshSummaries();
        }
    }

    public int SelectedRound
    {
        get => _selectedRound;
        set
        {
            var clamped = Math.Max(1, value);
            if (_selectedRound == clamped)
                return;
            _selectedRound = clamped;
            OnPropertyChanged();
            RefreshSummaries();
        }
    }

    #region 五份名单的开关

    public bool ExcludeRuleEnabled
    {
        get => _profile?.Rules.Exclude == true;
        set => SetRule(OrchestrationRuleKind.Exclude, value);
    }

    public bool WeightRuleEnabled
    {
        get => _profile?.Rules.Weight == true;
        set => SetRule(OrchestrationRuleKind.Weight, value);
    }

    public bool DeferRuleEnabled
    {
        get => _profile?.Rules.Defer == true;
        set => SetRule(OrchestrationRuleKind.Defer, value);
    }

    public bool OrderRuleEnabled
    {
        get => _profile?.Rules.Order == true;
        set => SetRule(OrchestrationRuleKind.Order, value);
    }

    public bool GuaranteeRuleEnabled
    {
        get => _profile?.Rules.Guarantee == true;
        set => SetRule(OrchestrationRuleKind.Guarantee, value);
    }

    /// <summary>
    ///     一个开关只改这一份名单参不参与排序。顺带把「顺序」和「必中」的互斥落在这里——
    ///     <see cref="OrchestrationRules.SetEnabled" /> 是那条规则的唯一入口，界面不会出现两个都亮着。
    /// </summary>
    private async void SetRule(OrchestrationRuleKind kind, bool enabled)
    {
        if (_profile is null || SettingsView.Current?.IsPreviewMode == true)
        {
            NotifyRuleSwitches();
            return;
        }

        // 顺序与必中是一对：打开一个就会关掉另一个，先把这一步说出来再动手。
        var conflicting = kind switch
        {
            OrchestrationRuleKind.Order => _profile.Rules.Guarantee ? OrchestrationRuleKind.Guarantee : (OrchestrationRuleKind?)null,
            OrchestrationRuleKind.Guarantee => _profile.Rules.Order ? OrchestrationRuleKind.Order : (OrchestrationRuleKind?)null,
            _ => null
        };
        if (enabled && conflicting is { } other)
        {
            var result = await new FAContentDialog
            {
                Title = PR.M_Rule_ConflictSwitchTitle,
                Content = string.Format(PR.M_Rule_ConflictSwitchContent, RuleTitle(kind), RuleTitle(other)),
                PrimaryButtonText = PR.C_Rule_Continue,
                CloseButtonText = PR.C_Orchestration_Cancel,
                DefaultButton = FAContentDialogButton.Close
            }.ShowAsync(TopLevel.GetTopLevel(this));
            if (result != FAContentDialogResult.Primary)
            {
                NotifyRuleSwitches();
                return;
            }
        }

        _profile.Rules.SetEnabled(kind, enabled);
        SaveProfile();
        NotifyRuleSwitches();
        RefreshSummaries();
    }

    private static string RuleTitle(OrchestrationRuleKind kind) => kind switch
    {
        OrchestrationRuleKind.Exclude => PR.S_Orchestration_Exclude,
        OrchestrationRuleKind.Weight => PR.S_Orchestration_Weight,
        OrchestrationRuleKind.Defer => PR.S_Orchestration_Defer,
        OrchestrationRuleKind.Order => PR.S_Orchestration_Order,
        _ => PR.S_Orchestration_Guarantee
    };

    private void NotifyRuleSwitches()
    {
        OnPropertyChanged(nameof(ExcludeRuleEnabled));
        OnPropertyChanged(nameof(WeightRuleEnabled));
        OnPropertyChanged(nameof(DeferRuleEnabled));
        OnPropertyChanged(nameof(OrderRuleEnabled));
        OnPropertyChanged(nameof(GuaranteeRuleEnabled));
    }

    #endregion

    #region 每份名单卡上的当前状态

    public string ExcludeSummary =>
        _profile is null
            ? string.Empty
            : _profile.Rules.Exclude
                ? string.Format(PR.C_Orchestration_ExcludeSummary,
                    _profile.Members.Count(member => member.IsExcluded))
                : PR.C_Orchestration_RuleOff;

    public string WeightSummary =>
        _profile is null
            ? string.Empty
            : _profile.Rules.Weight
                ? string.Format(PR.C_Orchestration_WeightSummary, Round().Weights.Count)
                : PR.C_Orchestration_RuleOff;

    public string DeferSummary =>
        _profile is null
            ? string.Empty
            : _profile.Rules.Defer
                ? string.Format(PR.C_Orchestration_DeferSummary, Round().Skipped.Count)
                : PR.C_Orchestration_RuleOff;

    public string OrderSummary
    {
        get
        {
            if (_profile is null)
                return string.Empty;
            if (!_profile.Rules.Order)
                return PR.C_Orchestration_RuleOff;
            var length = Round().Order.Count;
            return length == 0
                ? PR.C_Orchestration_OrderSummaryEmpty
                : string.Format(PR.C_Orchestration_OrderSummary, length);
        }
    }

    public string GuaranteeSummary =>
        _profile is null
            ? string.Empty
            : _profile.Rules.Guarantee
                ? string.Format(PR.C_Orchestration_GuaranteeSummary, Round().Guaranteed.Count)
                : PR.C_Orchestration_RuleOff;

    private OrchestrationRound Round() =>
        _profile?.GetRound(_selectedRound) ?? new OrchestrationRound { Index = _selectedRound };

    private void RefreshSummaries()
    {
        OnPropertyChanged(nameof(ExcludeSummary));
        OnPropertyChanged(nameof(WeightSummary));
        OnPropertyChanged(nameof(DeferSummary));
        OnPropertyChanged(nameof(OrderSummary));
        OnPropertyChanged(nameof(GuaranteeSummary));
    }

    #endregion

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => NotifyPropertyChanged += value;
        remove => NotifyPropertyChanged -= value;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e) => OnPropertyChanged(nameof(ProgressText));

    /// <summary>
    ///     从五份名单的设置页回来时重新读盘：那些页面各自保存，这里不重读就会一直显示进去之前的条数。
    ///     用选的轮次覆盖 LoadProfiles 的默认（运行时游标）轮次，免得回来一次就跳走。
    /// </summary>
    private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        var round = _selectedRound;
        LoadProfiles(_profile?.Id);
        if (_profile is not null && round != _selectedRound)
            SelectedRound = Math.Min(round, _profile.EffectiveRoundCount);
    }

    private void LoadProfiles(string? selectId = null)
    {
        var previousId = selectId ?? _profile?.Id;
        Profiles.Clear();
        foreach (var profile in _store.LoadAll())
            Profiles.Add(new OrchestrationProfileItem(profile));
        OnPropertyChanged(nameof(HasAnyProfile));
        OnPropertyChanged(nameof(CurrentLabel));

        var target = previousId is null
            ? Profiles.FirstOrDefault()
            : Profiles.FirstOrDefault(item => item.Profile.Id == previousId) ?? Profiles.FirstOrDefault();
        _selectedProfileItem = target;
        OnPropertyChanged(nameof(SelectedProfileItem));
        OnPropertyChanged(nameof(CurrentLabel));
        LoadSelectedProfile();
    }

    private void LoadSelectedProfile()
    {
        _profile = SelectedProfileItem?.Profile;
        OnPropertyChanged(nameof(HasProfile));
        OnPropertyChanged(nameof(ActivateLabel));
        OnPropertyChanged(nameof(ProfileSummary));
        NotifyChannelState();
        NotifyRuleSwitches();
        if (_profile is null)
        {
            RoundOptions.Clear();
            RefreshSummaries();
            return;
        }

        var runtime = _store.GetRuntime(_profile);
        _runtimeRound = runtime.Round;
        _selectedRound = runtime.Round;
        _roundCount = _profile.EffectiveRoundCount;
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(RoundCount));
        RefreshRoundOptions();
        OnPropertyChanged(nameof(SelectedRound));
        RefreshSummaries();
    }

    private void RefreshRoundOptions()
    {
        if (_profile is null)
        {
            RoundOptions.Clear();
            return;
        }

        var count = _profile.EffectiveRoundCount;
        if (RoundOptions.Count != count)
        {
            RoundOptions.Clear();
            for (var index = 1; index <= count; index++)
                RoundOptions.Add(index);
        }

        if (_selectedRound > count)
            _selectedRound = count;
    }

    private void NotifyChannelState()
    {
        OnPropertyChanged(nameof(ChannelStatusText));
        OnPropertyChanged(nameof(CanSwitchChannel));
        OnPropertyChanged(nameof(IsProfileInactive));
    }

    private void SaveProfile()
    {
        if (_profile is null)
            return;
        _profile.Members ??= [];
        _store.Save(_profile);
        OnPropertyChanged(nameof(ActivateLabel));
        OnPropertyChanged(nameof(ProfileSummary));
    }

    #region 编排名单抽屉

    private void CurrentProfileButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var control = (Control)Resources["CurrentProfileDrawer"]!;
        control.DataContext = this;
        SettingsView.Current?.OpenDrawer(control);
    }

    private void NewButton_OnClick(object? sender, RoutedEventArgs e) =>
        SettingsView.Current?.OpenDrawer(new OrchestrationEditorDrawer(null, ApplyNewProfile));

    private void EditProfileButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_profile is null)
            return;
        SettingsView.Current?.OpenDrawer(new OrchestrationEditorDrawer(_profile, ApplyProfileEdit));
    }

    private void ApplyNewProfile(OrchestrationEditorResult result)
    {
        var profile = new OrchestrationProfile
        {
            Id = OrchestrationStore.NewId(),
            Name = result.Name,
            Target = result.Target,
            SourceListName = result.SourceListName,
            IsActive = false,
            RoundCount = result.RoundCount
        };
        // 新建就带入来源名单的全部成员：否则打开是一张空表，还得再点一次「同步成员」才有人。
        MergeSourceMembers(profile);
        _store.Save(profile);
        SettingsView.Current?.CloseDrawer();
        LoadProfiles(profile.Id);
        this.ShowSuccessToast(string.Format(PR.M_Orchestration_Created, profile.Name));
    }

    private void ApplyProfileEdit(OrchestrationEditorResult result)
    {
        if (_profile is null)
            return;

        var rebound = _profile.Target != result.Target ||
                      !string.Equals(_profile.SourceListName, result.SourceListName, StringComparison.Ordinal);
        _profile.Name = result.Name;
        _profile.Target = result.Target;
        _profile.SourceListName = result.SourceListName;
        _profile.RoundCount = result.RoundCount;
        if (rebound)
            MergeSourceMembers(_profile);
        _store.Save(_profile);
        SettingsView.Current?.CloseDrawer();
        LoadProfiles(_profile.Id);
        this.ShowSuccessToast(string.Format(PR.M_Orchestration_Saved, _profile.Name));
    }

    private async void DeleteButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_profile is null)
            return;

        var name = _profile.Name;
        var result = await new FAContentDialog
        {
            Title = PR.M_Orchestration_DeleteTitle,
            Content = string.Format(PR.M_Orchestration_DeleteContent, name),
            PrimaryButtonText = PR.M_Orchestration_DeletePrimary,
            CloseButtonText = PR.C_Cancel,
            DefaultButton = FAContentDialogButton.Close
        }.ShowAsync(TopLevel.GetTopLevel(this));
        if (result != FAContentDialogResult.Primary)
            return;

        _store.Delete(_profile.Id);
        _profile = null;
        LoadProfiles();
        this.ShowSuccessToast(string.Format(PR.M_Orchestration_Deleted, name));
    }

    #endregion

    private void ToggleActiveButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_profile is null)
            return;

        var enable = !_profile.IsActive;
        if (enable)
        {
            // One active profile per (channel, source list): the draw looks up exactly one.
            foreach (var other in _store.FindActive(_profile.Target, _profile.SourceListName))
            {
                if (other.Id == _profile.Id)
                    continue;
                other.IsActive = false;
                _store.Save(other);
            }
        }

        _profile.IsActive = enable;
        SaveProfile();
        RefreshProfileItem();
        NotifyChannelState();
        this.ShowSuccessToast(string.Format(
            PR.M_Orchestration_ActiveChanged,
            enable ? PR.C_Orchestration_Enable : PR.C_Orchestration_Disable,
            _profile.Name));
    }

    /// <summary>
    ///     把这份名单所属通道的抽取方式切到编排抽取。名单只有在通道选中编排抽取时才会被抽取端读取，
    ///     于是在这里直接给按钮，用户不必再去「点名/抽奖抽取设置」翻一遍才知道差这一步。
    ///     走 <see cref="DrawModeSelection" /> 而不是直接写 <see cref="DrawMode" />：进入编排前先把重复策略记下来，
    ///     用户之后在「抽取方式」里改回抽样算法时才能恢复它。
    /// </summary>
    private void UseOrchestratedButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_profile is null || SettingsView.Current?.IsPreviewMode == true)
            return;

        if (_profile.Target == OrchestrationTarget.Lottery)
            DrawModeSelection.SelectOrchestrated(_config.Data.LotterySettings);
        else
            DrawModeSelection.SelectOrchestrated(_config.Data.RollCallSettings);

        _config.Save();
        NotifyChannelState();
        this.ShowSuccessToast(PR.M_Orchestration_ChannelSwitched);
    }

    private void SyncButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_profile is null)
            return;

        MergeSourceMembers(_profile);
        SaveProfile();
        RefreshProfileItem();
        RefreshSummaries();
        this.ShowSuccessToast(string.Format(PR.M_Orchestration_Synced, _profile.Name));
    }

    /// <summary>
    ///     Adds every member of the bound source list that the profile does not carry yet, and marks carried members
    ///     whose record disappeared from the list instead of silently dropping them (their arrangement would vanish).
    /// </summary>
    private void MergeSourceMembers(OrchestrationProfile profile)
    {
        if (profile.Target == OrchestrationTarget.Lottery)
        {
            var pool = _catalog.LoadPrizeList(profile.SourceListName);
            if (pool is null)
                return;

            foreach (var prize in pool.Prizes.Where(prize => prize.Exists))
            {
                if (prize.RecordId == Guid.Empty)
                    ProfileRecordIdentity.EnsureRecordId(prize);
                if (profile.FindMember(prize.RecordId) is not null)
                    continue;
                profile.Members.Add(new OrchestrationMember
                {
                    RecordId = prize.RecordId,
                    Number = prize.Id,
                    Name = prize.Name
                });
            }

            var poolIds = pool.Prizes.Select(prize => prize.RecordId).ToHashSet();
            foreach (var member in profile.Members)
                member.IsSourceMissing = !poolIds.Contains(member.RecordId);
            return;
        }

        var list = _catalog.LoadStudentList(profile.SourceListName);
        if (list is null)
            return;

        foreach (var student in list.Students.Where(student => student.IsCandidate))
        {
            if (student.RecordId == Guid.Empty)
                ProfileRecordIdentity.EnsureRecordId(student);
            if (profile.FindMember(student.RecordId) is not null)
                continue;
            profile.Members.Add(new OrchestrationMember
            {
                RecordId = student.RecordId,
                Number = student.Id,
                Name = student.Name
            });
        }

        var listIds = list.Students.Select(student => student.RecordId).ToHashSet();
        foreach (var member in profile.Members)
            member.IsSourceMissing = !listIds.Contains(member.RecordId);
    }

    /// <summary>
    ///     Opens the settings page of one list. The page opens on the profile and round this page is showing; the
    ///     page itself re-reads both, because a profile edited here must never be edited from a stale object.
    /// </summary>
    private void OpenRuleButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: string kind } || _profile is null)
            return;
        if (!Enum.TryParse<OrchestrationRuleKind>(kind, out var rule))
            return;

        OrchestrationEditingSession.Open(_profile.Id, _selectedRound);
        SettingsView.Current?.NavigateToPage(OrchestrationEditingSession.PageIdOf(rule));
    }

    private void RefreshProfileItem()
    {
        if (SelectedProfileItem is null || _profile is null)
            return;
        var updated = new OrchestrationProfileItem(_profile);
        var index = Profiles.IndexOf(SelectedProfileItem);
        if (index < 0)
            return;
        Profiles[index] = updated;
        _selectedProfileItem = updated;
        OnPropertyChanged(nameof(SelectedProfileItem));
        OnPropertyChanged(nameof(CurrentLabel));
        OnPropertyChanged(nameof(ActivateLabel));
        OnPropertyChanged(nameof(ProfileSummary));
        NotifyChannelState();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>ComboBox row for one arranged profile; the display text states channel and source, so duplicates differ.</summary>
public sealed class OrchestrationProfileItem(OrchestrationProfile profile)
{
    public OrchestrationProfile Profile { get; } = profile;

    public string DisplayName
    {
        get
        {
            var target = Profile.Target == OrchestrationTarget.Lottery
                ? PR.O_Orchestration_TargetLottery
                : PR.O_Orchestration_TargetRollCall;
            var active = Profile.IsActive ? " | " + PR.C_Orchestration_Active : string.Empty;
            return $"{Profile.Name}（{target} | {Profile.SourceListName}{active}）";
        }
    }

    public override string ToString() => DisplayName;
}