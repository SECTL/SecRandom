namespace SecRandom.Core.Services.Ipc;

/// <summary>
/// One method descriptor. This is the single source of truth shared by the local v2 IPC
/// registry, the URL adapter and (later) the control-plane dispatcher.
/// </summary>
public sealed record AutomationCommandDescriptor(
    string Name,
    string Since,
    IReadOnlyList<string> Scopes,
    string Params,
    string Result);

/// <summary>
/// The v2 method catalog. Descriptors are intentionally hand-written rather than reflected
/// from handler signatures: the wire contract is public API and must not change because a
/// C# parameter was renamed.
/// </summary>
public static class AutomationCommandCatalog
{
    public const string ProtocolVersion = "2.0";

    public static IReadOnlyList<AutomationCommandDescriptor> Methods { get; } =
    [
        new("system.ping", "2.0", [], "-", "{pong,serverTimeUtc}"),
        new("system.info", "2.0", [], "-", "{appVersion,platform,protocolVersions,simpleMode,lotteryEnabled}"),
        new("system.capabilities", "2.0", [], "-", "{protocolVersions,features,methods:[...]}"),
        new("system.integration.status", "2.0", [], "-", "{urlRegistered,autostartEnabled,portable}"),

        new("state.windows", "2.0", [AutomationScopes.ReadState], "-", "{main,settings,floating,quickDraw}"),
        new("state.draw", "2.0", [AutomationScopes.ReadState], "-", "{rollCall,lottery,quickDraw}"),
        new("state.runtime", "2.0", [AutomationScopes.ReadState], "-", "{profile,linkage,control}"),

        new("window.show", "2.0", [AutomationScopes.WriteWindow], "{target,page?}", "{visible,page}"),
        new("window.hide", "2.0", [AutomationScopes.WriteWindow], "{target}", "{visible:false}"),
        new("window.toggle", "2.0", [AutomationScopes.WriteWindow], "{target}", "{visible}"),
        new("window.navigate", "2.0", [AutomationScopes.WriteWindow], "{target,page}", "{visible:true,page}"),

        new("settings.schema", "2.0", [AutomationScopes.ReadSettings], "-", "{revision,categories}"),
        new("settings.get", "2.0", [AutomationScopes.ReadSettings], "{paths?}", "{values,revision}"),
        new("settings.set", "2.0", [AutomationScopes.WriteSettings], "{patch}", "{values,requiresRestart,rejected?}"),
        new("settings.page.open", "2.0", [AutomationScopes.WriteSettings], "{page,preview?}", "{visible,page,preview}"),

        new("roster.lists", "2.0", [AutomationScopes.ReadRoster], "-", "{students,prizes,activeStudentList,activePrizePool}"),
        new("roster.read", "2.0", [AutomationScopes.ReadRoster], "{kind,list?}", "{lists,truncated}"),
        new("roster.write", "2.0", [AutomationScopes.WriteRoster], "{kind,list,mode,activate?,members}", "{list,added,updated,total,backup}"),
        new("roster.activate", "2.0", [AutomationScopes.WriteRoster], "{kind,list}", "{active}"),

        new("draw.rollCall.trigger", "2.0", [AutomationScopes.WriteDraw], "{list?,count?,gender?,group?}", "{status,reason,list,drawn}"),
        new("draw.rollCall.configure", "2.0", [AutomationScopes.WriteDraw], "{list?,count?,gender?,group?}", "{applied}"),
        new("draw.rollCall.start", "2.0", [AutomationScopes.WriteDraw], "-", "{accepted,state}"),
        new("draw.rollCall.stop", "2.0", [AutomationScopes.WriteDraw], "-", "{stopped}"),
        new("draw.rollCall.reset", "2.0", [AutomationScopes.WriteDraw], "-", "{reset}"),
        new("draw.quick.trigger", "2.0", [AutomationScopes.WriteDraw], "-", "{record}"),
        new("draw.lottery.trigger", "2.0", [AutomationScopes.WriteDraw], "{pool?,list?,count?,gender?,group?}", "{status,reason,list,drawn}"),
        new("draw.lottery.configure", "2.0", [AutomationScopes.WriteDraw], "{pool?,list?,count?,gender?,group?}", "{applied}"),
        new("draw.lottery.start", "2.0", [AutomationScopes.WriteDraw], "-", "{accepted,state}"),
        new("draw.lottery.stop", "2.0", [AutomationScopes.WriteDraw], "-", "{stopped}"),
        new("draw.lottery.reset", "2.0", [AutomationScopes.WriteDraw], "-", "{reset}"),
        new("draw.result", "2.0", [AutomationScopes.ReadHistory], "{channel}", "{roundId,records,at}"),
        new("draw.await", "2.0", [AutomationScopes.ReadHistory], "{roundId?,timeoutMs?}", "{completed,result?,pending?}"),

        new("history.query", "2.0", [AutomationScopes.ReadHistory], "{kind,name,from?,to?,limit?,offset?}", "{entries,total,hasMore}"),
        new("history.summary", "2.0", [AutomationScopes.ReadHistory], "{kind,name}", "{members,draws,lastAt}"),

        new("events.subscribe", "2.0", [AutomationScopes.ReadEvents], "{types?}", "{subscribed,seqStart}"),
        new("events.unsubscribe", "2.0", [AutomationScopes.ReadEvents], "-", "{subscribed}"),
        new("events.status", "2.0", [AutomationScopes.ReadEvents], "-", "{subscribed,queued,dropped}")
    ];

    public static AutomationCommandDescriptor? Find(string method) =>
        Methods.FirstOrDefault(m => string.Equals(m.Name, method, StringComparison.Ordinal));
}