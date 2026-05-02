using OpenCvSharp;

namespace SecRandom.Core.Services.Camera;

public partial class CameraDrawEngine
{
    private async Task workerLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using (var capture = new VideoCapture())
            {

            }
        }
    }
}
