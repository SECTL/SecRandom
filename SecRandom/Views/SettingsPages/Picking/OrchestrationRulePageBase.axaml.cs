using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.Orchestration;
using SecRandom.Core.Services.Orchestration;
using PR = SecRandom.Langs.SettingsPages.Picking.Resources;

namespace SecRandom.Views.SettingsPages.Picking;

/// <summary>
///     One of the five list settings pages: 排斥 / 权重 / 延后 / 顺序 / 必中.
/// </summary>
/// <remarks>
///     <para>
///         All five are the same screen with the same member rows, because the difference between them is a rule,
///         not a layout: what a check means (「不参加」「本轮先不抽」「进队列」「排最前」) and whether the row carries a
///         weight box or the queue's up/down controls. Splitting them into five pages is what stops one member's
///         five unrelated settings from sitting side by side in one grid, where flipping 顺序 and 必中 at once looked
///         like a valid state.
///     </para>
///     <para>
///         The page is hidden from the settings navigation and reached only from the arrangement overview
///         (<see cref="OrchestrationEditingSession" /> carries the profile and round). It re-reads the profile from
///         the store on every attach and saves immediately on each edit, because a draw reads the file.
///     </para>
/// </remarks>
public abstract partial class OrchestrationRulePageBase : UserControl, INotifyPropertyChanged
{
    private readonly IOrchestrationStore _store = IAppHost.GetService<IOrchestrationStore>();
    private OrchestrationProfile? _profile;
    private OrchestrationRound _round = new();
    private int _selectedRound = 1;
    private string _searchText = string.Empty;
    private event PropertyChangedEventHandler? NotifyPropertyChanged;

    protected OrchestrationRulePageBase(OrchestrationRuleKind kind)
    {
        Kind = kind;
        DataContext = this;
        InitializeComponent();
        Load();
    }

    public OrchestrationRuleKind Kind { get; }

    public ObservableCollection<OrchestrationRuleMemberRow> Rows { get; } = [];

    public ObservableCollection<int> RoundOptions { get; } = [];

    public bool HasRows => Rows.Count > 0;

    public bool IsRoundScoped => Kind != OrchestrationRuleKind.Exclude;

    public bool IsExcludePage => Kind == OrchestrationRuleKind.Exclude;

    public bool IsWeightPage => Kind == OrchestrationRuleKind.Weight;

    public bool IsDeferPage => Kind == OrchestrationRuleKind.Defer;

    public bool IsOrderPage => Kind == OrchestrationRuleKind.Order;

    public bool IsGuaranteePage => Kind == OrchestrationRuleKind.Guarantee;

    public string HeaderGlyph => Kind switch
    {
        OrchestrationRuleKind.Exclude => FluentIcons.ProhibitedFilled,
        OrchestrationRuleKind.Weight => FluentIcons.ScalesFilled,
        OrchestrationRuleKind.Defer => FluentIcons.TimerFilled,
        OrchestrationRuleKind.Order => FluentIcons.ArrowSortFilled,
        _ => FluentIcons.TargetFilled
    };

    public string PageTitle => Kind switch
    {
        OrchestrationRuleKind.Exclude => PR.S_Orchestration_Exclude,
        OrchestrationRuleKind.Weight => PR.S_Orchestration_Weight,
        OrchestrationRuleKind.Defer => PR.S_Orchestration_Defer,
        OrchestrationRuleKind.Order => PR.S_Orchestration_Order,
        _ => PR.S_Orchestration_Guarantee
    };

    public string RuleHint => Kind switch
    {
        OrchestrationRuleKind.Exclude => PR.C_Rule_ExcludeHint_D,
        OrchestrationRuleKind.Weight => PR.C_Rule_WeightHint_D,
        OrchestrationRuleKind.Defer => PR.C_Rule_DeferHint_D,
        OrchestrationRuleKind.Order => PR.C_Rule_OrderHint_D,
        _ => PR.C_Rule_GuaranteeHint_D
    };

    public string HeaderDetail
    {
        get
        {
            if (_profile is null)
                return PR.C_Rule_NoProfile;

            var target = _profile.Target == OrchestrationTarget.Lottery
                ? PR.O_Orchestration_TargetLottery
                : PR.O_Orchestration_TargetRollCall;
            return string.Format(PR.C_Rule_Profile, _profile.Name, target, _profile.SourceListName);
        }
    }

    /// <summary>这份名单开关没开时，内容仍然可以编辑，但抽取端不会用它。</summary>
    public bool IsRuleDisabled => _profile is not null && !_profile.Rules.IsEnabled(Kind);

    public bool CanEdit => _profile is not null && SettingsView.Current?.IsPreviewMode != true;

    public string CountText => _profile is null
        ? string.Empty
        : string.Format(PR.S_Rule_Count, _profile.Members.Count, CountInList());

    public string EmptyState
    {
        get
        {
            if (_profile is null)
                return PR.C_Rule_NoProfile_D;
            if (_profile.Members.Count == 0)
                return PR.C_Rule_NoMembers_D;
            return PR.C_Rule_NoMatch_D;
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            _searchText = value ?? string.Empty;
            OnPropertyChanged();
            RebuildRows();
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
            SelectRound();
        }
    }

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => NotifyPropertyChanged += value;
        remove => NotifyPropertyChanged -= value;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e) => Load();

    /// <summary>
    ///     Re-reads the arranged profile the overview handed over. A missing profile (deleted, or the page opened
    ///     directly) is not an error: the page shows "choose a list first" instead of editing nothing.
    /// </summary>
    private void Load()
    {
        _profile = OrchestrationEditingSession.ProfileId is { } id ? _store.Load(id) : null;
        _selectedRound = _profile is null
            ? 1
            : Math.Clamp(OrchestrationEditingSession.Round, 1, _profile.EffectiveRoundCount);
        RefreshRoundOptions();
        SelectRound();
    }

    private void SelectRound()
    {
        Rows.Clear();
        if (_profile is not null)
        {
            _round = _profile.GetRound(_selectedRound);
            RebuildRows();
        }

        NotifyAll();
    }

    private void RefreshRoundOptions()
    {
        RoundOptions.Clear();
        if (_profile is null)
            return;
        for (var index = 1; index <= _profile.EffectiveRoundCount; index++)
            RoundOptions.Add(index);
    }

    /// <summary>
    ///     Builds the visible rows. 顺序 and 必中 are ordered lists, so whoever is on the list comes first in list
    ///     order and everyone else follows as "not on this list yet"; the other lists keep the member order.
    /// </summary>
    private void RebuildRows()
    {
        Rows.Clear();
        if (_profile is null)
            return;

        var members = _profile.Members
            .Where(member => Matches(member))
            .ToList();

        if (Kind is OrchestrationRuleKind.Order or OrchestrationRuleKind.Guarantee)
        {
            var list = Kind == OrchestrationRuleKind.Order ? _round.Order : _round.Guaranteed;
            var ordered = new List<OrchestrationMember>();
            foreach (var recordId in list)
            {
                var member = members.FirstOrDefault(candidate => candidate.RecordId == recordId);
                if (member is not null)
                    ordered.Add(member);
            }

            ordered.AddRange(members.Where(member => !list.Contains(member.RecordId)));
            members = ordered;
        }

        for (var index = 0; index < members.Count; index++)
        {
            var row = new OrchestrationRuleMemberRow(Kind, _profile, _round, members[index], OnRowChanged);
            row.SetPosition(ResolvePosition(row.Member.RecordId), CanMove(row, -1), CanMove(row, 1));
            Rows.Add(row);
        }

        OnPropertyChanged(nameof(HasRows));
    }

    private bool Matches(OrchestrationMember member)
    {
        if (string.IsNullOrWhiteSpace(_searchText))
            return true;
        return member.Number.Contains(_searchText, StringComparison.CurrentCultureIgnoreCase)
               || member.Name.Contains(_searchText, StringComparison.CurrentCultureIgnoreCase);
    }

    private int ResolvePosition(Guid recordId)
    {
        var list = Kind == OrchestrationRuleKind.Order ? _round.Order : _round.Guaranteed;
        if (Kind is not (OrchestrationRuleKind.Order or OrchestrationRuleKind.Guarantee))
            return 0;
        var index = list.IndexOf(recordId);
        return index < 0 ? 0 : index + 1;
    }

    private bool CanMove(OrchestrationRuleMemberRow row, int delta)
    {
        if (Kind is not (OrchestrationRuleKind.Order or OrchestrationRuleKind.Guarantee))
            return false;
        var list = Kind == OrchestrationRuleKind.Order ? _round.Order : _round.Guaranteed;
        var index = list.IndexOf(row.Member.RecordId);
        if (index < 0)
            return delta > 0; // 还没进名单的人可以「往上」加入；已在名单里的才谈得上移动
        var target = index + delta;
        return target >= 0 && target < list.Count;
    }

    private int CountInList() => Kind switch
    {
        OrchestrationRuleKind.Exclude => _profile?.Members.Count(member => member.IsExcluded) ?? 0,
        OrchestrationRuleKind.Weight => _round.Weights.Count,
        OrchestrationRuleKind.Defer => _round.Skipped.Count,
        OrchestrationRuleKind.Order => _round.Order.Count,
        _ => _round.Guaranteed.Count
    };

    private void OnRowChanged()
    {
        Save();
        RebuildRows();
        NotifyAll();
    }

    private void Save()
    {
        if (_profile is not null)
            _store.Save(_profile);
    }

    /// <summary>
    ///     勾选/取消勾选一名成员。取消勾选不会冲突，直接生效；勾选前先问一句「这会取消哪份名单」，
    ///     因为这份名单和别的名单互斥的地方都在模型里（排斥清全局、延后清顺序与必中、顺序清必中……），
    ///     让用户看着自己按下的这一下会撤掉什么，而不是保存完才发现另一页空了。
    /// </summary>
    private async void Toggle_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox box || box.DataContext is not OrchestrationRuleMemberRow row)
            return;

        var wanted = box.IsChecked == true;
        if (wanted && _profile is not null)
        {
            var cancelled = DescribeCancelledLists(row.Member.RecordId);
            if (cancelled.Count > 0 && !await ConfirmCancelAsync(cancelled))
            {
                // 绑定是 OneWay，用户点出来的一下要自己退回去，否则界面会说谎。
                box.IsChecked = row.IsInList;
                return;
            }
        }

        row.IsInList = wanted;
    }

    /// <summary>勾上这一行会顺手取消的其他名单，空表示没有互斥冲突，不该打扰用户。</summary>
    private List<string> DescribeCancelledLists(Guid recordId)
    {
        var cancelled = new List<string>();
        if (_profile is null)
            return cancelled;

        switch (Kind)
        {
            case OrchestrationRuleKind.Exclude:
                // 排斥是全局的：每一轮的顺序、延后、必中、权重都会随这一勾消失。
                foreach (var round in _profile.Rounds)
                    CollectConflicts(cancelled, round, recordId, defer: true, order: true, guarantee: true, weight: true);
                break;

            case OrchestrationRuleKind.Defer:
                if (_profile.FindMember(recordId)?.IsExcluded == true)
                    cancelled.Add(PR.S_Orchestration_Exclude);
                CollectConflicts(cancelled, _round, recordId, defer: false, order: true, guarantee: true, weight: false);
                break;

            case OrchestrationRuleKind.Order:
                CollectConflicts(cancelled, _round, recordId, defer: false, order: false, guarantee: true, weight: false);
                break;

            case OrchestrationRuleKind.Guarantee:
                CollectConflicts(cancelled, _round, recordId, defer: true, order: true, guarantee: false, weight: false);
                break;
        }

        return cancelled;
    }

    private static void CollectConflicts(
        List<string> target,
        OrchestrationRound round,
        Guid recordId,
        bool defer,
        bool order,
        bool guarantee,
        bool weight)
    {
        if (defer && round.Skipped.Contains(recordId))
            target.Add(string.Format(PR.M_Rule_ConflictRound, round.Index, PR.S_Orchestration_Defer));
        if (order && round.Order.Contains(recordId))
            target.Add(string.Format(PR.M_Rule_ConflictRound, round.Index, PR.S_Orchestration_Order));
        if (guarantee && round.Guaranteed.Contains(recordId))
            target.Add(string.Format(PR.M_Rule_ConflictRound, round.Index, PR.S_Orchestration_Guarantee));
        if (weight && round.Weights.ContainsKey(recordId))
            target.Add(string.Format(PR.M_Rule_ConflictRound, round.Index, PR.S_Orchestration_Weight));
    }

    private async Task<bool> ConfirmCancelAsync(IReadOnlyList<string> cancelled)
    {
        var result = await new FAContentDialog
        {
            Title = PR.M_Rule_ConflictTitle,
            Content = string.Format(PR.M_Rule_ConflictContent, PageTitle, string.Join(PR.C_Rule_ConflictJoin, cancelled)),
            PrimaryButtonText = PR.C_Rule_Continue,
            CloseButtonText = PR.C_Orchestration_Cancel,
            DefaultButton = FAContentDialogButton.Close
        }.ShowAsync(TopLevel.GetTopLevel(this));
        return result == FAContentDialogResult.Primary;
    }

    private async Task MoveAsync(OrchestrationRuleMemberRow row, int delta)
    {
        if (_profile is null)
            return;

        var list = Kind == OrchestrationRuleKind.Order ? _round.Order : _round.Guaranteed;
        var recordId = row.Member.RecordId;
        var index = list.IndexOf(recordId);
        if (index < 0)
        {
            // 不在名单里的人：往上按一下等于排到队尾，和勾选一样要过互斥确认。
            if (delta <= 0)
                return;

            var cancelled = DescribeCancelledLists(recordId);
            if (cancelled.Count > 0 && !await ConfirmCancelAsync(cancelled))
                return;

            list.Add(recordId);
        }
        else
        {
            var target = index + delta;
            if (target < 0 || target >= list.Count)
                return;
            (list[index], list[target]) = (list[target], list[index]);
        }

        // 顺序名单的队列要按成员集合过滤一遍：直接改 List 会绕过 SetOrder 的去重与必中互斥。
        if (Kind == OrchestrationRuleKind.Order)
            _profile.SetOrder(_round, list);
        Save();
        RebuildRows();
        NotifyAll();
    }

    private async void MoveUpButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: OrchestrationRuleMemberRow row })
            await MoveAsync(row, -1);
    }

    private async void MoveDownButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: OrchestrationRuleMemberRow row })
            await MoveAsync(row, 1);
    }

    /// <summary>只对顺序名单有意义：本轮参与者的队列随机重排。必中名单的顺序由权重和加入次序决定。</summary>
    private async void ShuffleButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_profile is null || Kind != OrchestrationRuleKind.Order)
            return;

        if (_round.Guaranteed.Count > 0 && !await ConfirmReplaceAsync(PR.M_Rule_ShuffleConflict))
            return;

        var ids = _profile.Members
            .Where(member => !member.IsExcluded && !_round.Skipped.Contains(member.RecordId))
            .Select(member => member.RecordId)
            .ToArray();
        for (var index = ids.Length - 1; index > 0; index--)
        {
            var swap = Random.Shared.Next(index + 1);
            (ids[index], ids[swap]) = (ids[swap], ids[index]);
        }

        _profile.SetOrder(_round, ids);
        Save();
        RebuildRows();
        NotifyAll();
        this.ShowSuccessToast(PR.M_Rule_Shuffled);
    }

    private async void CopyOrderButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_profile is null || Kind != OrchestrationRuleKind.Order || _round.Order.Count == 0)
            return;

        var otherRounds = Enumerable
            .Range(1, _profile.EffectiveRoundCount)
            .Where(index => index != _selectedRound)
            .Select(_profile.GetRound)
            .ToArray();
        if (otherRounds.Any(round => round.Guaranteed.Count > 0)
            && !await ConfirmReplaceAsync(PR.M_Rule_CopyConflict))
            return;

        var sequence = _round.Order.ToList();
        foreach (var round in otherRounds)
            _profile.SetOrder(round, sequence);

        Save();
        this.ShowSuccessToast(string.Format(PR.M_Rule_OrderCopied, otherRounds.Length));
    }

    private async Task<bool> ConfirmReplaceAsync(string content)
    {
        var result = await new FAContentDialog
        {
            Title = PR.M_Rule_ConflictTitle,
            Content = content,
            PrimaryButtonText = PR.C_Rule_ReplaceOrder,
            CloseButtonText = PR.C_Orchestration_Cancel,
            DefaultButton = FAContentDialogButton.Close
        }.ShowAsync(TopLevel.GetTopLevel(this));
        return result == FAContentDialogResult.Primary;
    }

    /// <summary>清空当前这一份名单——不动其他四份，也不动成员本身。</summary>
    private void ClearListButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_profile is null)
            return;

        switch (Kind)
        {
            case OrchestrationRuleKind.Exclude:
                foreach (var member in _profile.Members)
                    member.IsExcluded = false;
                break;
            case OrchestrationRuleKind.Weight:
                _round.Weights.Clear();
                break;
            case OrchestrationRuleKind.Defer:
                _round.Skipped.Clear();
                break;
            case OrchestrationRuleKind.Order:
                _profile.SetOrder(_round, []);
                break;
            case OrchestrationRuleKind.Guarantee:
                _round.Guaranteed.Clear();
                break;
        }

        Save();
        RebuildRows();
        NotifyAll();
        this.ShowSuccessToast(string.Format(PR.M_Rule_Cleared, PageTitle));
    }

    private void NotifyAll()
    {
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(IsRuleDisabled));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(HeaderDetail));
        OnPropertyChanged(nameof(EmptyState));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
///     One member on one of the five list pages. The row mutates the model directly (the page then saves and rebuilds),
///     so the same member can be looked at from five angles without any of them writing another list's data.
/// </summary>
public sealed class OrchestrationRuleMemberRow : INotifyPropertyChanged
{
    private readonly OrchestrationProfile _profile;
    private readonly OrchestrationRound _round;
    private readonly Action _changed;
    private int _position;
    private bool _canMoveUp;
    private bool _canMoveDown;
    private event PropertyChangedEventHandler? NotifyPropertyChanged;

    public OrchestrationRuleMemberRow(
        OrchestrationRuleKind kind,
        OrchestrationProfile profile,
        OrchestrationRound round,
        OrchestrationMember member,
        Action changed)
    {
        Kind = kind;
        _profile = profile;
        _round = round;
        Member = member;
        _changed = changed;
    }

    public OrchestrationRuleKind Kind { get; }

    public OrchestrationMember Member { get; }

    public string DisplayName => $"{Member.Number} {Member.Name}".Trim();

    public bool ShowPosition => Kind is OrchestrationRuleKind.Order or OrchestrationRuleKind.Guarantee;

    public bool ShowToggle => Kind != OrchestrationRuleKind.Weight;

    public bool ShowWeight => Kind == OrchestrationRuleKind.Weight;

    public bool ShowOrderButtons => Kind is OrchestrationRuleKind.Order or OrchestrationRuleKind.Guarantee;

    public string PositionText => _position > 0 ? _position.ToString() : "—";

    public bool CanMoveUp => _canMoveUp;

    public bool CanMoveDown => _canMoveDown;

    public void SetPosition(int position, bool canMoveUp, bool canMoveDown)
    {
        _position = position;
        _canMoveUp = canMoveUp;
        _canMoveDown = canMoveDown;
        OnPropertyChanged(nameof(PositionText));
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
    }

    public string ToggleHint => Kind switch
    {
        OrchestrationRuleKind.Exclude => PR.C_Rule_ToggleExclude,
        OrchestrationRuleKind.Defer => PR.C_Rule_ToggleDefer,
        OrchestrationRuleKind.Order => PR.C_Rule_ToggleOrder,
        _ => PR.C_Rule_ToggleGuarantee
    };

    /// <summary>Whether this member is currently on this page's list.</summary>
    public bool IsInList
    {
        get => Kind switch
        {
            OrchestrationRuleKind.Exclude => Member.IsExcluded,
            OrchestrationRuleKind.Defer => _round.Skipped.Contains(Member.RecordId),
            OrchestrationRuleKind.Order => _round.Order.Contains(Member.RecordId),
            OrchestrationRuleKind.Guarantee => _round.Guaranteed.Contains(Member.RecordId),
            _ => false
        };
        set
        {
            if (IsInList == value)
                return;

            switch (Kind)
            {
                case OrchestrationRuleKind.Exclude:
                    _profile.SetExcluded(Member.RecordId, value);
                    break;
                case OrchestrationRuleKind.Defer:
                    _profile.SetSkipped(_round, Member.RecordId, value);
                    break;
                case OrchestrationRuleKind.Order:
                    // 走 SetOrder 而不是直接改 List：队列写入同时负责去重，并把队列里的人从必中名单里拿出来。
                    _profile.SetOrder(
                        _round,
                        value
                            ? _round.Order.Concat([Member.RecordId]).ToList()
                            : _round.Order.Where(id => id != Member.RecordId).ToList());
                    break;
                case OrchestrationRuleKind.Guarantee:
                    _profile.SetGuaranteed(_round, Member.RecordId, value);
                    break;
            }

            _changed();
        }
    }

    public string WeightText
    {
        get => _round.Weights.TryGetValue(Member.RecordId, out var weight) ? weight.ToString("0.##") : string.Empty;
        set
        {
            var parsed = double.TryParse(value, out var weight) && weight >= 0;
            var current = _round.Weights.ContainsKey(Member.RecordId);
            if (parsed is false && current is false)
                return;

            _profile.SetWeight(_round, Member.RecordId, parsed ? Math.Clamp(weight, 0d, 100d) : null);
            _changed();
        }
    }

    public string Note
    {
        get
        {
            if (Member.IsSourceMissing)
                return PR.C_Orchestration_SourceMissing;
            if (Kind != OrchestrationRuleKind.Exclude && Member.IsExcluded)
                return PR.C_Rule_ExcludeNote;
            if (Kind == OrchestrationRuleKind.Order && !_round.Order.Contains(Member.RecordId))
                return PR.C_Rule_NotInQueue;
            if (Kind == OrchestrationRuleKind.Guarantee && !_round.Guaranteed.Contains(Member.RecordId))
                return PR.C_Rule_NotGuaranteed;
            return string.Empty;
        }
    }

    public bool HasNote => Note.Length > 0;

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => NotifyPropertyChanged += value;
        remove => NotifyPropertyChanged -= value;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}