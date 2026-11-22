using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecRandom.Models;
using SR = SecRandom.Langs.MainPages.History.Resources;

namespace SecRandom.ViewModels.SettingsPages.History;

/// <summary>对筛选后的完整结果排序，再展示当前页。</summary>
public sealed class HistoryPagination : ObservableObject
{
    private const int PageSize = 50;
    private List<HistoryDisplayRow> _allRows = [];
    private string? _sortMember;
    private ListSortDirection _sortDirection;
    private int _page = 1;
    private string _pageInput = "1";

    public HistoryPagination()
    {
        PreviousCommand = new RelayCommand(() => Page--, () => Page > 1);
        NextCommand = new RelayCommand(() => Page++, () => Page < PageCount);
    }

    public ObservableCollection<HistoryDisplayRow> Rows { get; } = [];
    public IRelayCommand PreviousCommand { get; }
    public IRelayCommand NextCommand { get; }
    public int PageCount => Math.Max(1, (_allRows.Count + PageSize - 1) / PageSize);
    public string Status => string.Format(CultureInfo.CurrentCulture, SR.C_PageStatus, Page, PageCount, _allRows.Count);

    public string PageInput
    {
        get => _pageInput;
        set
        {
            if (!SetProperty(ref _pageInput, value))
                return;

            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out var page))
            {
                Page = page;
                SetPageInput(Page);
            }
        }
    }

    public int Page
    {
        get => _page;
        set
        {
            var page = Math.Clamp(value, 1, PageCount);
            if (SetProperty(ref _page, page))
                ShowPage();
            SetPageInput(Page);
        }
    }

    public void CommitPageInput()
    {
        if (int.TryParse(PageInput, NumberStyles.Integer, CultureInfo.CurrentCulture, out var page))
            Page = page;
        else
            SetPageInput(Page);
    }

    public void SetRows(IEnumerable<HistoryDisplayRow> rows)
    {
        _allRows = rows.ToList();
        ApplySort();
        _page = 1;
        SetPageInput(Page);
        ShowPage();
    }

    public void Sort(string? member)
    {
        if (string.IsNullOrWhiteSpace(member) || typeof(HistoryDisplayRow).GetProperty(member) is null)
            return;

        _sortDirection = _sortMember == member && _sortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;
        _sortMember = member;
        ApplySort();
        _page = 1;
        SetPageInput(Page);
        ShowPage();
    }

    private void SetPageInput(int page)
    {
        var text = page.ToString(CultureInfo.CurrentCulture);
        if (_pageInput == text)
            return;

        _pageInput = text;
        OnPropertyChanged(nameof(PageInput));
    }

    private void ApplySort()
    {
        var property = typeof(HistoryDisplayRow).GetProperty(_sortMember ?? string.Empty);
        if (property is null) return;
        _allRows = (_sortDirection == ListSortDirection.Ascending
                ? _allRows.OrderBy(row => property.GetValue(row))
                : _allRows.OrderByDescending(row => property.GetValue(row)))
            .ToList();
    }

    private void ShowPage()
    {
        Rows.Clear();
        foreach (var row in _allRows.Skip((Page - 1) * PageSize).Take(PageSize))
            Rows.Add(row);
        OnPropertyChanged(nameof(Page));
        OnPropertyChanged(nameof(PageCount));
        OnPropertyChanged(nameof(Status));
        PreviousCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
    }
}
