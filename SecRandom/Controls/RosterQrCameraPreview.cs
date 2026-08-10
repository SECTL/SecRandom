using Avalonia.Controls;
using CameraView;

namespace SecRandom.Controls;

/// <summary>
/// Defers CameraView construction until a platform camera provider needs a live preview.
/// </summary>
public sealed class RosterQrCameraPreview : ContentControl
{
    private CameraViewControl? _cameraView;

    internal CameraViewControl GetOrCreateCameraView()
    {
        if (_cameraView is not null)
            return _cameraView;

        _cameraView = new CameraViewControl();
        Content = _cameraView;
        return _cameraView;
    }
}
