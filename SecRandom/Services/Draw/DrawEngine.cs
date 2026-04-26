using System;
using System.Collections.Generic;
using System.Linq;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Models.Profile;
using SecRandom.Enums;
using SecRandom.Models;
using SecRandom.Models.Draw;
using SecRandom.Services.Config;

namespace SecRandom.Services.Draw;

public partial class DrawEngine
{
    private MainConfigModel configData = IAppHost.GetService<MainConfigHandler>().Data;
    private StudentHistory studentHistory = IAppHost.GetService<IProfileService>().CurrentStudentHistory;
    private PrizeHistory prizeHistory = IAppHost.GetService<IProfileService>().CurrentPrizeHistory;
    private StudentList studentList = IAppHost.GetService<IProfileService>().CurrentStudentList;
    private PrizeList prizeList = IAppHost.GetService<IProfileService>().CurrentPrizeList;

}
