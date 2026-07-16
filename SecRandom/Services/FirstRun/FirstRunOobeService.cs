using System;
using SecRandom.Core.Services.Config;

namespace SecRandom.Services.FirstRun;

public sealed class FirstRunOobeService(MainConfigHandler configHandler)
{
    public const int CurrentPrivacyPolicyVersion = 1;
    public const int CurrentGplVersion = 1;
    public const int CurrentVerificationNoticeVersion = 1;

    public bool IsRequired()
    {
        return IsPrivacyPolicyOnlyRequired() || !configHandler.Data.General.Basic.GuideCompleted;
    }

    public bool IsPrivacyPolicyOnlyRequired()
    {
        var basic = configHandler.Data.General.Basic;
        return basic.GuideCompleted &&
                (Math.Max(basic.AcceptedGplVersion, basic.AcceptedEulaVersion) < CurrentGplVersion ||
                  basic.AcceptedPrivacyPolicyVersion < CurrentPrivacyPolicyVersion ||
                  basic.AcceptedVerificationNoticeVersion < CurrentVerificationNoticeVersion);
    }

    public void Complete()
    {
        var basic = configHandler.Data.General.Basic;
        basic.AcceptedEulaVersion = CurrentGplVersion;
        basic.AcceptedPrivacyPolicyVersion = CurrentPrivacyPolicyVersion;
        basic.AcceptedGplVersion = CurrentGplVersion;
        basic.AcceptedVerificationNoticeVersion = CurrentVerificationNoticeVersion;
        basic.GuideCompleted = true;
        configHandler.Save();
    }
}
