using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums;
using SecRandom.Core.Services.Config;
using SecRandom.Models;
using SecRandom.ViewModels;
using SecRandom.Shared.Models.Profile;
using GroupSR = SecRandom.Langs.SettingsPages.History.Resources;
using History = SecRandom.Shared.Models.Profile.History;
using ProfileHistory = SecRandom.Shared.Models.Profile.History;

namespace SecRandom.ViewModels.SettingsPages.History;

public sealed partial class LotteryHistoryViewModel : ViewModelBase
{
    private readonly IHistoryQueryService _historyQueryService = IAppHost.GetService<IHistoryQueryService>();
    private readonly IProfileCatalogManager _catalogManager = IAppHost.GetService<IProfileCatalogManager>();
    private readonly IHistoryGroupCatalog _historyGroupCatalog = IAppHost.GetService<IHistoryGroupCatalog>();

    /// <summary>「移到另一组」作用的目标记录标识（RecordId），由表格选中项同步过来。</summary>
    private readonly HashSet<string> _selectedRecordIds = new(StringComparer.Ordinal);

    private PrizeHistory? _history;
    private PrizeList? _prizeList;
    private Dictionary<string, Prize> _prizeByKey = [];
    private Dictionary<string, PrizeInfo> _prizeInfoByKey = [];
    private HashSet<string> _uniqueLegacyKeys = [];
    private int _prizeIdPadWidth;

    [ObservableProperty] private string? _selectedPoolName;
    [ObservableProperty] private string _selectedMode = HistoryMode.Overview;
    [ObservableProperty] private HistoryGroupOption? _selectedHistoryGroupFilter;
    [ObservableProperty] private HistoryGroupOption? _selectedMoveTargetGroup;
    [ObservableProperty] private string _newMoveTargetGroup = string.Empty;
    [ObservableProperty] private string _moveStatus = string.Empty;

    public LotteryHistoryViewModel(MainConfigHandler configHandler) : base(configHandler)
    {
        RefreshCommand = new RelayCommand(Refresh);
        MoveSelectedHistoryGroupCommand = new RelayCommand(MoveSelectedHistoryGroup);
        RefreshPoolNames();
        SelectedPoolName = ResolveInitial(PoolNames, Config.HistoryManagementSettings.SelectedPoolName);
    }

    public ObservableCollection<string> PoolNames { get; } = [];
    public ObservableCollection<HistoryModeOption> ModeOptions { get; } = [];
    public ObservableCollection<HistoryGroupOption> HistoryGroupOptions { get; } = [];
    public ObservableCollection<HistoryGroupOption> MoveTargetOptions { get; } = [];
    public ObservableCollection<HistoryDisplayRow> Rows { get; } = [];

    /// <summary>当前行里是否有记录带记录组标记；没有任何组时「记录组」列不必占用宽度。</summary>
    public bool HasHistoryGroupRows => Rows.Any(row => !string.IsNullOrWhiteSpace(row.HistoryGroup));

    /// <summary>
    ///     「记录组」列只在确实有记录带组时才有意义。总览模式的行是按奖品汇总出来的，
    ///     一组记录可能分散在多个组里，没有单一的组可显示，因此总览模式下不显示该列。
    /// </summary>
    public bool ShouldShowHistoryGroupColumn => HasHistoryGroupRows && SelectedMode != HistoryMode.Overview;

    public bool HasMoveStatus => !string.IsNullOrWhiteSpace(MoveStatus);

    public bool CanMoveSelectedRecords => _selectedRecordIds.Count > 0;

    public IRelayCommand RefreshCommand { get; }

    public IRelayCommand MoveSelectedHistoryGroupCommand { get; }

    private static string? ResolveInitial(IReadOnlyList<string> names, string preferred)
    {
        if (names.Count == 0) return null;
        return names.Contains(preferred) ? preferred : names[0];
    }

    public void Refresh()
    {
        var selectedPoolName = SelectedPoolName;
        var selectedMode = SelectedMode;
        RefreshPoolNames();

        SelectedPoolName = ResolveRefreshSelection(PoolNames, selectedPoolName);
        Load(selectedMode);
    }

    private void RefreshPoolNames()
    {
        PoolNames.Clear();
        foreach (var name in _catalogManager.GetPrizeListNames()
                     .Concat(_historyQueryService.GetPrizeHistoryNames())
                     .Distinct()
                     .OrderBy(name => name, StringComparer.Ordinal))
            PoolNames.Add(name);
    }

    partial void OnSelectedPoolNameChanged(string? value) => Load();
    partial void OnSelectedModeChanged(string value) => BuildRows();

    /// <summary>切换记录组筛选只重新过滤已加载的快照，不重新读文件。</summary>
    partial void OnSelectedHistoryGroupFilterChanged(HistoryGroupOption? value) => BuildRows();

    partial void OnMoveStatusChanged(string value) => OnPropertyChanged(nameof(HasMoveStatus));

    private void Load(string? preferredMode = null)
    {
        Rows.Clear();
        _history = null;
        _prizeList = null;
        _prizeByKey = [];
        _prizeInfoByKey = [];
        _uniqueLegacyKeys = [];
        _prizeIdPadWidth = 0;

        if (string.IsNullOrWhiteSpace(SelectedPoolName))
        {
            RebuildModeOptions(preferredMode);
            RebuildHistoryGroupOptions();
            return;
        }

        // 只读快照：缺失的历史按空历史处理（保留既有“无历史文件仍显示名单行”的行为），
        // 缺失的奖池按空奖池处理；读取失败由查询/目录服务内部记录警告。
        _history = _historyQueryService.LoadPrizeHistory(SelectedPoolName) ?? new PrizeHistory(SelectedPoolName);

        _prizeList = _catalogManager.LoadPrizeList(SelectedPoolName);
        if (_prizeList is not null)
        {
            _uniqueLegacyKeys = ProfileRecordIdentity.BuildUniquePrizeLegacyKeySet(_prizeList.Prizes);
            _prizeByKey = BuildPrizeMap(_prizeList.Prizes, _uniqueLegacyKeys);
            _prizeInfoByKey = BuildPrizeInfoMap(_prizeList.Prizes, _uniqueLegacyKeys);
            _prizeIdPadWidth = CalculateNumericPadWidth(_prizeList.Prizes.Select(prize => prize.Id));
        }

        RebuildModeOptions(preferredMode);
        RebuildHistoryGroupOptions();
        BuildRows();
    }

    private void RebuildModeOptions(string? preferredMode = null)
    {
        var current = preferredMode ?? SelectedMode;
        ModeOptions.Clear();
        ModeOptions.Add(new HistoryModeOption { Key = HistoryMode.Overview, DisplayName = SecRandom.Langs.MainPages.History.Resources.C_ModeOverview });
        ModeOptions.Add(new HistoryModeOption { Key = HistoryMode.Records, DisplayName = SecRandom.Langs.MainPages.History.Resources.C_ModeRecords });

        foreach (var prize in GetVisiblePrizes())
        {
            var key = ProfileRecordIdentity.EnsureRecordId(prize);
            ModeOptions.Add(new HistoryModeOption { Key = key, DisplayName = FormatPrizeName(prize) });
        }

        if (_history != null)
            foreach (var key in _history.Prizes.Keys.Where(key => !ModeOptions.Any(option => option.Key == key)))
                ModeOptions.Add(new HistoryModeOption { Key = key, DisplayName = ResolvePrizeInfo(key).Name });

        if (!ModeOptions.Any(option => option.Key == current))
            current = HistoryMode.Overview;

        SelectedMode = current;
    }

    /// <summary>
    ///     重建「记录组」筛选与「移到另一组」目标组的下拉项。
    ///     筛选项第一项是「全部记录组」（值为 <c>null</c>，即不过滤），第二项是「默认组」（值为空串），
    ///     其余是现有组名；目标组没有「全部」，只有「默认组」+ 现有组 + 页面上可输入的新组名。
    ///     组名来源是 <see cref="IHistoryGroupCatalog" /> 的已注册列表与当前快照里记录实际携带的值的并集，
    ///     因此即使注册信息丢失，只要记录上还有该组名，它依旧出现在下拉里。
    /// </summary>
    private void RebuildHistoryGroupOptions()
    {
        var currentFilter = SelectedHistoryGroupFilter?.Value;
        var currentTarget = SelectedMoveTargetGroup?.Value;
        var names = CollectHistoryGroupNames().ToList();

        HistoryGroupOptions.Clear();
        HistoryGroupOptions.Add(new HistoryGroupOption { Value = null, DisplayName = GroupSR.S_HistoryGroup_All });
        HistoryGroupOptions.Add(new HistoryGroupOption { Value = string.Empty, DisplayName = GroupSR.S_HistoryGroup_Default });
        foreach (var name in names)
            HistoryGroupOptions.Add(new HistoryGroupOption { Value = name, DisplayName = name });

        SelectedHistoryGroupFilter = HistoryGroupOptions
                                         .FirstOrDefault(option => string.Equals(option.Value, currentFilter, StringComparison.Ordinal))
                                     ?? HistoryGroupOptions[0];

        MoveTargetOptions.Clear();
        MoveTargetOptions.Add(new HistoryGroupOption { Value = string.Empty, DisplayName = GroupSR.S_HistoryGroup_Default });
        foreach (var name in names)
            MoveTargetOptions.Add(new HistoryGroupOption { Value = name, DisplayName = name });

        SelectedMoveTargetGroup = MoveTargetOptions
                                      .FirstOrDefault(option => string.Equals(option.Value, currentTarget, StringComparison.Ordinal))
                                  ?? MoveTargetOptions[0];
    }

    /// <summary>当前档案下已存在的记录组名（Ordinal 排序，不含默认组）。</summary>
    private IEnumerable<string> CollectHistoryGroupNames()
    {
        HashSet<string> names = new(StringComparer.Ordinal);

        foreach (var name in _historyGroupCatalog.GetGroupNames(HistoryGroupKind.Lottery))
            if (!string.IsNullOrWhiteSpace(name))
                names.Add(name.Trim());

        if (_history is not null)
        {
            foreach (var name in _history.HistoryGroups.Keys)
                if (!string.IsNullOrWhiteSpace(name))
                    names.Add(name.Trim());

            foreach (var item in _history.Prizes.Values.SelectMany(history => history.Histories))
                if (!string.IsNullOrWhiteSpace(item.HistoryGroup))
                    names.Add(item.HistoryGroup.Trim());
        }

        return names.OrderBy(name => name, StringComparer.Ordinal);
    }

    /// <summary>
    ///     表格选中项变化时调用。只有逐条记录的行带 <see cref="HistoryDisplayRow.RecordId" />，
    ///     总览模式的行是按奖品汇总的，没有对应记录，选中它们不会产生可移动的目标。
    /// </summary>
    public void SetSelectedRows(IEnumerable<HistoryDisplayRow> rows)
    {
        _selectedRecordIds.Clear();
        foreach (var row in rows)
            if (!string.IsNullOrWhiteSpace(row.RecordId))
                _selectedRecordIds.Add(row.RecordId);

        OnPropertyChanged(nameof(CanMoveSelectedRecords));
    }

    /// <summary>
    ///     「移到另一组」：只修改选中记录的记录组标记，不新增记录、不改变权重，
    ///     因此直接调用档案目录的 Move API 而不是抽取提交流程。输入框填了组名就用输入的组名（可新建组），
    ///     否则用下拉选中的目标组。
    /// </summary>
    private void MoveSelectedHistoryGroup()
    {
        MoveStatus = string.Empty;

        if (_selectedRecordIds.Count == 0 || string.IsNullOrWhiteSpace(SelectedPoolName))
            return;

        var targetGroup = NewMoveTargetGroup.Trim();
        if (targetGroup.Length == 0)
            targetGroup = SelectedMoveTargetGroup?.Value ?? string.Empty;

        try
        {
            var moved = _catalogManager.MovePrizeHistoryGroup(SelectedPoolName, _selectedRecordIds.ToArray(), targetGroup);
            NewMoveTargetGroup = string.Empty;
            MoveStatus = string.Format(CultureInfo.CurrentCulture, GroupSR.S_HistoryGroup_MoveDone, moved);
            Refresh();
        }
        catch (ArgumentException ex)
        {
            // 非法组名（空/超长/控制字符/保留名）：只提示，不让异常冒到 dispatcher
            MoveStatus = ex.Message;
        }
        catch (InvalidOperationException ex)
        {
            // 当前档案不存在或保存失败
            MoveStatus = ex.Message;
        }
    }

    /// <summary>记录组筛选只过滤已加载的快照，不重新读文件。</summary>
    private IEnumerable<HistoryItem> GetFilteredHistoryItems(ProfileHistory history)
    {
        var group = GetSelectedHistoryGroupFilter();
        return group is null
            ? history.Histories
            : history.Histories.Where(item => string.Equals(item.HistoryGroup ?? string.Empty, group, StringComparison.Ordinal));
    }

    private int GetFilteredHistoryCount(ProfileHistory? history)
    {
        if (history is null)
            return 0;

        return GetSelectedHistoryGroupFilter() is null
            ? history.TotalCount
            : GetFilteredHistoryItems(history).Count();
    }

    private string? GetSelectedHistoryGroupFilter() => SelectedHistoryGroupFilter?.Value;

    private void BuildRows()
    {
        Rows.Clear();
        BuildRowsCore();

        OnPropertyChanged(nameof(HasHistoryGroupRows));
        OnPropertyChanged(nameof(ShouldShowHistoryGroupColumn));
    }

    private void BuildRowsCore()
    {
        if (_history == null) return;

        var mode = SelectedMode;
        if (mode == HistoryMode.Overview)
        {
            foreach (var prize in GetVisiblePrizes())
                Rows.Add(BuildOverviewRow(prize));

            AddOrphanOverviewRows();
        }
        else if (mode == HistoryMode.Records)
        {
            foreach (var prize in GetVisiblePrizes())
                AddHistoryRows(prize);

            AddOrphanHistoryRows();
            SortByTimeDesc(Rows);
        }
        else if (_prizeByKey.TryGetValue(mode, out var prize))
        {
            var history = ResolveHistory(prize);
            if (history is null)
                return;

            var info = PrizeInfo.From(prize);
            foreach (var item in GetFilteredHistoryItems(history))
                Rows.Add(BuildEventRow(info, item, _prizeIdPadWidth));
            SortByTimeDesc(Rows);
        }
        else if (_history.Prizes.TryGetValue(mode, out var target))
        {
            var info = ResolvePrizeInfo(mode);
            foreach (var item in GetFilteredHistoryItems(target))
                Rows.Add(BuildEventRow(info, item, _prizeIdPadWidth));
            SortByTimeDesc(Rows);
        }
    }

    private IEnumerable<Prize> GetVisiblePrizes()
    {
        return _prizeList?.Prizes.Where(prize => prize.IsCandidate) ?? [];
    }

    private HistoryDisplayRow BuildOverviewRow(Prize prize)
    {
        var history = ResolveHistory(prize);
        return new HistoryDisplayRow
        {
            Id = FormatNumericId(prize.Id, _prizeIdPadWidth),
            Name = prize.Name,
            TotalCount = GetFilteredHistoryCount(history),
            Weight = FormatWeight(prize.Weight)
        };
    }

    private void AddHistoryRows(Prize prize)
    {
        var history = ResolveHistory(prize);
        if (history is null)
            return;

        var info = PrizeInfo.From(prize);
        foreach (var item in GetFilteredHistoryItems(history))
            Rows.Add(BuildEventRow(info, item, _prizeIdPadWidth));
    }

    private ProfileHistory? ResolveHistory(Prize prize)
    {
        if (_history is null)
            return null;

        return ProfileRecordIdentity.GetPrizeHistory(_history, prize, _uniqueLegacyKeys.Contains);
    }

    private void AddOrphanOverviewRows()
    {
        if (_history is null)
            return;

        var knownKeys = BuildKnownPrizeHistoryKeys();
        foreach (var (key, history) in _history.Prizes.Where(pair => !knownKeys.Contains(pair.Key)))
        {
            var info = ResolvePrizeInfo(key);
            Rows.Add(new HistoryDisplayRow
            {
                Id = FormatNumericId(info.Id, _prizeIdPadWidth),
                Name = info.Name,
                TotalCount = GetFilteredHistoryCount(history),
                Weight = FormatLatestWeight(history)
            });
        }
    }

    private void AddOrphanHistoryRows()
    {
        if (_history is null)
            return;

        var knownKeys = BuildKnownPrizeHistoryKeys();
        foreach (var (key, history) in _history.Prizes.Where(pair => !knownKeys.Contains(pair.Key)))
        {
            var info = ResolvePrizeInfo(key);
            foreach (var item in GetFilteredHistoryItems(history))
                Rows.Add(BuildEventRow(info, item, _prizeIdPadWidth));
        }
    }

    private HashSet<string> BuildKnownPrizeHistoryKeys()
    {
        HashSet<string> keys = [];
        foreach (var prize in GetVisiblePrizes())
        {
            keys.Add(ProfileRecordIdentity.EnsureRecordId(prize));
            foreach (var key in ProfileRecordIdentity.GetLegacyPrizeHistoryKeys(prize).Where(_uniqueLegacyKeys.Contains))
                keys.Add(key);
        }

        return keys;
    }

    private PrizeInfo ResolvePrizeInfo(string historyKey)
    {
        return _prizeInfoByKey.GetValueOrDefault(historyKey) ?? PrizeInfo.Unknown(historyKey);
    }

    private static Dictionary<string, PrizeInfo> BuildPrizeInfoMap(
        IEnumerable<Prize> prizes,
        ISet<string> uniqueLegacyKeys)
    {
        Dictionary<string, PrizeInfo> result = [];
        foreach (var prize in prizes)
        {
            var info = PrizeInfo.From(prize);
            result[ProfileRecordIdentity.EnsureRecordId(prize)] = info;
            foreach (var key in ProfileRecordIdentity.GetLegacyPrizeHistoryKeys(prize).Where(uniqueLegacyKeys.Contains))
                result.TryAdd(key, info);
        }

        return result;
    }

    private static Dictionary<string, Prize> BuildPrizeMap(
        IEnumerable<Prize> prizes,
        ISet<string> uniqueLegacyKeys)
    {
        Dictionary<string, Prize> result = [];
        foreach (var prize in prizes)
        {
            result[ProfileRecordIdentity.EnsureRecordId(prize)] = prize;
            foreach (var key in ProfileRecordIdentity.GetLegacyPrizeHistoryKeys(prize).Where(uniqueLegacyKeys.Contains))
                result.TryAdd(key, prize);
        }

        return result;
    }

    private static HistoryDisplayRow BuildEventRow(PrizeInfo info, HistoryItem item, int idPadWidth) =>
        new()
        {
            RecordId = item.RecordId,
            Id = FormatNumericId(string.IsNullOrWhiteSpace(item.RecordNumber) ? info.Id : item.RecordNumber, idPadWidth),
            Name = string.IsNullOrWhiteSpace(item.RecordName) ? info.Name : item.RecordName,
            DrawTime = item.DrawTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture),
            DrawNumbers = item.DrawNumbers,
            // 记录组是记录自己的用途标记，与奖品名/号码等字段互不相干
            HistoryGroup = item.HistoryGroup ?? string.Empty,
            Weight = FormatWeight(item.Weight),
            SortTime = item.DrawTime
        };

    private static string FormatPrizeName(Prize prize)
    {
        return string.IsNullOrWhiteSpace(prize.Id) ? prize.Name : $"{prize.Id} {prize.Name}";
    }

    private static int CalculateNumericPadWidth(IEnumerable<string> values)
    {
        return values
            .Where(value => int.TryParse(value, out _))
            .Select(value => value.Trim().Length)
            .DefaultIfEmpty(0)
            .Max();
    }

    private static string FormatNumericId(string value, int width)
    {
        var trimmed = value.Trim();
        return width > 0 && int.TryParse(trimmed, out var number)
            ? number.ToString($"D{width}", CultureInfo.CurrentCulture)
            : trimmed;
    }

    private static string FormatWeight(double weight)
    {
        return weight.ToString("0.00", CultureInfo.CurrentCulture);
    }

    private static string? ResolveRefreshSelection(IReadOnlyList<string> names, string? current) =>
        !string.IsNullOrWhiteSpace(current) && names.Contains(current) ? current : ResolveInitial(names, string.Empty);

    private string FormatLatestWeight(ProfileHistory history) =>
        GetFilteredHistoryItems(history).LastOrDefault() is { } last
            ? FormatWeight(last.Weight)
            : string.Empty;

    private static void SortByTimeDesc(ObservableCollection<HistoryDisplayRow> rows)
    {
        var sorted = rows.OrderByDescending(r => r.SortTime).ToList();
        rows.Clear();
        foreach (var row in sorted) rows.Add(row);
    }

    private sealed record PrizeInfo(string Name, string Id)
    {
        public static PrizeInfo From(Prize prize) => new(prize.Name, prize.Id);
        public static PrizeInfo Unknown(string key) => new(key, string.Empty);
    }
}
