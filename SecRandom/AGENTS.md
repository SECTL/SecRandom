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
├── ViewModels/          # App VM state; inherits ViewModelBase
├── Services/            # App-only services; desktop config storage, profiles, settings search, telemetry runtime seam
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
| Add main page                | `Views/MainPages/`, `BuildHost()`                                       | `[PageInfo]` + `AddMainPage<T>()`.                                                                                       |
| Add settings page            | `Views/SettingsPages/`, `Langs/SettingsPages/`, `App.axaml.cs`          | `[PageInfo]` + localization folder + `AddSettingsPage<T>()`; update title/resource wiring if language refresh is needed. General pages currently include `Basic`, `Privacy`, and `Backup`. |
| App config JSON              | `Services/Config/DesktopConfigService.cs`                               | Core handlers call into this app-specific storage.                                                                       |
| Searchable settings metadata | `Services/SettingsSearchService.cs`                                     | Reflects `Langs.SettingsPages.*` resources and registered settings pages.                                                |
| Profile persistence          | `Services/ProfileService.cs`                                            | Current lists/history and `SaveProfile()`.                                                                               |
| Telemetry runtime seam       | `Services/Telemetry/`                                                   | App-layer-only Sentry policy/runtime lifecycle boundary; reads and live-applies `PrivacySettings.SentryTelemetryEnabled`.  |
| Online status reporting      | `Services/OnlineStatusService.cs`                                       | Host-managed SECTL online status reporter; reads `PrivacySettings.OnlineStatusMode`.                                      |

## CONVENTIONS

- `BuildHost()` registers logging, config, services, windows/views, ViewModels, attached settings controls, and
  navigation pages.
- Telemetry runtime policy belongs in app-layer services and should live-apply `MainConfigHandler.Data.General.PrivacySettings.SentryTelemetryEnabled`; do not move SDK-specific wiring into Core or Shared. The concrete Sentry adapter stays under `SecRandom/Services/Telemetry/SentryTelemetrySdkAdapter.cs`.
- Background app services such as `OnlineStatusService` are registered through Host and must honor `PrivacySettings.OnlineStatusMode` before doing network work.
- ViewModels must be registered in Host and inherit `ViewModelBase`; `ViewModelBase` exposes `Config`.
- Use `IAppHost.GetService<T>()` for existing service resolution patterns in views and services.
- Views usually set `DataContext = this` and expose a `ViewModel` property.
- Main default page: `main.rollCall`; settings default page: `settings.home`.
- Page IDs follow root rules: `main.xxx`, `settings.xxx`, `settings.group.xxx`.
- Toasts are available from views via `this.ShowWarningToast(...)` / `ShowErrorToast(...)`; shell views inject
  `AppToastAdorner` on load.
- `SettingsView.RequestRestartApp()` is the pattern for settings that require app restart.
- `Styles.axaml` should stay a thin include of `avares://SecRandom.Core/StylesBase.axaml` unless app-only styling is
  needed.

## LOCALIZATION

- Each page has its own folder under `Langs/`.
- Privacy settings localization lives under `Langs/SettingsPages/General/Privacy/` and follows the same base `.resx` + designer registration pattern as other settings pages.
- List management pages currently include roll-call lists (`data/list/roll_call_list`) and lottery prize pools (`data/list/lottery_list`).
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
- Do not assume config dictionary/list mutations auto-save; call save at the mutation boundary or unload.
- Do not put reusable controls/styles here when they are intended for Core consumers.
- Debug-only pages (`CameraPreviewTestPage`, `DebugSettingsPage`) intentionally skip localization folders; do not remove
  their `#if DEBUG` guards.
