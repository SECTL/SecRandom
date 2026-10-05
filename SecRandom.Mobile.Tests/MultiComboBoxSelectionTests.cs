using System.Collections.Specialized;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SecRandom.Core.Controls;

namespace SecRandom.Mobile.Tests;

/// <summary>
///     Regression tests for the reported MultiComboBox crash:
///     <c>MultiComboBoxRemoveCommand</c> -> <c>MultiComboBox.Remove</c> -> <c>AvaloniaList.Remove</c>
///     -> <c>PanelContainerGenerator.OnItemsChanged</c> -> <c>AvaloniaList.get_Item</c> out of range.
/// </summary>
/// <remarks>
///     Avalonia's non-virtualizing <c>PanelContainerGenerator</c> indexes the panel's containers with the
///     index carried by the collection notification, so the tag panel must never observe a notification
///     that is already stale. That happened for two reasons: the control wrote <c>SelectedItems</c> twice for
///     one removal, and the tag list was bound straight to the mutable <c>SelectedItems</c> collection, which
///     a page-side change handler may rebuild re-entrantly (settings pages subscribe in their constructor,
///     i.e. before the panel's container generator exists).
/// </remarks>
public sealed class MultiComboBoxSelectionTests
{
    private static readonly Uri ThemeUri = new("avares://SecRandom.Core/Styles/MultiComboBox.axaml");

    [AvaloniaFact]
    public void RemovingLastSelectedTagUpdatesSelection()
    {
        var (window, box, selected) = CreateBox();

        Assert.Equal(3, GetTagPanel(window, box).Children.Count);

        box.Remove("C");

        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new object[] { "A", "B" }, selected);
        Assert.Equal(2, GetTagPanel(window, box).Children.Count);
        window.Close();
    }

    [AvaloniaFact]
    public void RemovingMiddleSelectedTagUpdatesSelection()
    {
        var (window, box, selected) = CreateBox();

        box.Remove("B");

        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new object[] { "A", "C" }, selected);
        Assert.Equal(2, GetTagPanel(window, box).Children.Count);
        window.Close();
    }

    /// <summary>
    ///     One ✕ click must change the bound collection exactly once; the old implementation removed the
    ///     item and then flipped the option container's <c>IsSelected</c>, which wrote the collection a
    ///     second time from inside the first removal.
    /// </summary>
    [AvaloniaFact]
    public void RemoveRaisesExactlyOneCollectionChange()
    {
        var (window, box, selected) = CreateBox();
        var changes = new List<NotifyCollectionChangedEventArgs>();
        selected.CollectionChanged += (_, e) => changes.Add(e);

        box.Remove("B");
        Dispatcher.UIThread.RunJobs();

        var change = Assert.Single(changes);
        Assert.Equal(NotifyCollectionChangedAction.Remove, change.Action);
        Assert.Equal(1, change.OldStartingIndex);
        window.Close();
    }

    /// <summary>
    ///     Removes the option container's selection state as well, so the dropdown does not keep showing an
    ///     option as picked after its tag was dismissed.
    /// </summary>
    [AvaloniaFact]
    public void RemoveDeselectsTheMatchingOptionContainer()
    {
        var (window, box, _) = CreateBox();

        var option = GetOptionContainer(box, "C");
        Assert.NotNull(option);
        Assert.True(option!.IsSelected);

        box.Remove("C");
        Dispatcher.UIThread.RunJobs();

        Assert.False(GetOptionContainer(box, "C")!.IsSelected);
        window.Close();
    }

    /// <summary>
    ///     Reproduces the crash: a page registers its handler on the bound collection in the page
    ///     constructor — before the control's tag panel generator exists — and rebuilds that collection from
    ///     the change handler. <c>SecuritySettingsPage.SynchronizeSelectedFactorOptions</c> and
    ///     <c>FloatingWindowSettingsPage.RebuildButtonOptions</c> both do <c>Clear()</c> + <c>Add()</c>.
    /// </summary>
    [AvaloniaFact]
    public void ReentrantPageListenerRebuildDoesNotThrow()
    {
        EnsureThemeLoaded();
        var selected = new AvaloniaList<object> { "A", "B", "C" };
        var reentering = false;

        selected.CollectionChanged += (_, _) =>
        {
            if (reentering)
                return;

            reentering = true;
            try
            {
                var keep = selected.Cast<object>().ToList();
                selected.Clear();
                foreach (var item in keep)
                    selected.Add(item);
            }
            finally
            {
                reentering = false;
            }
        };

        var (window, box, _) = CreateBox(selected);
        box.Remove("C");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new object[] { "A", "B" }, selected);
        Assert.Equal(2, GetTagPanel(window, box).Children.Count);
        window.Close();
    }

    /// <summary>The history-management page replaces the whole bound collection during a change.</summary>
    [AvaloniaFact]
    public void ReentrantPageListenerReplacingSelectedItemsDoesNotThrow()
    {
        EnsureThemeLoaded();
        var selected = new AvaloniaList<object> { "A", "B", "C" };
        MultiComboBox? live = null;
        var replaced = false;

        selected.CollectionChanged += (_, _) =>
        {
            if (replaced || live is null)
                return;

            replaced = true;
            live.SelectedItems = new AvaloniaList<object> { "A", "B" };
        };

        var (window, box, _) = CreateBox(selected);
        live = box;

        box.Remove("C");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, GetTagPanel(window, box).Children.Count);

        box.Remove("B");
        Dispatcher.UIThread.RunJobs();

        Assert.Single(GetTagPanel(window, box).Children);
        window.Close();
    }

    /// <summary>Replacing SelectedItems (page repopulate) must re-target the tag panel.</summary>
    [AvaloniaFact]
    public void ReplacingSelectedItemsRetargetsTheTagPanel()
    {
        var (window, box, _) = CreateBox();

        var next = new AvaloniaList<object> { "A", "B", "C", "D", "E" };
        box.SelectedItems = next;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(5, GetTagPanel(window, box).Children.Count);

        box.Remove("E");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(4, GetTagPanel(window, box).Children.Count);
        Assert.DoesNotContain("E", next);
        window.Close();
    }

    /// <summary>A view model may mutate the bound collection directly (RemoteDrawPage does).</summary>
    [AvaloniaFact]
    public void ExternalSelectionChangesTrackTheTagPanel()
    {
        var (window, box, selected) = CreateBox();

        selected.Remove("A");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, GetTagPanel(window, box).Children.Count);

        selected.Add("D");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(3, GetTagPanel(window, box).Children.Count);

        selected.Clear();
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(GetTagPanel(window, box).Children);
        box.Remove("D");
        window.Close();
    }

    private static (Window Window, MultiComboBox Box, AvaloniaList<object> Selected) CreateBox(
        AvaloniaList<object>? selected = null)
    {
        EnsureThemeLoaded();
        selected ??= new AvaloniaList<object> { "A", "B", "C" };
        var items = new AvaloniaList<object> { "A", "B", "C", "D", "E" };
        var box = new MultiComboBox
        {
            ItemsSource = items,
            SelectedItems = selected
        };
        var window = new Window
        {
            Width = 400,
            Height = 300,
            Content = box
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        // A real user opens the dropdown to pick options; that is what realizes the option containers.
        box.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();
        box.IsDropDownOpen = false;
        Dispatcher.UIThread.RunJobs();
        return (window, box, selected);
    }

    private static void EnsureThemeLoaded()
    {
        var resources = Application.Current!.Resources;
        if (resources.TryGetResource(typeof(MultiComboBox), null, out _))
            return;
        resources.MergedDictionaries.Add(
            (ResourceDictionary)AvaloniaXamlLoader.Load(ThemeUri));
    }

    private static Panel GetTagPanel(Window window, MultiComboBox box)
    {
        var tagList = window.GetVisualDescendants()
            .OfType<ItemsControl>()
            .First(control => !ReferenceEquals(control, box));
        var panel = tagList.Presenter?.Panel;
        Assert.NotNull(panel);
        return panel!;
    }

    private static MultiComboBoxItem? GetOptionContainer(MultiComboBox box, object item) =>
        box.Presenter?.Panel?.Children
            .OfType<MultiComboBoxItem>()
            .FirstOrDefault(container => ReferenceEquals(container.DataContext, item));
}
