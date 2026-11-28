using SecRandom.Core.Models.SubConfigs.General;

namespace SecRandom.Core.Services.FeatureConfiguration;

/// <summary>Capabilities represented by the desktop startup feature profile editor.</summary>
public static class StartupFeatureCatalog
{
    public const string Plugins = "plugins";
    public const string CourseLinkage = "course-linkage";
    public const string ControlNode = "control-node";
    public const string Integrations = "integrations";
    public const string AutomaticBackup = "automatic-backup";
    public const string AutomaticUpdates = "automatic-updates";
    public const string Lottery = "lottery";
    public const string Voice = "voice";
    public const string Notifications = "notifications";
    public const string FullscreenNotifications = "fullscreen-notifications";
    public const string Timer = "timer";
    public const string Music = "draw-music";
    public const string QuickDraw = "quick-draw";
    public const string RollCall = "roll-call";
    public const string RandomNumber = "random-number-draw";
    public const string Orchestration = "orchestrated-draw";
    public const string History = "history";
    public const string RosterManagement = "roster-management";
    public const string Verification = "fairness-verification";
    public const string FloatingWindow = "floating-window";
    public const string DataArchive = "data-archive";
    public const string FullProfile = "builtin.full";
    public const string SimpleProfile = "builtin.simple";

    public static FeatureGraph Graph { get; } = new([
        // Core capabilities stay visible in the profile editor but cannot be disabled.
        new(RollCall, [], Required: true, NameKey: "RollCall", DescriptionKey: "RollCallDescription", CategoryKey: "Core"),
        new(RandomNumber, [], Required: true, NameKey: "RandomNumber", DescriptionKey: "RandomNumberDescription", CategoryKey: "Core"),
        new(Orchestration, [], Required: true, NameKey: "Orchestration", DescriptionKey: "OrchestrationDescription", CategoryKey: "Core"),
        new(History, [], Required: true, NameKey: "History", DescriptionKey: "HistoryDescription", CategoryKey: "Data"),
        new(RosterManagement, [], Required: true, NameKey: "RosterManagement", DescriptionKey: "RosterManagementDescription", CategoryKey: "Data"),
        new(Verification, [], Required: true, NameKey: "Verification", DescriptionKey: "VerificationDescription", CategoryKey: "Data"),
        new(FloatingWindow, [], Required: true, NameKey: "FloatingWindow", DescriptionKey: "FloatingWindowDescription", CategoryKey: "Window"),
        new(DataArchive, [], Required: true, NameKey: "DataArchive", DescriptionKey: "DataArchiveDescription", CategoryKey: "Data"),
        new(Plugins, [], NameKey: "Plugins", DescriptionKey: "PluginsDescription", CategoryKey: "Extension"),
        new(CourseLinkage, [], NameKey: "CourseLinkage", DescriptionKey: "CourseLinkageDescription", CategoryKey: "Extension"),
        new(ControlNode, [], NameKey: "ControlNode", DescriptionKey: "ControlNodeDescription", CategoryKey: "Extension"),
        new(Integrations, [], NameKey: "Integrations", DescriptionKey: "IntegrationsDescription", CategoryKey: "Extension"),
        new(AutomaticBackup, [], NameKey: "AutomaticBackup", DescriptionKey: "AutomaticBackupDescription", CategoryKey: "System"),
        new(AutomaticUpdates, [], NameKey: "AutomaticUpdates", DescriptionKey: "AutomaticUpdatesDescription", CategoryKey: "System"),
        new(Lottery, [], NameKey: "Lottery", DescriptionKey: "LotteryDescription", CategoryKey: "Draw"),
        new(Voice, [], NameKey: "Voice", DescriptionKey: "VoiceDescription", CategoryKey: "Sound"),
        new(Notifications, [], NameKey: "Notifications", DescriptionKey: "NotificationsDescription", CategoryKey: "Sound"),
        new(FullscreenNotifications, [Notifications], NameKey: "Fullscreen", DescriptionKey: "FullscreenDescription", CategoryKey: "Sound"),
        new(Timer, [], NameKey: "Timer", DescriptionKey: "TimerDescription", CategoryKey: "Window"),
        new(Music, [], NameKey: "Music", DescriptionKey: "MusicDescription", CategoryKey: "Sound"),
        new(QuickDraw, [], NameKey: "QuickDraw", DescriptionKey: "QuickDrawDescription", CategoryKey: "Draw")
    ]);

    public static IReadOnlySet<string> ResolveLegacyMode(bool simpleMode) =>
        Graph.Resolve(simpleMode ? [QuickDraw, ControlNode, Integrations, AutomaticBackup, AutomaticUpdates] : Graph.Features.Select(feature => feature.Id));

    public static string ResolveSelectedProfileId(SimpleModeSettingsConfig settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var selected = settings.SelectedProfileId;
        if (selected is FullProfile or SimpleProfile ||
            (selected is not null && settings.Profiles?.Any(profile => profile?.Id == selected) == true))
            return selected;
        return settings.Enabled ? SimpleProfile : FullProfile;
    }

    public static IReadOnlySet<string> Resolve(SimpleModeSettingsConfig settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.SelectedProfileId is null)
            return ResolveLegacyMode(settings.Enabled);
        if (settings.SelectedProfileId == FullProfile)
            return ResolveLegacyMode(false);
        if (settings.SelectedProfileId == SimpleProfile)
            return ResolveLegacyMode(true);

        var profile = settings.Profiles?.FirstOrDefault(item => item?.Id == settings.SelectedProfileId);
        if (profile is null)
            return ResolveLegacyMode(settings.Enabled);

        var registered = Graph.Features.Select(feature => feature.Id).ToHashSet(StringComparer.Ordinal);
        return Graph.Resolve((profile.EnabledFeatures ?? []).Where(registered.Contains));
    }
}
