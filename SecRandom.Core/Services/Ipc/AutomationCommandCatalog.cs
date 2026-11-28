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
        new("system.ping", "2.0", [], "-", "{pong,serverTime}"),
        new("system.info", "2.0", [], "-", "{version,protocol,platform,product}"),
        new("system.capabilities", "2.0", [], "-", "{protocol,maxFrameBytes,eventPipe,methods}"),
        new("system.integration.status", "2.0", [], "-", "{urlProtocolEnabled,automationIpcEnabled,singleInstancePipe,eventPipe}"),

        new("state.windows", "2.0", [AutomationScopes.ReadState], "-", "{main,settings,floating,simpleMode}"),
        new("state.draw", "2.0", [AutomationScopes.ReadState], "-", "{rollCall,lottery,quickDraw}"),
        new("state.runtime", "2.0", [AutomationScopes.ReadState], "-", "{uptimeSeconds,simpleMode,lotteryEnabled,drawing}"),

        new("window.show", "2.0", [AutomationScopes.WriteWindow], "{target?,page?}", "{target,action,page?,preview?}"),
        new("window.hide", "2.0", [AutomationScopes.WriteWindow], "{target?}", "{target,action,page?,preview?}"),
        new("window.toggle", "2.0", [AutomationScopes.WriteWindow], "{target?}", "{target,action,page?,preview?}"),
        new("window.navigate", "2.0", [AutomationScopes.WriteWindow], "{target?,page}", "{target,action,page,preview?}"),

        new("settings.schema", "2.0", [AutomationScopes.ReadSettings], "-", "{revision,schema}"),
        new("settings.get", "2.0", [AutomationScopes.ReadSettings], "-", "{values}"),
        new("settings.set", "2.0", [AutomationScopes.WriteSettings], "{patch}", "settings patch outcome"),
        new("settings.page.open", "2.0", [AutomationScopes.WriteSettings], "{page}", "{target,action,page,preview}"),

        new("roster.lists", "2.0", [AutomationScopes.ReadRoster], "-", "{students,prizes}"),
        new("roster.read", "2.0", [AutomationScopes.ReadRoster], "{roster_kind?,list_name?,include_disabled?}", "{roster_kind,lists}"),
        new("roster.write", "2.0", [AutomationScopes.WriteRoster], "{roster_kind?,list_name,mode?,activate?,students|prizes}", "roster write outcome"),
        new("roster.activate", "2.0", [AutomationScopes.WriteRoster], "{kind?,list}", "{activated,kind,list}"),

        new("draw.rollCall.trigger", "2.0", [AutomationScopes.WriteDraw], "{list?,count?,gender?,group?}", "{target,count,list,drawn}"),
        new("draw.rollCall.configure", "2.0", [AutomationScopes.WriteDraw], "{list?,count?,gender?,group?}", "{target,list,count,gender,group}"),
        new("draw.rollCall.start", "2.0", [AutomationScopes.WriteDraw], "-", "{target,state}"),
        new("draw.rollCall.stop", "2.0", [AutomationScopes.WriteDraw], "-", "{target,state}"),
        new("draw.rollCall.reset", "2.0", [AutomationScopes.WriteDraw], "-", "{target,state}"),
        new("draw.quick.trigger", "2.0", [AutomationScopes.WriteDraw], "-", "{target,count,list,drawn}"),
        new("draw.lottery.trigger", "2.0", [AutomationScopes.WriteDraw], "{list?,count?,conditions?}", "{target,count,list,drawn}"),
        new("draw.lottery.configure", "2.0", [AutomationScopes.WriteDraw], "{list?,count?,gender?,group?}", "{target,list,count,gender,group}"),
        new("draw.lottery.start", "2.0", [AutomationScopes.WriteDraw], "-", "{target,state}"),
        new("draw.lottery.stop", "2.0", [AutomationScopes.WriteDraw], "-", "{target,state}"),
        new("draw.lottery.reset", "2.0", [AutomationScopes.WriteDraw], "-", "{target,state}"),
        new("draw.result", "2.0", [AutomationScopes.ReadHistory], "{target?}", "{target,students|prizes|student}"),
        new("draw.await", "2.0", [AutomationScopes.ReadHistory], "{target?,timeout_ms?}", "{target,students|prizes|student}"),

        new("history.query", "2.0", [AutomationScopes.ReadHistory], "{kind?,name}", "{entries}"),
        new("history.summary", "2.0", [AutomationScopes.ReadHistory], "{kind?,name}", "{kind,name,rounds,count}"),

        new("events.subscribe", "2.0", [AutomationScopes.ReadEvents], "-", "{pipe,protocol,maxSubscribers}"),
        new("events.unsubscribe", "2.0", [AutomationScopes.ReadEvents], "-", "{unsubscribed}"),
        new("events.status", "2.0", [AutomationScopes.ReadEvents], "-", "{sequence,pipe,maxSubscribers}")
    ];

    public static AutomationCommandDescriptor? Find(string method) =>
        Methods.FirstOrDefault(m => string.Equals(m.Name, method, StringComparison.Ordinal));
}
