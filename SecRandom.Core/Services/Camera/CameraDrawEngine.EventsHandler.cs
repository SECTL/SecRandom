using System.ComponentModel;
using SecRandom.Core.Models.SubConfigs;

namespace SecRandom.Core.Services.Camera;

public partial class CameraDrawEngine
{
    private void requestCameraRestart()
    {
        if (!isPreviewRunning) return;
        requireCameraRestart = true;
    }

    private void requestDetectorReload()
    {
        if (!isPreviewRunning) return;
        requireDetectorReload = true;
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            //需要重启摄像头的时候
            case nameof(FaceDetectorSettingsConfig.CameraSource):
                requestCameraRestart();
                break;
            case nameof(FaceDetectorSettingsConfig.DetectorType):
            case nameof(FaceDetectorSettingsConfig.ModelInputHeight):
            case nameof(FaceDetectorSettingsConfig.ModelInputWidth):
                requestDetectorReload();
                break;
        }
    }

    private void OnResolutionMapChanged(object? sender, PropertyChangedEventArgs e)
    {
        requestCameraRestart();
    }
}
