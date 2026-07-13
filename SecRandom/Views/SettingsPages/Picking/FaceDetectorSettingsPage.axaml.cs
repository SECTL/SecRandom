using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs.Picking;
using SecRandom.Core.Services.Camera;
using SecRandom.Core.Services.Config;
using SecRandom.ViewModels;

namespace SecRandom.Views.SettingsPages.Picking;

[PageInfo("settings.picking.faceDetector", FluentIcons.VideoPersonSparkleFilled, "settings.picking")]
public partial class FaceDetectorSettingsPage : UserControl
{
    private readonly List<CameraOption> _cameraDevices = [];
    private bool _isLoading;

    public FaceDetectorSettingsPage()
    {
        Settings = ViewModel.Config.FaceDetectorSettings;
        DataContext = this;
        InitializeComponent();
        Settings.PropertyChanged += SettingsOnPropertyChanged;
        LoadCameraOptions();
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public FaceDetectorSettingsConfig Settings { get; }

    private MainConfigHandler ConfigHandler { get; } = IAppHost.GetService<MainConfigHandler>();

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        Settings.PropertyChanged -= SettingsOnPropertyChanged;
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        ConfigHandler.Save();
    }

    private void CameraComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || CameraComboBox.SelectedItem is not CameraOption camera)
            return;

        ApplyCameraSelection(camera);
    }

    private void ResolutionComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || CameraComboBox.SelectedItem is not CameraOption camera ||
            ResolutionComboBox.SelectedItem is not ResolutionOption resolution)
            return;

        SaveCameraDisplayResolution(camera, resolution);
    }

    private void LoadCameraOptions()
    {
        _isLoading = true;
        try
        {
            _cameraDevices.AddRange(CameraDrawEngine.GetAllCameraDevices()
                .Select(device => new CameraOption(
                    device.Name,
                    device.Source,
                    device.Resolutions
                        .OrderByDescending(resolution => resolution.Width * resolution.Height)
                        .ThenByDescending(resolution => resolution.Fps)
                        .Select(resolution => new ResolutionOption(
                            resolution.Width,
                            resolution.Height,
                            resolution.Fps))
                        .ToList())));
            CameraComboBox.ItemsSource = _cameraDevices;
            CameraComboBox.SelectedItem = _cameraDevices.FirstOrDefault(device => device.Source == Settings.CameraSource) ??
                                          _cameraDevices.FirstOrDefault();
        }
        finally
        {
            _isLoading = false;
        }

        if (CameraComboBox.SelectedItem is CameraOption camera)
            ApplyCameraSelection(camera);
    }

    private void ApplyCameraSelection(CameraOption camera)
    {
        Settings.CameraSource = camera.Source;
        PopulateResolutionOptions(camera);

        if (ResolutionComboBox.SelectedItem is ResolutionOption resolution)
            SaveCameraDisplayResolution(camera, resolution);
    }

    private void SaveCameraDisplayResolution(CameraOption camera, ResolutionOption resolution)
    {
        Settings.CameraDisplayResolutionMap[camera.Source] = $"{resolution.Width}x{resolution.Height}";
        ConfigHandler.Save();
    }

    private void PopulateResolutionOptions(CameraOption camera)
    {
        _isLoading = true;
        try
        {
            var resolutions = camera.Resolutions;
            ResolutionComboBox.ItemsSource = resolutions;
            Settings.CameraDisplayResolutionMap.TryGetValue(camera.Source, out var savedResolution);
            ResolutionComboBox.SelectedItem = resolutions.FirstOrDefault(resolution =>
                                                  $"{resolution.Width}x{resolution.Height}" == savedResolution) ??
                                              resolutions.FirstOrDefault(resolution => resolution.Width == 640 && resolution.Height == 480) ??
                                              resolutions.FirstOrDefault();
        }
        finally
        {
            _isLoading = false;
        }
    }

    private sealed record CameraOption(string Name, string Source, IReadOnlyList<ResolutionOption> Resolutions)
    {
        public override string ToString() => Name;
    }

    private sealed record ResolutionOption(int Width, int Height, double Fps)
    {
        public override string ToString() => Fps > 0
            ? $"{Width} x {Height} · {Fps:0.#} FPS"
            : $"{Width} x {Height}";
    }
}
