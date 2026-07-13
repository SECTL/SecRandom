using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Models;

namespace SecRandom.Core.Services.Config;

public class MainConfigHandler : ConfigHandlerBase<MainConfigModel>
{
    public MainConfigHandler(ILogger<MainConfigHandler> logger, ConfigServiceBase configService)
        : base(logger, configService, () => new MainConfigModel())
    {
        if (Data.FaceDetectorSettings.DetectorTypeWasNormalized)
            Save();
    }
}
