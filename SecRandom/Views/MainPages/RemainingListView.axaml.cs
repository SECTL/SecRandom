using Avalonia.Markup.Xaml;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using SecRandom.Core.Views;
using SecRandom.Services.ViewEngine;

namespace SecRandom.Views.MainPages;

public sealed partial class RemainingListView : ViewBase
{
    public RemainingListView(RemainingListViewState state)
    {
        ViewTitle = state.Title;
        EmptyText = state.EmptyText;
        Items = state.Items;
        Header = ViewTitle;
        DataContext = this;
        InitializeComponent();
        if (Items.Count > 0 && this.FindControl<ItemsControl>("ItemsPresenter") is { } presenter)
        {
            presenter.ItemTemplate = Items[0] is SecRandom.ViewModels.MainPages.RollCallRemainingItem
                ? this.FindResource("RollCallRemainingItemTemplate") as IDataTemplate
                : this.FindResource("LotteryRemainingItemTemplate") as IDataTemplate;
        }
    }

    public string ViewTitle { get; }
    public string EmptyText { get; }
    public IReadOnlyList<object> Items { get; }
    public bool HasItems => Items.Count > 0;
    public bool IsEmpty => !HasItems;

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
