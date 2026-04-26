using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Models.Profile;
using SecRandom.Models;
using SecRandom.Services.Config;

namespace SecRandom.Services.Draw;

public partial class DrawEngine
{
    private readonly MainConfigHandler configHandler = IAppHost.GetService<MainConfigHandler>();
    private readonly IProfileService profileService = IAppHost.GetService<IProfileService>();

    private MainConfigModel configData => configHandler.Data;
    private StudentHistory studentHistory => profileService.CurrentStudentHistory ?? new StudentHistory();
    private PrizeHistory prizeHistory => profileService.CurrentPrizeHistory ?? new PrizeHistory();
    private StudentList studentList => profileService.CurrentStudentList ?? new StudentList();
    private PrizeList prizeList => profileService.CurrentPrizeList ?? new PrizeList();

}
