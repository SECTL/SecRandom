using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Models;

namespace SecRandom.Core.Services.Config;

public class MainConfigHandler : ConfigHandlerBase<MainConfigModel>
{
    public MainConfigHandler(ILogger<MainConfigHandler> logger, ConfigServiceBase configService)
        : base(logger, configService, () => new MainConfigModel())
    {
        PersistNormalizedDetectorSettings();
    }

    public override void Reload()
    {
        base.Reload();
        PersistNormalizedDetectorSettings();
    }

    private void PersistNormalizedDetectorSettings()
    {
        if (Data.FaceDetectorSettings.DetectorTypeWasNormalized)
            Save();
    }
}
