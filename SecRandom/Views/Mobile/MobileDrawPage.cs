using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System.ComponentModel;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Services.ViewEngine;
using SecRandom.ViewModels.MainPages;
using RollCallResources = SecRandom.Langs.MainPages.RollCall.Resources;
using LotteryResources = SecRandom.Langs.MainPages.Lottery.Resources;

namespace SecRandom.Views.Mobile;

/// <summary>
/// Mobile layout shell over the desktop draw sessions. Draw state, result projection, and animations remain shared.
/// </summary>
public sealed partial class MobileDrawPage : UserControl
{
    private readonly IFeatureAvailabilityService _featureAvailability;
    private readonly TabStrip _drawSurfaceTabs;
    private readonly Control _rollCallSurface;
    private readonly Control _lotterySurface;
    private bool _synchronizingSurface;

    public MobileDrawPage(
        RollCallPageViewModel rollCallViewModel,
        LotteryPageViewModel lotteryViewModel,
        IFeatureAvailabilityService featureAvailability)
    {
        RollCallViewModel = rollCallViewModel;
        LotteryViewModel = lotteryViewModel;
        _featureAvailability = featureAvailability;
        DataContext = this;
        InitializeComponent();
        _drawSurfaceTabs = this.FindControl<TabStrip>("DrawSurfaceTabs")!;
        _rollCallSurface = this.FindControl<Control>("RollCallSurface")!;
        _lotterySurface = this.FindControl<Control>("LotterySurface")!;
        _featureAvailability.Changed += FeatureAvailabilityOnChanged;
        RollCallViewModel.PropertyChanged += DrawViewModelOnPropertyChanged;
        LotteryViewModel.PropertyChanged += DrawViewModelOnPropertyChanged;
        DetachedFromVisualTree += (_, _) =>
        {
            _featureAvailability.Changed -= FeatureAvailabilityOnChanged;
            RollCallViewModel.PropertyChanged -= DrawViewModelOnPropertyChanged;
            LotteryViewModel.PropertyChanged -= DrawViewModelOnPropertyChanged;
        };
        RefreshLotteryAvailability();
    }

    public RollCallPageViewModel RollCallViewModel { get; }
    public LotteryPageViewModel LotteryViewModel { get; }
    public bool IsLotteryEnabled => _featureAvailability.IsLotteryEnabled;
    public bool CanChangeSurface => !RollCallViewModel.IsDrawing && !LotteryViewModel.IsDrawing;

    private void DrawSurfaceTabs_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Avalonia raises the initial selection event while InitializeComponent is still populating
        // the control tree, before the named fields have been assigned by the constructor.
        if (_drawSurfaceTabs is null || !ReferenceEquals(sender, _drawSurfaceTabs))
            return;

        if (_synchronizingSurface || _drawSurfaceTabs.SelectedIndex != 1 || IsLotteryEnabled)
        {
            SetSurface(_drawSurfaceTabs.SelectedIndex == 1 && IsLotteryEnabled);
            return;
        }

        SetSurface(false);
    }

    private void FeatureAvailabilityOnChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(RefreshLotteryAvailability);

    private void DrawViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RollCallPageViewModel.IsDrawing))
            _drawSurfaceTabs.IsEnabled = CanChangeSurface;
    }

    private void RefreshLotteryAvailability()
    {
        this.FindControl<TabStripItem>("LotteryTab")!.IsVisible = IsLotteryEnabled;
        if (!IsLotteryEnabled)
        {
            // Cancels only the visual/audio preview; the shared transactional draw is allowed to finish its commit.
            LotteryViewModel.StopProtocolDraw();
            SetSurface(false);
        }
    }

    private void SetSurface(bool lottery)
    {
        _synchronizingSurface = true;
        try
        {
            _drawSurfaceTabs.SelectedIndex = lottery && IsLotteryEnabled ? 1 : 0;
            _rollCallSurface.IsVisible = !lottery || !IsLotteryEnabled;
            _lotterySurface.IsVisible = lottery && IsLotteryEnabled;
        }
        finally
        {
            _synchronizingSurface = false;
        }
    }

    private void ShowRollCallRemaining_OnClick(object? sender, RoutedEventArgs e)
    {
        RollCallViewModel.RefreshRemainingList();
        _ = SecRandom.Core.Abstraction.IAppHost.GetService<RemainingListViewService>().ShowAsync(
            RollCallResources.C_RemainingListTitle, RollCallViewModel.RemainingItems, RollCallResources.M_NoRemainingStudents);
    }

    private void ShowLotteryRemaining_OnClick(object? sender, RoutedEventArgs e)
    {
        LotteryViewModel.RefreshRemainingList();
        _ = SecRandom.Core.Abstraction.IAppHost.GetService<RemainingListViewService>().ShowAsync(
            LotteryResources.C_RemainingListTitle, LotteryViewModel.RemainingItems, LotteryResources.M_NoRemainingPrizes);
    }

    private void ClearRollCallTemporaryRecords_OnClick(object? sender, RoutedEventArgs e)
    {
        IAppHost.GetService<IDrawTemporaryRecordService>().ClearStudentList(RollCallViewModel.SelectedStudentListName);
        RollCallViewModel.RefreshAfterProfileChange();
    }

    private void ClearLotteryTemporaryRecords_OnClick(object? sender, RoutedEventArgs e)
    {
        var temporaryRecords = IAppHost.GetService<IDrawTemporaryRecordService>();
        temporaryRecords.ClearPrizeList(LotteryViewModel.SelectedPrizeListName);
        if (LotteryViewModel.IsStudentAssignmentEnabled)
            temporaryRecords.ClearStudentList(LotteryViewModel.SelectedStudentListName);
        LotteryViewModel.RefreshAfterProfileChange();
    }

    private void OpenRollCallListSettings_OnClick(object? sender, RoutedEventArgs e) =>
        App.ShowSettingsWindow("settings.listManagement.rollCallList");

    private void OpenLotteryListSettings_OnClick(object? sender, RoutedEventArgs e) =>
        App.ShowSettingsWindow("settings.listManagement.lotteryList");

    private void OpenLotterySettings_OnClick(object? sender, RoutedEventArgs e) =>
        App.ShowSettingsWindow("settings.picking.lottery");

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
