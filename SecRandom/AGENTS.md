# SecRandom/ AGENTS.md

<!--
App-layer supplement to ../AGENTS.md. Update this file when folders, Host registrations,
page IDs, localization folders, or window/navigation flows change. AI agents touching those
areas must update this file in the same task.
-->

## OVERVIEW

Avalonia app layer: app lifetime, DI composition root, windows, views/viewmodels, app-local services, assets, and page
localization.

## STRUCTURE

```
SecRandom/
├── App.axaml(.cs)       # Application bootstrap, Host registration, windows, shutdown/restart
├── App.Consts.cs        # App UI constants/support flags
├── Views/               # Main shell, settings shell, pages, windows
├── ViewModels/          # App VM state; root holds shell/profile bases
│   ├── MainPages/       # Page-specific VMs for built-in main pages and floating-window quick draw
│   └── SettingsPages/
│       └── History/     # History settings VMs (RollCallHistoryViewModel, LotteryHistoryViewModel)
├── Services/            # App-only services
│   ├── Config/          # DesktopConfigService
│   ├── CrashRecovery/   # Crash detection, recovery prompt, restart guard
│   ├── Desktop/         # TaskBarIconService
│   ├── Draw/            # DrawAudioService, DrawTemporaryRecordService
│   ├── Plugins/         # Plugin runtime: manager, catalog, invoker, state
│   ├── Profiles/        # ProfileService
│   ├── Settings/        # SettingsSearchService
│   ├── Telemetry/       # SentryTelemetrySdkAdapter, TelemetryRuntimeService
│   ├── Voice/           # VoiceAnnouncementService
│   └── OnlineStatusService.cs  # Root-level status reporter
├── Models/              # App-local view/support models
├── Helpers/             # App-local helpers
├── Converters/          # App-local Avalonia converters
├── Langs/               # Per-page resx localization + generated designers
├── Assets/              # Avalonia resources, icons, MiSans font, banners
├── Controls/            # App-specific controls; shared controls belong in Core
└── Styles.axaml         # Includes Core style bundle
```

## WHERE TO LOOK

| Task                         | Location                                                                | Notes                                                                                                                    |
|------------------------------|-------------------------------------------------------------------------|--------------------------------------------------------------------------------------------------------------------------|
| Register VM/service/page     | `App.axaml.cs` `BuildHost()`                                            | Strong convention; Host is source of truth.                                                                              |
| Main app window flow         | `App.ShowMainWindow()`, `Views/MainView.axaml.cs`                       | `FloatingWindow` is desktop main window; main view opens separately.                                                     |
| Settings flow                | `App.ShowSettingsWindow()`, `Views/SettingsView.axaml.cs`               | Settings has navigation history and restart prompt.                                                                      |
| Profile settings window      | `App.ShowProfileSettingsWindow()`, `Views/ProfileSettingsView.axaml.cs` | Profile list/history management window.                                                                                  |
| Crash recovery               | `Services/CrashRecovery/`, `Views/CrashRecoveryWindow.axaml(.cs)`, `Langs/CrashRecovery/` | Desktop fatal/dispatcher crash restart, crash report prompt, and feedback issue helpers.                                  |
| Add main page                | `Views/MainPages/`, `BuildHost()`                                       | `[PageInfo]` + `AddMainPage<T>()`; built-in main navigation currently includes roll-call, lottery, and history. Quick draw opens from the floating window. |
| Main page ViewModels         | `ViewModels/MainPages/`                                                 | Page-specific VMs for built-in main pages and floating-window quick draw; XAML compiled bindings must use this namespace. |
| History settings ViewModels  | `ViewModels/SettingsPages/History/`                                     | VMs used by roll-call/lottery history settings pages embedded in settings and main history views.                          |
| Add settings page            | `Views/SettingsPages/`, `Langs/SettingsPages/`, `App.axaml.cs`          | `[PageInfo]` + localization folder + `AddSettingsPage<T>()`; update title/resource wiring if language refresh is needed. General pages currently include `Basic`, `Privacy`, and `Backup`; v2-parity root entries include floating window, notification, security, linkage, voice, theme/background, history, update, logs, and more settings. |
| Log viewer                   | `Views/SettingsPages/LogViewer/LogViewerSettingsPage.axaml(.cs)`       | Hidden settings page `settings.logs`; opened from the settings more-options menu. Reads `.log` and `.log.gz` files from `data/logs`. |
| App config JSON              | `Services/Config/DesktopConfigService.cs`                               | Core handlers call into this app-specific storage.                                                                       |
| Searchable settings metadata | `Services/Settings/SettingsSearchService.cs`                            | Reflects `Langs.SettingsPages.*` resources and registered settings pages. Matches by `Type.Name` so pages in subdirectories are found correctly. |
| Plugin runtime               | `Services/Plugins/`                                                     | Imports `.srpx` packages, scans `data/plugins`, stores enable/restart state, starts enabled plugins, filters plugin logs, and exposes restricted host invokers. |
| Profile persistence          | `Services/Profiles/ProfileService.cs`                                   | Current lists/history, active point-call list/history switching, and `SaveProfile()`; list items carry hidden `RecordId` identity. |
| Voice announcements          | `Services/Voice/VoiceAnnouncementService.cs`, `Controls/AttachedSettings/SpecificAnnouncementAttachedSettingsControl.axaml(.cs)` | App-layer TTS runtime; voice/music settings choose system or Edge TTS, while per-student/per-prize alias/prefix/suffix live in list-page attached settings. |
| Draw audio / temp records    | `Services/Draw/DrawAudioService.cs`, `Services/Draw/DrawTemporaryRecordService.cs` | App-layer draw audio playback and session-scoped temporary draw records.                                                |
| Taskbar icon                 | `Services/Desktop/TaskBarIconService.cs`                                | App taskbar icon lifecycle; hosted service registered in `BuildHost()`.                                                 |
| Telemetry runtime seam       | `Services/Telemetry/`                                                   | App-layer-only Sentry policy/runtime lifecycle boundary; reads and live-applies `PrivacySettings.SentryTelemetryEnabled`.  |
| Online status reporting      | `Services/OnlineStatusService.cs`                                       | Host-managed SECTL online status reporter; reads `PrivacySettings.OnlineStatusMode`.                                      |

## CONVENTIONS

- `BuildHost()` registers logging, config, services, windows/views, ViewModels, attached settings controls, and
  navigation pages.
- Crash recovery startup prompt handling runs before single-instance acquisition; normal app restart must release `SingleInstanceService` before launching the replacement process.
- Telemetry runtime policy belongs in app-layer services and should live-apply `MainConfigHandler.Data.General.PrivacySettings.SentryTelemetryEnabled`; do not move SDK-specific wiring into Core or Shared. The concrete Sentry adapter stays under `SecRandom/Services/Telemetry/SentryTelemetrySdkAdapter.cs`.
- Background app services such as `OnlineStatusService` are registered through Host and must honor `PrivacySettings.OnlineStatusMode` before doing network work.
- `IVoiceAnnouncementService` is app-layer because Edge TTS playback and Windows SAPI/MCI integration are platform/runtime concerns. Per-record TTS alias/prefix/suffix belongs in attached settings on `Student`/`Prize`, not in a standalone settings page.
- Plugin runtime services are app-layer only and are registered in `BuildHost()`. Enabled plugin pages must be configured before Host build so keyed navigation can instantiate them.
- `.srpx` plugin files are ZIP packages containing exactly one `plugin.json`; import extracts to a temporary directory, validates the manifest, then copies into `data/plugins/<plugin-id>`.
- Plugin runtime config/data exposed to plugins is private per plugin under `data/configs/plugins/<plugin-id>` via `PluginInfo.ConfigDirectory` and `IPluginRuntimeContext.DataDirectory`.
- Plugin enable/disable changes require `SettingsView.RequestRestartApp()`; the persisted restart flag is cleared on the next app startup after the change is applied.
- Plugin logs are not separate files. They write through the existing `ILogger` pipeline with category prefix `SecRandom.Plugin[<plugin-id>].`; plugin detail UI must only show entries for the selected plugin.
- Plugin draw access is invocation-only through `IPluginDrawInvoker`; do not pass `DrawEngine`, mutable profile history, draw config, or random sources into plugin contexts.
- Picking `ClearRecord` is about app-layer temporary draw records, not persistent profile histories. RollCall and QuickDraw share student temporary records; Lottery uses prize temporary records.
- ViewModels must be registered in Host and inherit `ViewModelBase`; `ViewModelBase` exposes `Config`.
- Keep shell/profile/base ViewModels in `ViewModels/`; page-specific main ViewModels belong in `ViewModels/MainPages/`, and history settings page ViewModels belong in `ViewModels/SettingsPages/History/`.
- Use `IAppHost.GetService<T>()` for existing service resolution patterns in views and services.
- Views usually set `DataContext = this` and expose a `ViewModel` property.
- Main default page: `main.rollCall`; settings default page: `settings.home`.
- The roll-call main page is bottom-pinned in the main window sidebar (`PageLocation.Bottom`), full-width, and title-hidden. Keep its page chrome controlled by `MoreSettings`.
- Lottery main page ID is `main.lottery`; quick draw is not registered as a main navigation page, but its settings page remains `settings.picking.quickDraw`.
- More settings includes roll-call and lottery page management options for the control panel side and per-control visibility; wire built-in draw pages through `MainConfigModel.MoreSettings` instead of duplicating local UI flags.
- Page IDs follow root rules: `main.xxx`, `settings.xxx`, `settings.group.xxx`.
- V2-parity settings pages currently use root IDs such as `settings.personalized.floatingWindow`, `settings.notification`, `settings.general.security`, `settings.linkage`, `settings.notification.voiceMusic`, `settings.personalized.theme`, `settings.history`, `settings.update`, and `settings.more`; register them in `BuildHost()` instead of manually editing navigation UI. Hidden pages like `settings.logs` still need Host registration even though they are not shown in the sidebar.
- The log viewer page ID is `settings.logs`; keep it hidden from the settings sidebar and route the settings more-options “查看日志” command through navigation instead of opening a separate window.
- Toasts are available from views via `this.ShowWarningToast(...)` / `ShowErrorToast(...)`; shell views inject
  `AppToastAdorner` on load.
- `SettingsView.RequestRestartApp()` is the pattern for settings that require app restart.
- `Styles.axaml` should stay a thin include of `avares://SecRandom.Core/StylesBase.axaml` unless app-only styling is
  needed.

## LOCALIZATION

- Each page has its own folder under `Langs/`.
- Crash recovery has an app-level localization folder at `Langs/CrashRecovery/` because the prompt is a top-level window, not a settings page.
- Privacy settings localization lives under `Langs/SettingsPages/General/Privacy/` and follows the same base `.resx` + designer registration pattern as other settings pages.
- List management pages currently include roll-call lists (`data/list/roll_call_list`) and lottery prize pools (`data/list/lottery_list`). Student/prize number columns are optional; import must only require the name column.
- Roll-call and lottery list/history files are stored as plain JSON on disk; keep their `.json` paths stable and use `DesktopConfigService` instead of direct serialization.
- Required files for a localized resource set: `Resources.resx` and `Resources.Designer.cs`; culture files such as
  `Resources.en-US.resx` / `Resources.ja-JP.resx` are optional and must keep exact on-disk casing.
- Register only base `.resx` and designer in `SecRandom.csproj` using existing `EmbeddedResource` / `Compile` pattern.
- Use `PublicResXFileCodeGenerator` for resource designers.
- Settings keys: `S_`, `S_xxx_D`, `S_xxx_R`; options `O_`; messages `M_`; controls/content `C_`.

## COMMENTS

- Keep comments that describe lifecycle, DI, localization, or platform constraints.
- Avoid comments that simply repeat obvious UI event names or assignments.

## ANTI-PATTERNS

- Do not add a page class without `[PageInfo]` and Host registration.
- Do not add a settings page without its localization folder.
- Do not instantiate reusable services directly in pages.
- Do not expose raw Host, app services, writable profile/config services, shell execution, or broad filesystem access to plugins.
- Do not assume config dictionary/list mutations auto-save; call save at the mutation boundary or unload.
- Do not reintroduce required student/prize number columns in list management; `Id` is optional metadata and `RecordId` is the internal identity.
- Do not put reusable controls/styles here when they are intended for Core consumers.
- Debug-only pages (`CameraPreviewTestPage`, `DebugSettingsPage`) intentionally skip localization folders; do not remove
  their `#if DEBUG` guards.
