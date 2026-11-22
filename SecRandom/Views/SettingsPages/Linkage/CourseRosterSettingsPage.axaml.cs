using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.Linkage;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Profiles;
using SecRandom.Services.Linkage;
using SecRandom.ViewModels;
using LR = SecRandom.Langs.SettingsPages.Linkage.Resources;

namespace SecRandom.Views.SettingsPages.Linkage;

[PageInfo("settings.linkage.courseRoster", FluentIcons.CalendarLtrFilled, "settings.linkage", isHide: true)]
public partial class CourseRosterSettingsPage : UserControl, INotifyPropertyChanged
{
    private readonly CourseRosterCourseCatalog _courseCatalog;
    private readonly IProfileCatalogManager _profileCatalog;
    private readonly MainConfigHandler _configHandler;
    private readonly CourseRosterMappingService _mappingService;
    private CancellationTokenSource? _loadCancellation;
    private string[] _allCourseNames = [];
    private HashSet<string> _sourceCourseNames = new(StringComparer.Ordinal);
    private LinkageDataSource _loadedSource;
    private string _searchText = string.Empty;
    private string _statusText = string.Empty;
    private bool _isLoading;
    private bool _isRebuilding;

    private event PropertyChangedEventHandler? NotifyPropertyChanged;

    public CourseRosterSettingsPage()
    {
        _courseCatalog = IAppHost.GetService<CourseRosterCourseCatalog>();
        _profileCatalog = IAppHost.GetService<IProfileCatalogManager>();
        _configHandler = IAppHost.GetService<MainConfigHandler>();
        _mappingService = IAppHost.GetService<CourseRosterMappingService>();
        Settings = IAppHost.GetService<ViewModelBase>().Config.LinkageSettings;
        _loadedSource = Settings.DataSource;
        DataContext = this;
        InitializeComponent();
        RebuildRows();
        Unloaded += (_, _) => _loadCancellation?.Cancel();
    }

    public LinkageSettingsConfig Settings { get; }
    public ObservableCollection<CourseRosterRow> Rows { get; } = [];
    public bool HasRows => Rows.Count > 0;
    public bool IsLoading => _isLoading;

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText == value)
                return;
            _statusText = value;
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value)
                return;
            _searchText = value ?? string.Empty;
            OnPropertyChanged(nameof(SearchText));
            RebuildRows();
        }
    }

    private static string Text(string key) => LR.ResourceManager.GetString(key, LR.Culture) ?? key;

    private void OnLoaded(object? sender, RoutedEventArgs e) => _ = ReloadAsync();

    private async void Refresh_OnClick(object? sender, RoutedEventArgs e) => await ReloadAsync();

    private async Task ReloadAsync()
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        var cancellationToken = _loadCancellation.Token;
        var source = Settings.DataSource;
        _loadedSource = source;
        if (source == LinkageDataSource.Off)
        {
            _allCourseNames = [];
            _sourceCourseNames = new HashSet<string>(StringComparer.Ordinal);
            _isLoading = false;
            OnPropertyChanged(nameof(IsLoading));
            StatusText = Text("M_CourseSourceOff");
            RebuildRows();
            return;
        }

        _isLoading = true;
        OnPropertyChanged(nameof(IsLoading));
        StatusText = Text("M_CourseLoading");
        try
        {
            var names = await _courseCatalog.GetCourseNamesAsync(source, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
                return;

            _sourceCourseNames = names.ToHashSet(StringComparer.Ordinal);
            var assignments = Settings.CourseRosterBindings ?? [];
            var configuredNames = assignments
                .Where(binding => binding.DataSource == source)
                .Select(binding => binding.CourseName)
                .Where(name => !string.IsNullOrWhiteSpace(name));
            _allCourseNames = names.Concat(configuredNames)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.CurrentCulture)
                .ToArray();
            BuildRows(_allCourseNames, _sourceCourseNames, source);
            StatusText = _allCourseNames.Length == 0
                ? Text("M_CourseUnavailable")
                : string.Format(System.Globalization.CultureInfo.CurrentCulture, Text("M_CourseCount"), _allCourseNames.Length);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            StatusText = Text("M_CourseUnavailable");
            this.ShowErrorToast(StatusText, exception);
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                _isLoading = false;
                OnPropertyChanged(nameof(IsLoading));
            }
        }
    }

    private void RebuildRows()
    {
        if (_isRebuilding)
            return;
        if (Settings.DataSource != _loadedSource)
        {
            _ = ReloadAsync();
            return;
        }
        _isRebuilding = true;
        try
        {
            var source = Settings.DataSource;
            BuildRows(_allCourseNames, _sourceCourseNames, source);
        }
        finally
        {
            _isRebuilding = false;
        }
    }

    private void BuildRows(IReadOnlyList<string> names, IReadOnlySet<string> sourceNames,
        LinkageDataSource? sourceOverride = null)
    {
        if (_isRebuilding)
            return;
        _isRebuilding = true;
        try
        {
            var source = sourceOverride ?? Settings.DataSource;
            var studentOptions = BuildOptions(_profileCatalog.GetStudentListNames(), Text("O_CourseRosterDefault"));
            var prizeOptions = BuildOptions(_profileCatalog.GetPrizeListNames(), Text("O_CourseRosterDefault"));
            var assignments = Settings.CourseRosterBindings ?? [];
            Rows.Clear();
            foreach (var courseName in names.Where(name =>
                         string.IsNullOrWhiteSpace(_searchText) ||
                         name.Contains(_searchText.Trim(), StringComparison.CurrentCultureIgnoreCase)))
            {
                var binding = assignments.FirstOrDefault(item =>
                    item.DataSource == source && string.Equals(item.CourseName, courseName, StringComparison.Ordinal));
                binding ??= new CourseRosterBinding { DataSource = source, CourseName = courseName };
                var row = new CourseRosterRow(binding, studentOptions, prizeOptions,
                    sourceNames.Contains(courseName), PersistBindings);
                Rows.Add(row);
            }
            OnPropertyChanged(nameof(HasRows));
        }
        finally
        {
            _isRebuilding = false;
        }
    }

    private static IReadOnlyList<CourseRosterListOption> BuildOptions(
        IReadOnlyList<string> names,
        string defaultLabel) =>
        [new CourseRosterListOption(string.Empty, defaultLabel), .. names.Select(name => new CourseRosterListOption(name, name))];

    private void PersistBindings()
    {
        if (_isRebuilding)
            return;

        var source = Settings.DataSource;
        var visibleNames = Rows.Select(row => row.CourseName).ToHashSet(StringComparer.Ordinal);
        var otherBindings = (Settings.CourseRosterBindings ?? [])
            .Where(binding => binding.DataSource != source || !visibleNames.Contains(binding.CourseName))
            .ToList();
        otherBindings.AddRange(Rows.Select(row => row.Binding)
            .Where(binding => binding.IsCustomCourse
                              || !string.IsNullOrWhiteSpace(binding.RollCallStudentListName)
                              || !string.IsNullOrWhiteSpace(binding.QuickDrawStudentListName)
                              || !string.IsNullOrWhiteSpace(binding.LotteryPrizeListName)));
        Settings.CourseRosterBindings = otherBindings;
        _configHandler.Save();
        _mappingService.NotifyBindingsChanged();
    }

    private void OnPropertyChanged(string propertyName) =>
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => NotifyPropertyChanged += value;
        remove => NotifyPropertyChanged -= value;
    }

    private async void AddCourse_OnClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null || Settings.DataSource == LinkageDataSource.Off)
            return;

        var input = new TextBox
        {
            MinWidth = 280,
            Text = SearchText.Trim(),
            PlaceholderText = Text("M_AddCoursePrompt")
        };
        var result = await new FAContentDialog
        {
            Title = Text("C_AddCourse"),
            Content = input,
            PrimaryButtonText = LR.C_Confirm,
            CloseButtonText = LR.C_Cancel,
            DefaultButton = FAContentDialogButton.Primary
        }.ShowAsync(topLevel);
        if (result != FAContentDialogResult.Primary)
            return;

        var courseName = input.Text?.Trim() ?? string.Empty;
        if (courseName.Length == 0)
        {
            this.ShowWarningToast(Text("M_CourseNameRequired"));
            return;
        }
        if (_allCourseNames.Contains(courseName, StringComparer.Ordinal))
        {
            SearchText = courseName;
            this.ShowWarningToast(Text("M_CourseAlreadyExists"));
            return;
        }

        var bindings = (Settings.CourseRosterBindings ?? []).ToList();
        bindings.Add(new CourseRosterBinding
        {
            DataSource = Settings.DataSource,
            CourseName = courseName,
            IsCustomCourse = true
        });
        Settings.CourseRosterBindings = bindings;
        _configHandler.Save();
        _mappingService.NotifyBindingsChanged();
        _allCourseNames = [.. _allCourseNames, courseName];
        SearchText = courseName;
        RebuildRows();
    }
}

public sealed record CourseRosterListOption(string Name, string Label);

public sealed class CourseRosterRow : INotifyPropertyChanged
{
    private readonly Action _persist;
    private readonly CourseRosterBinding _binding;

    public event PropertyChangedEventHandler? PropertyChanged;

    public CourseRosterRow(CourseRosterBinding binding,
        IReadOnlyList<CourseRosterListOption> studentListOptions,
        IReadOnlyList<CourseRosterListOption> prizeListOptions,
        bool isAvailable,
        Action persist)
    {
        _binding = binding;
        StudentListOptions = AddMissingOption(studentListOptions, binding.RollCallStudentListName,
            binding.QuickDrawStudentListName);
        PrizeListOptions = AddMissingOption(prizeListOptions, binding.LotteryPrizeListName);
        IsAvailable = isAvailable;
        _persist = persist;
    }

    public CourseRosterBinding Binding => _binding;
    public string CourseName => _binding.CourseName;
    public bool IsAvailable { get; }
    public IReadOnlyList<CourseRosterListOption> StudentListOptions { get; }
    public IReadOnlyList<CourseRosterListOption> PrizeListOptions { get; }

    public CourseRosterListOption SelectedRollCallStudentList
    {
        get => FindOption(StudentListOptions, _binding.RollCallStudentListName);
        set => SetName(_binding.RollCallStudentListName, value?.Name, name => _binding.RollCallStudentListName = name,
            nameof(SelectedRollCallStudentList));
    }

    public CourseRosterListOption SelectedQuickDrawStudentList
    {
        get => FindOption(StudentListOptions, _binding.QuickDrawStudentListName);
        set => SetName(_binding.QuickDrawStudentListName, value?.Name, name => _binding.QuickDrawStudentListName = name,
            nameof(SelectedQuickDrawStudentList));
    }

    public CourseRosterListOption SelectedLotteryPrizeList
    {
        get => FindOption(PrizeListOptions, _binding.LotteryPrizeListName);
        set => SetName(_binding.LotteryPrizeListName, value?.Name, name => _binding.LotteryPrizeListName = name,
            nameof(SelectedLotteryPrizeList));
    }

    private void SetName(string current, string? next, Action<string> assign, string propertyName)
    {
        next ??= string.Empty;
        if (string.Equals(current, next, StringComparison.Ordinal))
            return;
        assign(next);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        _persist();
    }

    private static CourseRosterListOption FindOption(IReadOnlyList<CourseRosterListOption> options, string name) =>
        options.FirstOrDefault(option => string.Equals(option.Name, name, StringComparison.Ordinal)) ?? options[0];

    private static IReadOnlyList<CourseRosterListOption> AddMissingOption(
        IReadOnlyList<CourseRosterListOption> options,
        params string[] assignedNames)
    {
        var result = options.ToList();
        foreach (var name in assignedNames.Where(name => !string.IsNullOrWhiteSpace(name)))
            if (result.All(option => !string.Equals(option.Name, name, StringComparison.Ordinal)))
                result.Add(new CourseRosterListOption(name, name + " (missing)"));
        return result;
    }
}
