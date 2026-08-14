using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using SecRandom.Helpers;
using SecRandom.ViewModels.MainPages;

namespace SecRandom.Views.MainPages;

/// <summary>Shared desktop/mobile projection of the desktop roll-call result model.</summary>
public sealed partial class RollCallResultPresenter : UserControl
{
    private readonly ItemsControl _resultPresenter;
    private RollCallPageViewModel? _viewModel;

    public RollCallResultPresenter()
    {
        InitializeComponent();
        _resultPresenter = this.FindControl<ItemsControl>("ResultPresenter")!;
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += (_, _) => AttachViewModel();
        DetachedFromVisualTree += (_, _) => DetachViewModel();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        AttachViewModel();
    }

    private void AttachViewModel()
    {
        var viewModel = DataContext as RollCallPageViewModel;
        if (ReferenceEquals(_viewModel, viewModel))
            return;

        DetachViewModel();
        _viewModel = viewModel;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += ViewModelOnPropertyChanged;
    }

    private void DetachViewModel()
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= ViewModelOnPropertyChanged;
        _viewModel = null;
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var viewModel = _viewModel;
        if (viewModel is null)
            return;

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render).GetTask();
                if (e.PropertyName == nameof(RollCallPageViewModel.PreviewAnimationRevision))
                    await DrawAnimationHelper.PreviewAsync(_resultPresenter, viewModel.AnimationStyle,
                        viewModel.PreviewAnimationDuration);
                else if (e.PropertyName == nameof(RollCallPageViewModel.ResultAnimationRevision))
                    await DrawAnimationHelper.RevealAsync(_resultPresenter, viewModel.AnimationEnabled,
                        viewModel.AnimationStyle, viewModel.AnimationDuration);
            }
            catch
            {
                // A presentation animation must not affect the completed draw.
            }
        }, DispatcherPriority.Render);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
