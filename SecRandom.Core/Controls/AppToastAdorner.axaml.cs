using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using SecRandom.Core.Models.UI;

namespace SecRandom.Core.Controls;

public partial class AppToastAdorner : UserControl
{
    public static readonly RoutedEvent<ShowToastEventArgs> ShowToastEvent =
        RoutedEvent.Register<AppToastAdorner, ShowToastEventArgs>(nameof(ShowToast), RoutingStrategies.Bubble);

    private Control? _control;

    public AppToastAdorner()
    {
        InitializeComponent();
    }

    public AppToastAdorner(Control control)
    {
        InitializeComponent();
        Attach(control);
    }

    public ObservableCollection<ToastMessage> Messages { get; } = [];

    // Provide CLR accessors for the event
    public event EventHandler<ShowToastEventArgs> ShowToast
    {
        add => AddHandler(ShowToastEvent, value);
        remove => RemoveHandler(ShowToastEvent, value);
    }

    public void Attach(Control control)
    {
        if (_control is not null)
        {
            _control.Unloaded -= ControlOnUnloaded;
            _control.RemoveHandler(ShowToastEvent, OnShowToast);
        }

        _control = control;
        control.AddHandler(ShowToastEvent, OnShowToast);
        control.Unloaded += ControlOnUnloaded;
    }

    private void ControlOnUnloaded(object? sender, EventArgs e)
    {
        if (_control is null) return;

        _control.Unloaded -= ControlOnUnloaded;
        _control.RemoveHandler(ShowToastEvent, OnShowToast);
        _control = null;
    }

    private void OnShowToast(object? sender, ShowToastEventArgs e)
    {
        Messages.Insert(0, e.Message);
        e.Message.ClosedCancellationTokenSource.Token.Register(() =>
        {
            DispatcherTimer.RunOnce(() => Messages.Remove(e.Message), TimeSpan.FromSeconds(0.3));
        });
        if (e.Message.AutoClose) DispatcherTimer.RunOnce(() => e.Message.Close(), e.Message.Duration);
    }

    [RelayCommand]
    private void CloseToast(ToastMessage message)
    {
        if (!Messages.Contains(message)) return;

        message.Close();
    }
}