using System.Globalization;
using System.Resources;

namespace SecRandom.Langs.SettingsPages.Voice;

public static class MiMoResources
{
    private static readonly ResourceManager ResourceManager = new(
        "SecRandom.Langs.SettingsPages.Voice.MiMoResources",
        typeof(MiMoResources).Assembly);

    public static string S_MiMoVoiceDesign => GetString(nameof(S_MiMoVoiceDesign));
    public static string S_MiMoVoiceDesign_D => GetString(nameof(S_MiMoVoiceDesign_D));
    public static string S_MiMoVoiceDesignPrompt => GetString(nameof(S_MiMoVoiceDesignPrompt));
    public static string S_MiMoVoiceDesignPrompt_D => GetString(nameof(S_MiMoVoiceDesignPrompt_D));
    public static string S_MiMoVoiceClone => GetString(nameof(S_MiMoVoiceClone));
    public static string S_MiMoVoiceClone_D => GetString(nameof(S_MiMoVoiceClone_D));
    public static string C_MiMoVoiceCloneSelect => GetString(nameof(C_MiMoVoiceCloneSelect));
    public static string C_MiMoVoiceCloneClear => GetString(nameof(C_MiMoVoiceCloneClear));
    public static string M_MiMoVoiceCloneConfigured => GetString(nameof(M_MiMoVoiceCloneConfigured));
    public static string M_MiMoVoiceCloneNotConfigured => GetString(nameof(M_MiMoVoiceCloneNotConfigured));
    public static string M_MiMoVoiceCloneImportFailed => GetString(nameof(M_MiMoVoiceCloneImportFailed));
    public static string M_MiMoVoiceCloneCleared => GetString(nameof(M_MiMoVoiceCloneCleared));
    public static string M_MiMoVoiceCloneConsentTitle => GetString(nameof(M_MiMoVoiceCloneConsentTitle));
    public static string M_MiMoVoiceCloneConsentContent => GetString(nameof(M_MiMoVoiceCloneConsentContent));
    public static string C_MiMoVoiceCloneConsent => GetString(nameof(C_MiMoVoiceCloneConsent));
    public static string C_MiMoVoiceCloneCancel => GetString(nameof(C_MiMoVoiceCloneCancel));

    private static string GetString(string name) =>
        ResourceManager.GetString(name, CultureInfo.CurrentUICulture) ?? name;
}
