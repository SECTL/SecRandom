using SecRandom.Models;
using SecRandom.ViewModels.SettingsPages.History;

namespace SecRandom.Mobile.Tests;

public sealed class HistoryPaginationTests
{
    [Fact]
    public void PagesCoverAllRowsAndResetWhenResultsChange()
    {
        var pagination = new HistoryPagination();
        pagination.SetRows(Enumerable.Range(0, 123).Select(i => new HistoryDisplayRow { TotalCount = i }));

        Assert.Equal(3, pagination.PageCount);
        Assert.Equal(50, pagination.Rows.Count);
        pagination.NextCommand.Execute(null);
        Assert.Equal(50, pagination.Rows[0].TotalCount);
        pagination.Page = 3;
        Assert.Equal(23, pagination.Rows.Count);
        Assert.False(pagination.NextCommand.CanExecute(null));

        pagination.SetRows(Enumerable.Range(0, 3).Select(i => new HistoryDisplayRow { TotalCount = i }));
        Assert.Equal(1, pagination.Page);
        Assert.Equal(1, pagination.PageCount);
        Assert.Equal(3, pagination.Rows.Count);
        Assert.False(pagination.PreviousCommand.CanExecute(null));
        pagination.Page = 99;
        Assert.Equal(1, pagination.Page);
    }

    [Fact]
    public void SortingAppliesToAllRowsBeforePaging()
    {
        var pagination = new HistoryPagination();
        pagination.SetRows(Enumerable.Range(0, 101).Reverse().Select(i => new HistoryDisplayRow { TotalCount = i }));

        pagination.Sort(nameof(HistoryDisplayRow.TotalCount));
        Assert.Equal(0, pagination.Rows[0].TotalCount);
        pagination.Page = 3;
        Assert.Equal(100, Assert.Single(pagination.Rows).TotalCount);

        pagination.Sort(nameof(HistoryDisplayRow.TotalCount));
        Assert.Equal(1, pagination.Page);
        Assert.Equal(100, pagination.Rows[0].TotalCount);
        pagination.Page = 3;
        Assert.Equal(0, Assert.Single(pagination.Rows).TotalCount);
    }
}
