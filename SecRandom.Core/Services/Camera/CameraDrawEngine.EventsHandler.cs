using System.ComponentModel;
using FaceDetectorSettingsConfig = SecRandom.Core.Models.SubConfigs.Picking.FaceDetectorSettingsConfig;

namespace SecRandom.Core.Services.Camera;

public partial class CameraDrawEngine
{
    private void RequestCameraRestart()
    {
        if (!IsPreviewRunning) return;
        RequireCameraRestart = true;
    }

    private void RequestDetectorReload()
    {
        if (!IsPreviewRunning) return;
        RequireDetectorReload = true;
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            //需要重启摄像头的时候
            case nameof(FaceDetectorSettingsConfig.CameraSource):
                RequestCameraRestart();
                break;
            case nameof(FaceDetectorSettingsConfig.DetectorType):
            case nameof(FaceDetectorSettingsConfig.ModelInputHeight):
            case nameof(FaceDetectorSettingsConfig.ModelInputWidth):
                RequestDetectorReload();
                break;
        }
    }

    private void OnResolutionMapChanged(object? sender, PropertyChangedEventArgs e)
    {
        RequestCameraRestart();
    }
}