using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Draw;
using SecRandom.Core.Views;

namespace SecRandom.Mobile.Views;

/// <summary>
/// MVE route for the mobile draw surface. The physical root owns only chrome.
/// </summary>
public sealed class MobileDrawView : ViewBase
{
    private readonly IProfileService _profileService;
    private readonly IDrawTemporaryRecordService _temporaryRecordService;
    private readonly IFeatureAvailabilityService _featureAvailabilityService;
    private readonly MainConfigHandler _configHandler;
    private readonly DrawEngine _drawEngine;
    private readonly IRollCallSession _rollCallSession;
    private readonly ILotterySession _lotterySession;
    private DrawSurface _surface = DrawSurface.RollCall;

    public MobileDrawView(
        IProfileService profileService,
        IDrawTemporaryRecordService temporaryRecordService,
        IFeatureAvailabilityService featureAvailabilityService,
        MainConfigHandler configHandler,
        DrawEngine drawEngine,
        IRollCallSession rollCallSession,
        ILotterySession lotterySession)
    {
        _profileService = profileService;
        _temporaryRecordService = temporaryRecordService;
        _featureAvailabilityService = featureAvailabilityService;
        _configHandler = configHandler;
        _drawEngine = drawEngine;
        _rollCallSession = rollCallSession;
        _lotterySession = lotterySession;
        Render();
    }

    private void Render()
    {
        Content = new MobileDrawPage(
            _profileService,
            _temporaryRecordService,
            _featureAvailabilityService,
            _configHandler,
            _drawEngine,
            _rollCallSession,
            _lotterySession,
            _surface,
            SelectSurface,
            static () => { });
    }

    private void SelectSurface(DrawSurface surface)
    {
        if (surface == DrawSurface.Lottery && !_featureAvailabilityService.IsLotteryEnabled)
            return;

        _surface = surface;
        Render();
    }
}
