using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Services.Config;
using SecRandom.Models;
using SecRandom.Shared;
using SecRandom.Shared.Models.Profile;
using SR = SecRandom.Langs.MainPages.History.Resources;

namespace SecRandom.ViewModels;

public sealed partial class RollCallHistoryViewModel : ViewModelBase
{
    private readonly ILogger<RollCallHistoryViewModel> _logger =
        IAppHost.GetService<ILogger<RollCallHistoryViewModel>>();

    private StudentHistory? _history;
    private Dictionary<string, string> _studentIdMap     = [];  // name → id
    private Dictionary<string, string> _studentGenderMap = [];  // name → gender
    private Dictionary<string, string> _studentGroupMap  = [];  // name → group

    [ObservableProperty] private string? _selectedClassName;
    [ObservableProperty] private string _selectedMode = HistoryMode.Overview;

    public RollCallHistoryViewModel(MainConfigHandler configHandler) : base(configHandler)
    {
        RefreshCommand = new RelayCommand(Refresh);
        RefreshClassNames();
        SelectedClassName = ResolveInitial(ClassNames, Config.HistoryManagementSettings.SelectedClassName);
    }

    public ObservableCollection<string> ClassNames { get; } = [];
    public ObservableCollection<string> ModeOptions { get; } = [];
    public ObservableCollection<HistoryDisplayRow> Rows { get; } = [];
    public bool ShowWeight => Config.HistoryManagementSettings.SelectWeight;

    public IRelayCommand RefreshCommand { get; }

    private static string? ResolveInitial(IReadOnlyList<string> names, string preferred)
    {
        if (names.Count == 0) return null;
        return names.Contains(preferred) ? preferred : names[0];
    }

    public void Refresh()
    {
        RefreshClassNames();
        Load();
    }

    private void RefreshClassNames()
    {
        ClassNames.Clear();
        var dir = Utils.GetDirectoryPath("history", "roll_call_history");
        foreach (var f in Directory.GetFiles(dir, "*.json").OrderBy(Path.GetFileName))
            ClassNames.Add(Path.GetFileNameWithoutExtension(f));
    }

    partial void OnSelectedClassNameChanged(string? value) => Load();
    partial void OnSelectedModeChanged(string value) => BuildRows();

    private void Load()
    {
        Rows.Clear();
        _history = null;
        _studentIdMap     = [];
        _studentGenderMap = [];
        _studentGroupMap  = [];

        if (string.IsNullOrWhiteSpace(SelectedClassName))
        {
            RebuildModeOptions();
            return;
        }

        try
        {
            _history = new StudentHistoryConfig(SelectedClassName).Data;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "读取点名历史失败：班级={ClassName}。", SelectedClassName);
        }

        try
        {
            var list = new StudentListConfig(SelectedClassName).Data;
            _studentIdMap = list.Students
                .Where(s => !string.IsNullOrEmpty(s.Id))
                .ToDictionary(s => s.Name, s => s.Id, StringComparer.Ordinal);
            _studentGenderMap = list.Students
                .ToDictionary(s => s.Name, s => s.Gender, StringComparer.Ordinal);
            _studentGroupMap = list.Students
                .ToDictionary(s => s.Name, s => s.Group, StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取学生列表失败，学号列将为空：班级={ClassName}。", SelectedClassName);
        }

        RebuildModeOptions();
        BuildRows();
    }

    private void RebuildModeOptions()
    {
        var current = SelectedMode;
        ModeOptions.Clear();
        ModeOptions.Add(HistoryMode.Overview);
        ModeOptions.Add(HistoryMode.Records);

        if (_history != null)
            foreach (var key in _history.Students.Keys)
                ModeOptions.Add(key);

        if (!ModeOptions.Contains(current))
            SelectedMode = HistoryMode.Overview;
    }

    private void BuildRows()
    {
        Rows.Clear();
        if (_history == null) return;

        var students = _history.Students;
        var mode = SelectedMode;

        if (mode == HistoryMode.Overview)
        {
            foreach (var (name, h) in students)
            {
                Rows.Add(new HistoryDisplayRow
                {
                    Id         = _studentIdMap.GetValueOrDefault(name, string.Empty),
                    Name       = name,
                    Gender     = _studentGenderMap.GetValueOrDefault(name, string.Empty),
                    Group      = _studentGroupMap.GetValueOrDefault(name, string.Empty),
                    TotalCount = h.TotalCount,
                    Weight     = FormatLatestWeight(h)
                });
            }
        }
        else if (mode == HistoryMode.Records)
        {
            foreach (var (name, h) in students)
                foreach (var item in h.Histories)
                    Rows.Add(BuildEventRow(name, item, _studentIdMap.GetValueOrDefault(name, string.Empty)));
            SortByTimeDesc(Rows);
        }
        else if (students.TryGetValue(mode, out var target))
        {
            foreach (var item in target.Histories)
                Rows.Add(BuildEventRow(mode, item, _studentIdMap.GetValueOrDefault(mode, string.Empty)));
            SortByTimeDesc(Rows);
        }
    }

    private static HistoryDisplayRow BuildEventRow(string name, HistoryItem item, string id = "") =>
        new()
        {
            Id         = id,
            Name       = name,
            Gender     = item.DrawGender,
            Group      = item.DrawGroup,
            DrawTime   = item.DrawTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture),
            DrawMethod = item.DrawMethod == 0 ? SR.C_MethodRandom : SR.C_MethodWeight,
            DrawNumbers = item.DrawNumbers,
            Weight     = item.Weight.ToString("0.##", CultureInfo.CurrentCulture),
            SortTime   = item.DrawTime
        };

    private static string FormatLatestWeight(History h) =>
        h.Histories.LastOrDefault() is { } last
            ? last.Weight.ToString("0.##", CultureInfo.CurrentCulture)
            : string.Empty;

    private static void SortByTimeDesc(ObservableCollection<HistoryDisplayRow> rows)
    {
        var sorted = rows.OrderByDescending(r => r.SortTime).ToList();
        rows.Clear();
        foreach (var row in sorted) rows.Add(row);
    }
}
