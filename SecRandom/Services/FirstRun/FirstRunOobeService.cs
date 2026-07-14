using SecRandom.Core.Services.Config;

namespace SecRandom.Services.FirstRun;

public sealed class FirstRunOobeService(MainConfigHandler configHandler)
{
    public const int CurrentEulaVersion = 1;

    public bool IsRequired()
    {
        var basic = configHandler.Data.General.Basic;
        return !basic.GuideCompleted || basic.AcceptedEulaVersion < CurrentEulaVersion;
    }

    public void Complete()
    {
        var basic = configHandler.Data.General.Basic;
        basic.AcceptedEulaVersion = CurrentEulaVersion;
        basic.GuideCompleted = true;
        configHandler.Save();
    }
}
