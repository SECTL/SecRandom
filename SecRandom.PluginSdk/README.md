# SecRandom Plugin SDK

SDK for developing in-process SecRandom plugins. Plugins reference `SecRandom.Core` through this SDK and register services, settings pages, main pages, or other Core extensions from their entry point.

## API version

- `manifest.yml` `apiVersion` declares which host API the plugin targets.
- The host rejects plugins whose `apiVersion` major is below `PluginApiVersions.Current.Major` (currently `3`, host API `3.1.5`).
- `apiVersion` follows the application major version and must be bumped with it.
- `version` in `manifest.yml` is the plugin's own version and is independent of `apiVersion`.

Example manifest:

```yaml
id: secrandom.example
name: SecRandom 示例插件
entranceAssembly: SecRandom.ExamplePlugin.dll
apiVersion: 3.0.0
version: 1.0.0
author: SECTL
permissions:
  - storage
  - notification
```

## Permissions

`permissions` in `manifest.yml` declares what the plugin intends to use. Names come from `PluginPermissionNames` / `PluginPermissions`: `none`, `ui`, `storage`, `network`, `clipboard.read`, `clipboard.write`, `file.picker`, `settings`, `draw`, `history`, `overlay` (case, `_`, `-` and `.` are ignored, so `clipboardRead` and `clipboard-read` both work).

- Omitting `permissions` means "not declared": `ICapabilityService.GetPermissions` reports `IsDeclared = false` and every capability is allowed — exactly how plugins behaved before host API 3.1.5.
- `[none]` declares "nothing needed" and is the only way to deny everything.
- Unknown names are dropped, not rejected, so a plugin may ship a permission before the host knows it.
- This is a declarative policy for the host UI and the plugin market, **not a security boundary**: plugins run in-process with full trust.


## Referencing the SDK

Published plugins reference the SDK package and exclude its runtime assets so the host supplies Core and its dependencies:

```xml
<PackageReference Include="SecRandom.PluginSdk" Version="3.1.0">
  <ExcludeAssets>runtime;native</ExcludeAssets>
</PackageReference>
```

The repository template defaults `UseLocalPluginSdk=true` so solution builds work before the SDK is published; set `UseLocalPluginSdk=false` with a NuGet source that contains `SecRandom.PluginSdk` to exercise the release packaging path.

## Building a plugin package

Set `<CreateSrpx>true</CreateSrpx>` to produce `srpx/<ProjectName>.srpx` after every build. The package is a ZIP whose root contains `manifest.yml`, the entrance assembly, and any external package dependencies. Place it in `data/cache/plugin-packages` and restart the desktop application to install.

## Extension points (host API 3.1.5)

Register these from `PluginBase.Initialize(HostBuilderContext, IServiceCollection)` — the host collects them through `IEnumerable<T>`, so registration order does not matter. Resolving them imperatively (`IAppHost.TryGetService<T>()`) also works once the host is built.

### Present draw results yourself

```csharp
public sealed class MyPresenter : IDrawResultPresenter
{
    public string Id => "com.example.presenter";
    public string DisplayName => "示例结果呈现";
    public int Priority => 100;                       // higher wins
    public bool CanPresent(DrawPresentationRequest request)
        => request.Phase == DrawPresentationPhase.Reveal && request.Students.Count > 0;

    public Task<DrawPresentationDecision> PresentAsync(
        DrawPresentationRequest request, CancellationToken cancellationToken = default)
    {
        // request: Channel, Phase, Students, Prizes, AssignedStudents, ListName, PrizeListName,
        //          GroupScope, GenderScope, CourseName, RequestedCount, ProofId, DrawRoundId, DrawTime
        ShowMyResultWindow(request);
        return Task.FromResult(DrawPresentationDecision.Taken());
    }
}

// in PluginBase.Initialize
services.AddSingleton<IDrawResultPresenter, MyPresenter>();
```

- Channels: `RollCall`, `Lottery`, `QuickDraw`, `RemoteDraw`, `MobileDraw`. Phases: `Preview`, `Reveal`.
- `CanPresent` returning false, `NotHandled`, or an exception hands that step back to the host (the host also skips a presenter that throws).
- `Taken()` makes the host skip its own reveal animation for that draw. Pass `Taken(hideHostResult: true)` to also hide the host's own result control while you present.
- `OperationCanceledException` is never swallowed; cancellation stops the presentation chain.

### Show your own overlay inside the main window

```csharp
var overlays = IAppHost.GetService<IOverlayHostService>();
var id = overlays.Show(new OverlayOptions
{
    Content = new MyResultPanel(),
    ShowBackdrop = true,
    BackdropColor = "#66000000",
    DismissOnClickOutside = true,
    DismissOnEscape = true,
    BlockInput = true
});

overlays.OverlayClosed += (_, e) => { /* e.Id, e.ClosedByUser */ };
overlays.Close(id);        // or overlays.CloseAll()
```

Calls are marshalled to the UI thread. On shells without an overlay layer (mobile/headless) `IsSupported` is false and `Show` returns an id without displaying anything.

### Contribute content to host-defined slots

```csharp
public sealed class TitleBarBadge : UiContentContributionBase
{
    public override string Id => "com.example.badge";
    public override string SlotId => HostUiSlots.MainTitleBar;
    public override UiSlotKind Kind => UiSlotKind.Append;   // Append / Prepend / Replace / Hide
    public override Control? CreateContent(UiSlotContext context) => new MyBadge();
}

services.AddSingleton<IUiContentContribution, TitleBarBadge>();
```

Built-in slots: `HostUiSlots.MainTitleBar`, `HostUiSlots.RollCallResultExtra`, `HostUiSlots.LotteryResultExtra`, `HostUiSlots.QuickDrawResultExtra`. Contributions can also be added or removed at runtime (`IUiContributionService.Add` / `Remove`); `Hide` makes the slot's default content disappear, `Replace` swaps it for the first contribution that builds successfully.

### Restyle the host UI

```csharp
public sealed class SoftTheme : UiStyleContributionBase
{
    public override string Id => "com.example.soft";
    public override string DisplayName => "示例配色";
    public override IReadOnlyDictionary<string, object?> Resources => new Dictionary<string, object?>
    {
        ["SystemAccentColor"] = Color.Parse("#FF66CCFF")
    };
}

services.AddSingleton<IUiStyleContribution, SoftTheme>();
```

Resources and styles are applied to `Application.Styles` / `Application.Resources` in `Priority` order (low to high), re-applied when the theme variant changes, and removed again on `Remove`; `IUiStyleService.Refresh()` forces a re-apply. Override `AppliesTo(ThemeVariant)` to target only light or dark.

### Keep private state under the plugin's config folder

```csharp
var storage = IAppHost.GetService<IPluginStorageFactory>().Create("com.example.plugin");
storage.WriteJson("state.json", myState);
var loaded = storage.ReadJson<MyState>("state.json");
```

`IPluginStorage` offers `PluginId`, `RootDirectory`, `GetPath(params string[])`, `Exists`, `ListFiles(relativeDirectory = "", recursive = false)`, `ReadText`, `WriteText`, `ReadBytes`, `WriteBytes`, `ReadJson<T>`, `WriteJson<T>`, `Delete` and `GetUsedBytes()`. Every path is relative to the plugin's own config directory; escapes (`..`, absolute paths, drive letters) throw `UnauthorizedAccessException`. `TryCreate` reports an invalid plugin id by returning false instead of throwing.

### Read lists and prize pools

```csharp
var lists = IAppHost.GetService<IListQueryService>();
foreach (var name in lists.GetStudentListNames())
    Use(lists.GetStudentList(name)!.Candidates);   // StudentSnapshot: Name, Group, Gender, Id, RecordId, Tags, Exists, IsCandidate
lists.Changed += (_, e) => { /* e.ListName, e.IsPrizeList */ };
```

Read-only: `GetStudentListNames` / `GetPrizeListNames` / `GetStudentList(name)` / `GetPrizeList(name)` / `GetCurrentStudentList()` / `GetCurrentPrizeList()` / `Refresh()` / `Changed`. Queries never switch the host's current list; `Refresh()` re-reads disk and raises `Changed` on the UI thread.

### Notify the user, or ask a question

```csharp
var notifications = IAppHost.GetService<INotificationService>();
notifications.Show("已保存", NotificationSeverity.Success);
var ok = await notifications.ConfirmAsync("要清空记录吗？", "确认", "确认", "取消", cancellationToken);
var name = await notifications.PromptAsync("输入名字", "重命名", "默认名字", cancellationToken);
```

`Show(string message, NotificationSeverity severity = Info, string? title = null)`, `Show(NotificationOptions)` (message, severity, title, duration, plugin id), `ConfirmAsync`, `PromptAsync` (returns null when cancelled). `IsSupported` is false on shells without a main view, and every call then no-ops (`ConfirmAsync` returns false).

### Show a busy indicator

```csharp
var busy = IAppHost.GetService<IBusyIndicator>();
using (busy.Begin("正在导入…", progress: 0))
{
    busy.Report("正在解析名单…", 0.4);
    busy.Report("正在写入…", 0.8);
}
```

`Begin` returns an `IDisposable`; the indicator closes when the last one is disposed and never closes another plugin's overlay.

### Marshal work to the UI thread

```csharp
var ui = IAppHost.GetService<IUiScheduler>();
await ui.InvokeAsync(() => Title = "完成", UiPriority.Render);
```

`IsOnUiThread`, `Post`, `InvokeAsync`, `InvokeAsync<T>`, `DelayAsync`, `RunOnUiThreadAsync` — all taking an optional `UiPriority` (`Background`, `Normal`, `Render`, `Immediate`). Headless hosts run everything inline, so plugin code never needs `Dispatcher.UIThread.CheckAccess()`.

### Check capabilities and declared permissions

```csharp
var capabilities = IAppHost.GetService<ICapabilityService>();
if (capabilities.Has("overlay.host")) { /* ... */ }
var permissions = capabilities.GetPermissions("com.example.plugin");   // IsDeclared / Permissions / Allows(...)
```

`HostApiVersion`, `Capabilities`, `Has(id)`, `IsAllowed(pluginId, permission)`. Capability ids: `draw.presenter`, `overlay.host`, `ui.contribution`, `ui.style`, `plugin.storage`, `list.query`, `notification`, `busy.indicator`, `ui.scheduler`, `window.service`, `popup.service`, `draw.pipeline`, `theme.tokens`, `localization`, `event.bus`, `diagnostics`.

### Open a window or dialog

```csharp
var windows = IAppHost.GetService<IWindowService>();
var result = await windows.ShowDialogAsync(
    new WindowRequest { Id = "settings", Content = new MyPanel(), Title = "设置", Width = 480 }, cancellationToken);
windows.Close("settings", result);
```

`Show`, `ShowDialogAsync`, `Activate(id)`, `Close(id, object? result = null)`, `CloseAll()`, `OpenWindowIds`, `WindowClosed` (`WindowClosedEventArgs(Id, ClosedByUser)`). Reusing an `Id` activates the existing window instead of opening a second one; `IsSupported` is false where the shell cannot host windows.

### Pop up UI without caring about the shell

```csharp
var popups = IAppHost.GetService<IPopupService>();
var handle = popups.Show(new PopupOptions { Content = new MyPanel(), ShowBackdrop = true });
popups.Close(handle.Id);   // handle.UsedWindow tells you which surface was used
```

Desktop uses the in-window overlay layer; shells without one fall back to a separate window unless `PreferSeparateWindow` is set. `CloseAll` closes only this service's own popups.

### Filter candidates and post-process results

```csharp
public sealed class OnlyThirdYear : IDrawCandidateFilter
{
    public string Id => "com.example.third-year";
    public int Priority => 100;                    // filters run low to high
    public bool AppliesTo(DrawPipelineContext context) => context.Channel == DrawPresentationChannel.RollCall;
    public DrawCandidateSet? Filter(DrawPipelineContext context, DrawCandidateSet candidates)
        => candidates with { Students = candidates.Students.Where(s => s.Group == "三年级").ToArray() };
}

services.AddSingleton<IDrawCandidateFilter, OnlyThirdYear>();
```

`DrawPipelineContext` carries `Channel`, `ListName`, `PrizeListName`, `GroupScope`, `GenderScope`, `CourseName`, `RequestedCount` and `DrawMethod`. Filters run **before** the draw, so previews, the sealed draw proof and history all cover the filtered candidates; a filter that throws is skipped with a warning.

`IDrawResultPostProcessor` runs **after** the winners are drawn and returns `DrawResultEdit(Students, Prizes, Note, SuppressHistory, SuppressPresentation, OverrideRoundId)`: annotate the result (`Note`), skip host history (`SuppressHistory = true`), ask the host to skip its reveal animation (`SuppressPresentation = true`), or pin the round id (`OverrideRoundId`). Rewriting `Students`/`Prizes` is ignored with a warning because the draw proof is already sealed.

### Read theme colors instead of hard-coded keys

```csharp
var theme = IAppHost.GetService<IThemeTokenService>();
if (theme.TryGetColor("SystemAccentColor", out var accent)) Use(accent);
theme.Changed += (_, _) => Repaint(theme.IsDark);
```

`CurrentTheme` (`ThemeKind.Light` / `Dark`), `IsDark`, `TokenKeys`, `TryGetColor`, `GetColor(tokenKey, fallback)`, `TryGetResource`, `GetResource`, `Changed`.

### Localize your own UI

```csharp
var localization = IAppHost.GetService<ILocalizationService>();
localization.RegisterStrings("com.example.plugin", new Dictionary<string, string>
{
    ["title"] = "Example",
    ["zh-CN:title"] = "示例"
});
var title = localization.GetForPlugin("com.example.plugin", "title", "Example");
```

`CurrentCulture`, `AvailableCultures`, `Get(key, fallback)`, `Format(key, fallback, args)`, `GetForPlugin(pluginId, key, fallback)`, `Changed`. A culture-prefixed key (`"ja-JP:title"`) overrides that language only.

### Talk to other plugins

```csharp
var bus = IAppHost.GetService<IPluginEventBus>();
using var subscription = bus.Subscribe<HostEvents.DrawCompleted>(e => Log(e.RoundId));
bus.Publish(new MyPluginEvent(/* ... */));
```

`Subscribe<TEvent>(Action<TEvent>)`, `Subscribe<TEvent>(Func<TEvent, CancellationToken, Task>)`, `Publish<TEvent>`, `PublishAsync<TEvent>`, `GetSubscriberCount<TEvent>()`. The returned `IDisposable` unsubscribes; subscriber exceptions are swallowed and logged. Host events live in `HostEvents`: `DrawCompleted`, `PluginLoaded`, `ProfileChanged`, `ThemeChanged`, `AppStarted`, `AppStopping`.

### Report diagnostics

```csharp
IAppHost.GetService<IPluginDiagnosticsService>()
    .Report("com.example.plugin", "import", "名单解析完成", DiagnosticLevel.Info);
```

`Report(pluginId, area, message, level = Info)`, `GetEntries(pluginId = null, maximumCount = 200)`, `GetLoadedPlugins()`, `Changed`. Entries are kept in memory (latest 500, newest first) and mirrored to the host log.

### Run code on application shutdown

```csharp
public sealed class MyShutdownParticipant : IAppShutdownParticipant
{
    public string Id => "com.example.plugin";
    public int Priority => 50;                      // lower runs first
    public ValueTask OnShuttingDownAsync(CancellationToken cancellationToken = default) => FlushAsync(cancellationToken);
}

services.AddSingleton<IAppShutdownParticipant, MyShutdownParticipant>();
```

The host runs participants in `Priority` order with a 5-second overall budget; a participant that throws does not block the others.

### Already available before 3.1

`IAppNavigationService`, `IAppLifecycleService`, `IPluginDrawService` (plugins must draw through it — never call `IDrawCommitService` directly), `IMainView` (`OpenDrawer` / `CloseDrawer` / `NavigateToPage`), `ISettingsView`, `IFloatingWindowButtonRegistry`, the page registry (`AddMainPage` / `AddSettingsPage` / separators / groups), and the roll-call / lottery algorithm registries.