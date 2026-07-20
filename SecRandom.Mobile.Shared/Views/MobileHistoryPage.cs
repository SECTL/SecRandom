using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Markup.Xaml;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Icons;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Draw;
using SecRandom.Core.Views;
using SecRandom.Mobile.Controls;
using SecRandom.Shared.Models.Profile;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;

namespace SecRandom.Mobile.Views;

public sealed partial class MobileHistoryPage : ViewBase
{
    private readonly IProfileService _profileService;
    private readonly IHistoryQueryService _historyQueryService;
    private readonly IProfileCatalogManager _catalogManager;
    private readonly MainConfigHandler _configHandler;
    private readonly DrawEngine _drawEngine;
    private int _segment;
    private string? _profileName;
    private bool _recordsMode;
    private bool _firstRender = true;

    public MobileHistoryPage(
        IProfileService profileService,
        IHistoryQueryService historyQueryService,
        IProfileCatalogManager catalogManager,
        MainConfigHandler configHandler,
        DrawEngine drawEngine)
    {
        _profileService = profileService;
        _historyQueryService = historyQueryService;
        _catalogManager = catalogManager;
        _configHandler = configHandler;
        _drawEngine = drawEngine;
        InitializeComponent();
        Render();
    }

    private void Render()
    {
        if (_segment == 1 && !MobileCapabilities.IsLotteryEnabled)
            _segment = 0;

        var segmented = MobileViewFactory.CreateTabSplit(
            _segment,
            [
                (LR.C_RollCall, true),
                (LR.C_Lottery, MobileCapabilities.IsLotteryEnabled)
            ],
            segment =>
            {
                if (segment == _segment)
                    return;

                _segment = segment;
                _profileName = null;
                _recordsMode = false;
                Render();
            });

        var profileNames = GetProfileNames();
        _profileName = ResolveProfileName(profileNames);
        var profile = CreateComboBox(profileNames, _profileName, value =>
        {
            _profileName = value;
            Render();
        });
        var modes = new[] { LR.O_HistoryOverview, LR.O_HistoryRecords };
        var mode = CreateComboBox(modes, _recordsMode ? modes[1] : modes[0], value =>
        {
            _recordsMode = string.Equals(value, modes[1], StringComparison.Ordinal);
            Render();
        });
        var filters = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 8,
            Children =
            {
                CreateLabeledControl(LR.S_Profile, profile),
                CreateLabeledControl(LR.S_HistoryMode, mode)
            }
        };
        Grid.SetColumn(filters.Children[1], 1);

        var rows = _segment == 0 ? BuildStudentRows() : BuildPrizeRows();
        Control table = rows.Count == 0
            ? new MobileEmptyState(
                FluentIcons.HistoryFilled,
                LR.M_NoHistory,
                LR.M_NoHistoryPrompt)
            : CreateHistoryGrid(rows);

        var refresh = MobileViewFactory.CreateSecondaryButton(LR.C_Refresh, Render);

        ScrollViewer scroll = MobileViewFactory.CreateScroll([
            segmented,
            filters,
            table,
            refresh
        ]);
        this.FindControl<ContentControl>("PageContent")!.Content = scroll;
        if (_firstRender)
        {
            _firstRender = false;
            MobileAnimations.PlayPageEnter(scroll);
        }
    }

    private IReadOnlyList<string> GetProfileNames() => (_segment == 0
            ? _catalogManager.GetStudentListNames().Concat(_historyQueryService.GetStudentHistoryNames())
            : _catalogManager.GetPrizeListNames().Concat(_historyQueryService.GetPrizeHistoryNames()))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(name => name, StringComparer.CurrentCulture)
        .ToArray();

    private string? ResolveProfileName(IReadOnlyList<string> names)
    {
        if (_profileName is not null && names.Contains(_profileName, StringComparer.Ordinal))
            return _profileName;

        var current = _segment == 0
            ? _profileService.StudentListConfig?.Name
            : _profileService.PrizeListConfig?.Name;
        return current is not null && names.Contains(current, StringComparer.Ordinal)
            ? current
            : names.FirstOrDefault();
    }

    private IReadOnlyList<MobileHistoryRow> BuildStudentRows()
    {
        if (string.IsNullOrWhiteSpace(_profileName))
            return [];

        var history = _historyQueryService.LoadStudentHistory(_profileName)
                      ?? new StudentHistory(_profileName);
        if (_recordsMode)
        {
            var recordStudents = _catalogManager.LoadStudentList(_profileName)?.Students ?? [];
            return history.Students.Values
                .SelectMany(record => record.Histories)
                .OrderByDescending(item => item.DrawTime)
                .Select(item =>
                {
                    var student = ResolveStudent(recordStudents, item);
                    return new MobileHistoryRow
                    {
                        Time = item.DrawTime.ToString("g", System.Globalization.CultureInfo.CurrentCulture),
                        Id = FirstNonBlank(item.RecordNumber, student?.Id),
                        Name = FirstNonBlank(item.RecordName, student?.Name, LR.M_Unknown),
                        Gender = FirstNonBlank(item.RecordGender, student?.Gender),
                        Group = FirstNonBlank(item.RecordGroup, student?.Group),
                        Count = item.DrawNumbers,
                        Scope = FormatScope(item.DrawGroup, item.DrawGender),
                        Weight = item.DrawMethod == (int)DrawType.Fair
                            ? item.Weight.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture)
                            : string.Empty
                    };
                })
                .ToArray();
        }

        var list = _catalogManager.LoadStudentList(_profileName);
        var students = list?.Students.Where(student => student.IsCandidate).ToArray() ?? [];
        var uniqueLegacyKeys = ProfileRecordIdentity.BuildUniqueStudentLegacyKeySet(students);
        var weights = BuildStudentWeights(students, history, uniqueLegacyKeys);
        HashSet<string> consumedHistoryKeys = [];
        var rows = new List<MobileHistoryRow>();
        foreach (var student in students)
        {
            var record = ProfileRecordIdentity.GetStudentHistory(history, student, uniqueLegacyKeys.Contains);
            foreach (var pair in history.Students.Where(pair => ReferenceEquals(pair.Value, record)))
                consumedHistoryKeys.Add(pair.Key);
            rows.Add(new MobileHistoryRow
            {
                Id = student.Id,
                Name = student.Name,
                Gender = student.Gender,
                Group = student.Group,
                Count = record?.TotalCount ?? 0,
                Weight = weights.GetValueOrDefault(student, string.Empty)
            });
        }

        foreach (var (_, record) in history.Students.Where(pair => !consumedHistoryKeys.Contains(pair.Key)))
        {
            var latest = record.Histories.LastOrDefault();
            rows.Add(new MobileHistoryRow
            {
                Id = latest?.RecordNumber ?? string.Empty,
                Name = FirstNonBlank(latest?.RecordName, LR.M_Unknown),
                Gender = latest?.RecordGender ?? string.Empty,
                Group = latest?.RecordGroup ?? string.Empty,
                Count = record.TotalCount,
                Weight = string.Empty
            });
        }

        return rows;
    }

    private IReadOnlyList<MobileHistoryRow> BuildPrizeRows()
    {
        if (string.IsNullOrWhiteSpace(_profileName))
            return [];

        var history = _historyQueryService.LoadPrizeHistory(_profileName)
                      ?? new PrizeHistory(_profileName);
        if (_recordsMode)
        {
            var recordPrizes = _catalogManager.LoadPrizeList(_profileName)?.Prizes ?? [];
            return history.Prizes.Values
                .SelectMany(record => record.Histories)
                .OrderByDescending(item => item.DrawTime)
                .Select(item =>
                {
                    var prize = ResolvePrize(recordPrizes, item);
                    return new MobileHistoryRow
                    {
                        Time = item.DrawTime.ToString("g", System.Globalization.CultureInfo.CurrentCulture),
                        Id = FirstNonBlank(item.RecordNumber, prize?.Id),
                        Name = FirstNonBlank(item.RecordName, prize?.Name, LR.M_Unknown),
                        Count = item.DrawNumbers,
                        Weight = item.DrawMethod == (int)LotteryDrawType.Pan
                            ? item.Weight.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture)
                            : string.Empty
                    };
                })
                .ToArray();
        }

        var list = _catalogManager.LoadPrizeList(_profileName);
        var prizes = list?.Prizes.Where(prize => prize.IsCandidate).ToArray() ?? [];
        var uniqueLegacyKeys = ProfileRecordIdentity.BuildUniquePrizeLegacyKeySet(prizes);
        HashSet<string> consumedHistoryKeys = [];
        var rows = new List<MobileHistoryRow>();
        foreach (var prize in prizes)
        {
            var record = ProfileRecordIdentity.GetPrizeHistory(history, prize, uniqueLegacyKeys.Contains);
            foreach (var pair in history.Prizes.Where(pair => ReferenceEquals(pair.Value, record)))
                consumedHistoryKeys.Add(pair.Key);
            rows.Add(new MobileHistoryRow
            {
                Id = prize.Id,
                Name = prize.Name,
                Count = record?.TotalCount ?? 0,
                Weight = _configHandler.Data.LotterySettings.DrawType == LotteryDrawType.Pan
                    ? prize.Weight.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture)
                    : string.Empty
            });
        }

        foreach (var (_, record) in history.Prizes.Where(pair => !consumedHistoryKeys.Contains(pair.Key)))
        {
            var latest = record.Histories.LastOrDefault();
            rows.Add(new MobileHistoryRow
            {
                Id = latest?.RecordNumber ?? string.Empty,
                Name = FirstNonBlank(latest?.RecordName, LR.M_Unknown),
                Count = record.TotalCount,
                Weight = string.Empty
            });
        }

        return rows;
    }

    private DataGrid CreateHistoryGrid(IReadOnlyList<MobileHistoryRow> rows)
    {
        var grid = CreateDataGrid(rows);
        if (_recordsMode)
            grid.Columns.Add(TextColumn(LR.H_Time, nameof(MobileHistoryRow.Time), 180));
        grid.Columns.Add(TextColumn(LR.H_Id, nameof(MobileHistoryRow.Id), 100));
        grid.Columns.Add(TextColumn(LR.H_Name, nameof(MobileHistoryRow.Name), 180));
        if (_segment == 0)
        {
            grid.Columns.Add(TextColumn(LR.H_Gender, nameof(MobileHistoryRow.Gender), 100));
            grid.Columns.Add(TextColumn(LR.H_Group, nameof(MobileHistoryRow.Group), 120));
        }
        grid.Columns.Add(TextColumn(LR.H_Count, nameof(MobileHistoryRow.Count), 120));
        if (_recordsMode && _segment == 0)
            grid.Columns.Add(TextColumn(LR.H_DrawScope, nameof(MobileHistoryRow.Scope), 180));
        if (rows.Any(row => !string.IsNullOrWhiteSpace(row.Weight)) &&
            (_recordsMode || _segment == 1 || _configHandler.Data.RollCallSettings.DrawType == DrawType.Fair))
            grid.Columns.Add(TextColumn(LR.H_Weight, nameof(MobileHistoryRow.Weight), 100));
        return grid;
    }

    private static DataGrid CreateDataGrid(IEnumerable<MobileHistoryRow> rows) => new()
    {
        AutoGenerateColumns = false,
        CanUserResizeColumns = true,
        CanUserSortColumns = true,
        GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        IsReadOnly = true,
        ItemsSource = rows,
        Height = 58 + rows.Count() * 48,
        RowHeight = 48,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
    };

    private static DataGridTextColumn TextColumn(string header, string path, double width) => new()
    {
        Header = header,
        Binding = new Binding(path),
        Width = new DataGridLength(width)
    };

    private static ComboBox CreateComboBox(
        IReadOnlyList<string> values,
        string? selected,
        Action<string> changed)
    {
        var combo = new ComboBox
        {
            ItemsSource = values,
            SelectedItem = selected,
            MinHeight = 44,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is string value && !string.Equals(value, selected, StringComparison.Ordinal))
                changed(value);
        };
        return combo;
    }

    private static Control CreateLabeledControl(string label, Control control)
    {
        var text = new TextBlock { Text = label, FontSize = 12 };
        MobileResources.BindBrush(text, TextBlock.ForegroundProperty, MobileResources.Keys.MutedText);
        return new StackPanel { Spacing = 4, Children = { text, control } };
    }

    private static string FormatScope(string group, string gender)
    {
        var scope = new[] { gender, group }.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        return scope.Length == 0 ? LR.O_All : string.Join(" · ", scope);
    }

    private IReadOnlyDictionary<Student, string> BuildStudentWeights(
        IReadOnlyList<Student> students,
        StudentHistory history,
        ISet<string> uniqueLegacyKeys)
    {
        if (_configHandler.Data.RollCallSettings.DrawType != DrawType.Fair ||
            students.Count == 0 ||
            !string.Equals(_profileName, _profileService.StudentListConfig?.Name, StringComparison.Ordinal))
            return new Dictionary<Student, string>();

        var cache = students
            .Select(student => (Student: student,
                History: ProfileRecordIdentity.GetStudentHistory(history, student, uniqueLegacyKeys.Contains)))
            .Where(pair => pair.History is not null)
            .ToDictionary(pair => pair.Student, pair => pair.History!);
        return _drawEngine.CalculateStudentWeightWithMobileDesktopDefaults(students.ToList(), cache)
            .ToDictionary(candidate => candidate.Candidate,
                candidate => candidate.Weight.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture));
    }

    private static Student? ResolveStudent(IReadOnlyList<Student> students, HistoryItem item) =>
        ResolveRecord(students, item, student => student.RecordId, student => student.Id, student => student.Name);

    private static Prize? ResolvePrize(IReadOnlyList<Prize> prizes, HistoryItem item) =>
        ResolveRecord(prizes, item, prize => prize.RecordId, prize => prize.Id, prize => prize.Name);

    private static T? ResolveRecord<T>(
        IReadOnlyList<T> records,
        HistoryItem item,
        Func<T, Guid> recordId,
        Func<T, string> id,
        Func<T, string> name) where T : class
    {
        if (Guid.TryParse(item.RecordId, out var parsedRecordId))
        {
            var exact = records.FirstOrDefault(record => recordId(record) == parsedRecordId);
            if (exact is not null)
                return exact;
        }

        var matches = records.Where(record =>
                (!string.IsNullOrWhiteSpace(item.RecordNumber) && string.Equals(id(record), item.RecordNumber, StringComparison.Ordinal)) ||
                (!string.IsNullOrWhiteSpace(item.RecordName) && string.Equals(name(record), item.RecordName, StringComparison.Ordinal)))
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static string FirstNonBlank(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private sealed class MobileHistoryRow
    {
        public string Time { get; init; } = string.Empty;
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Gender { get; init; } = string.Empty;
        public string Group { get; init; } = string.Empty;
        public int Count { get; init; }
        public string Scope { get; init; } = string.Empty;
        public string Weight { get; init; } = string.Empty;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
