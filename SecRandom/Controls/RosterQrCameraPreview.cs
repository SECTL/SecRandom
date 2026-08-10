using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CameraView;

namespace SecRandom.Controls;

/// <summary>
/// Defers CameraView construction until a platform camera provider needs a live preview.
/// </summary>
public sealed class RosterQrCameraPreview : ContentControl
{
    private CameraViewControl? _cameraView;
    private Image? _imagePreview;

    internal CameraViewControl GetOrCreateCameraView()
    {
        if (_cameraView is not null)
            return _cameraView;

        _cameraView = new CameraViewControl();
        Content = _cameraView;
        return _cameraView;
    }

    /// <summary>
    /// Displays a captured JPEG frame for camera backends without a native Avalonia preview.
    /// </summary>
    public async Task ShowFrameAsync(byte[] frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (Dispatcher.UIThread.CheckAccess())
        {
            ShowFrame(frame);
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() => ShowFrame(frame));
    }

    private void ShowFrame(byte[] frame)
    {
        if (_cameraView is not null)
            return;

        _imagePreview ??= new Image { Stretch = Stretch.UniformToFill };
        if (!ReferenceEquals(Content, _imagePreview))
            Content = _imagePreview;

        using var stream = new MemoryStream(frame, writable: false);
        var bitmap = new Bitmap(stream);
        var previous = _imagePreview.Source;
        _imagePreview.Source = bitmap;
        (previous as IDisposable)?.Dispose();
    }
}
