using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Models;

namespace SecRandom.Services.Config;

public class MainConfigHandler : ConfigHandlerBase<MainConfigModel>
{
    public MainConfigHandler(ILogger<MainConfigHandler> logger, ConfigServiceBase configService)
        : base(logger, configService, () => new MainConfigModel())
    {
    }
}
