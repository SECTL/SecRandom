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
├── Mobile/              # Mobile platform-neutral contracts and lifecycle support
├── Views/               # Main shell, settings shell, desktop and mobile pages, windows
├── ViewModels/          # App VM state; root holds shell/profile bases
│   ├── MainPages/       # Page-specific VMs for built-in main pages and floating-window quick draw
│   ├── Mobile/          # Mobile route ViewModels, including mobile draw state and commands
│   └── SettingsPages/
│       └── History/     # History settings VMs (RollCallHistoryViewModel, LotteryHistoryViewModel)
├── Services/            # App-only services
│   ├── Config/          # Device UUID and desktop-specific configuration adapters
│   ├── CrashRecovery/   # Crash detection, recovery prompt, restart guard
│   ├── Desktop/         # TaskBarIconService, GlobalShortcutService
│   ├── Feedback/        # Explicit Sentry user-feedback submission and standard diagnostic ZIP attachment
│   ├── Platform/        # App-side neutral window-handle adapter for platform feature requests
│   ├── Draw/            # DrawAudioService, DrawTemporaryRecordService
│   ├── ImportExport/    # Desktop shell + post-import hooks over Core Archive services; diagnostics export
│   ├── Linkage/         # CSES/ClassIsland sources, course state runtime, draw authorization, pre-class reset
│   ├── Music/            # Managed local draw-music library and stable track selection resolution
│   ├── Notification/     # ClassIsland SecRandom4Ci notification bridge
│   ├── Ipc/             # ProtocolCommandRouter for URL/IPC command routing
│   ├── Profiles/        # Non-mutating ProfileQueryService snapshots
│   ├── Settings/        # SettingsSearchService
│   ├── Mobile/          # Mobile navigation, draw/media, update, profile helpers
│   ├── Security/        # Credential store, verification prompts, factor/operation authorization
│   ├── Telemetry/       # ITelemetryTransaction/ITelemetrySdkAdapter seam; Sentry adapter + desktop-only Sentry shim; Sentry-free TelemetryRuntimeService
│   ├── Updates/         # Signed manifest discovery, complete-artifact deployment, update scheduler
│   ├── ViewEngine/      # Desktop Avalonia view-host provider for Core logical views
│   ├── Voice/           # VoiceAnnouncementService
│   ├── Verification/    # Immediate local proof export and asynchronous fair.sectl.cn replay attestation
│   └── OnlineStatusService.cs  # Root-level status reporter
├── Models/              # App-local view/support models
├── Helpers/             # App-local helpers
├── Converters/          # App-local Avalonia converters
├── Langs/               # Per-page resx plus app-level IPC response localization
├── Assets/              # Avalonia resources, icons, MiSans font, banners
├── Controls/            # App-specific controls, including Mobile/; shared controls belong in Core
└── Styles.axaml         # Includes Core style bundle
```

## WHERE TO LOOK

| Task                         | Location                                                                | Notes                                                                                                                    |
|------------------------------|-------------------------------------------------------------------------|--------------------------------------------------------------------------------------------------------------------------|
| Register VM/service/page     | `App.axaml.cs` `BuildHost()`                                            | Strong convention; Host is source of truth.                                                                              |
| Main app window flow         | `App.ShowMainWindow()`, `App.ShowQuickDrawWindow()`, `Views/MainView.axaml.cs` | `FloatingWindow` is desktop main window; main view opens separately. `QuickDrawPage` is resolved directly from Host for its standalone, draggable, topmost quick-draw window and must stay registered. The quick-draw window sizes to its content, caps itself to the current display work area, and scrolls result content when needed. Roll-call and lottery remaining lists open a shared resizable `RemainingListWindow` at `1000x600`. |
| Settings flow                | `App.ShowSettingsWindow()`, `Views/SettingsView.axaml.cs`               | Desktop uses navigation/search/restart; mobile uses the same independent SettingsView MVE with a hidden catalog, child Back-to-catalog, and Home-to-root behavior. |
| Crash recovery               | `Services/CrashRecovery/`, `Views/CrashRecoveryWindow.axaml(.cs)`, `Langs/CrashRecovery/` | Desktop fatal/dispatcher crash restart, crash report prompt, and shared FeedbackDrawer Bug draft with prefilled crash context. |
| Add main page                | `Views/MainPages/`, `BuildHost()`                                       | `[PageInfo]` + `AddMainPage<T>()`; built-in main navigation includes roll-call, lottery, and history. Quick draw opens from the floating window. |
| Main page ViewModels         | `ViewModels/MainPages/`                                                 | Page-specific VMs for built-in main pages and floating-window quick draw; XAML compiled bindings must use this namespace. |
| Shared draw orchestration    | `Services/Draw/SharedDrawServices.cs`                                   | Desktop and mobile use the same roll-call/lottery verification, candidate, temporary-record, and transactional commit workflows; UI owns authorization, media, animation, and presentation. |
| Mobile draw ViewModel        | `ViewModels/Mobile/MobileDrawPageViewModel.cs`                          | Owns mobile draw state, commands, media orchestration, and dialog/animation requests; the page hosts only Avalonia visuals. |
| Quick draw localization      | `Langs/MainPages/QuickDraw/`                                            | Floating quick-draw status and command text use their own resource set. |
| History settings ViewModels  | `ViewModels/SettingsPages/History/`                                     | VMs used by roll-call/lottery history settings pages embedded in settings and main history views.                          |
| Add settings page            | `Views/SettingsPages/`, `Langs/SettingsPages/`, `App.axaml.cs`          | `[PageInfo]` + localization folder + `AddSettingsPage<T>()`; update title/resource wiring if language refresh is needed. General pages currently include `Basic`, `Privacy`, and `Backup`; v2-parity root entries include floating window, notification, security, linkage, voice, history, update, logs, and more settings. |
| Log viewer                   | `Views/SettingsPages/LogViewer/LogViewerSettingsPage.axaml(.cs)`       | Hidden settings page `settings.logs`; opened from the settings more-options menu. Reads `.log` and `.log.gz` files from `data/logs`. |
| App config JSON              | `../SecRandom.Core/Services/Config/FileConfigService.cs`                 | Desktop registers the host-internal Core JSON storage; its default package-root path remains unchanged.                    |
| Device UUID                  | `Services/Config/DeviceUuidStore.cs`                                    | Persists the pseudo-anonymous online/witness client identity in `data/config/device-uuid.json`.                         |
| Settings/data import-export  | `Services/ImportExport/`, `../SecRandom.Core/Services/Archive/`, `Views/SettingsView.axaml(.cs)` | Core `DataArchiveService` owns strict v3 ZIP/JSON transfer, manifest/SHA-256 validation, manual/automatic backups, staging commit/rollback, and mandatory pre-import snapshots; platform follow-up work goes through `IArchivePostImportHooks` (desktop implementation: runtime refresh + desktop-integration sync). Desktop `IImportExportService` is a thin shell that adds diagnostics export and delegates everything else; `AutomaticBackupService` is Host-managed and checks configured backup cadence. Draw-verification proof files live under `data/proofs` and are included by default in both manual and automatic backups. The settings shell owns pickers, preview/confirmation, feedback, and restart requests. |
| Searchable settings metadata | `Services/Settings/SettingsSearchService.cs`                            | Reflects `Langs.SettingsPages.*` resources and registered settings pages. Matches by `Type.Name` so pages in subdirectories are found correctly. |
| ClassIsland notifications    | `Services/Notification/`, `../SecRandom4Ci.Interface/`                 | Sends draw-result DTOs to the optional ClassIsland `SecRandom4Ci` plugin through v2 IPC. |
| Profile persistence          | `../SecRandom.Core/Services/Profiles/ProfileService.cs`, `../SecRandom.Core/Services/Profiles/ProfileCatalogManager.cs` | Current lists/history, active point-call list/history switching, catalog rename/delete/create, and `SaveProfile()`; list items carry hidden `RecordId` identity. |
| Voice announcements          | `Services/Voice/`, `Controls/AttachedSettings/SpecificAnnouncementAttachedSettingsControl.axaml(.cs)` | App-layer TTS runtime: `ISpeechProvider` implementations enumerate/synthesize system or Edge audio, `SpeechAudioPlayer` owns SoundFlow/MiniAudio playback, and `VoiceAnnouncementService` builds one batched result announcement. The registered `专属语音` attached setting supplies per-student/per-prize alias, prefix, and suffix. |
| Draw-proof attestation       | `Services/Verification/` | Immediately saves a local reproducible proof, then asynchronously asks `fair.sectl.cn` to replay and sign its exact committed content. |
| Draw audio / temp records    | `Services/Draw/DrawAudioService.cs`, `../SecRandom.Core/Services/Draw/DrawTemporaryRecordService.cs` | Audio stays app-layer; session-scoped temporary records are shared Core runtime behavior. |
| Managed music library        | `Services/Music/MusicLibraryService.cs`, `Views/SettingsPages/Personalized/MusicSettingsPage.axaml(.cs)` | Imports, removes, and previews managed MP3/WAV/FLAC files under `data/audio/music`. |
| Desktop integration          | `Services/Desktop/`                                                     | Taskbar lifecycle, Windows native global shortcuts, cross-platform autostart, and `secrandom://` registration.          |
| Platform feature calls       | `Services/Platform/`, `../SecRandom.Platforms.Abstractions/`            | Views obtain `IWindowFeatureService` through Host; native implementations stay outside the app project. Removable-storage enumeration uses the platform-owned `IRemovableStorageCatalog`. |
| Cross-platform view host     | `Services/ViewEngine/`                                                    | Desktop physical host provider for `SecRandom.Core.Views`; it owns ordinary Avalonia windows only.                       |
| Mobile root                  | `App.axaml.cs`, `Mobile/`, `Views/Mobile/MobileViewHost.axaml(.cs)`, `MobileRootView.axaml(.cs)`; entry points `../SecRandom.Android/MobileEntryPoint.cs`, `../SecRandom.iOS/MobileEntryPoint.cs` | Shared App mounts `MobileViewHost`'s one `NavigationPage`; it presents `MobileRootView` and independent MVE pages, while `MobileRootView` owns ordinary bottom-bar `UserControl` routes. |
| Telemetry runtime seam       | `Services/Telemetry/`                                                   | App-layer-only telemetry policy/runtime boundary behind the `ITelemetryTransaction`/`ITelemetrySdkAdapter` seam; Sentry types stay inside the adapter and the desktop-only shim. Reads and live-applies `PrivacySettings.SentryTelemetryEnabled`. |
| User feedback                | `Services/Feedback/`, `Views/FeedbackDrawer.axaml(.cs)`                | Explicit Sentry User Feedback service; each host uses a transient FeedbackDrawer, and Bug reports attach the existing standard diagnostic ZIP without enabling background telemetry. |
| Online status reporting      | `Services/OnlineStatusService.cs`                                       | Host-managed SECTL online status reporter; reads `PrivacySettings.OnlineStatusMode`.                                      |
| Update center                | `Services/Updates/`, `Views/SettingsPages/Update/`                       | Signed full-artifact checks, portable staging/Launcher restart, or native installer handoff.                              |
| Security authorization       | `Services/Security/`                                                     | Separate credential storage, password/TOTP/USB factors, removable-device catalog, lockout state, and operation authorization gateway. |
| URL/IPC automation           | `Services/Ipc/ProtocolCommandRouter.cs`, `Core/Services/SingleInstance/` | URL activation and duplex structured IPC share one protected command router; legacy plaintext activation remains supported. |

## CONVENTIONS

- iPadOS is detected by the iOS entry head through `UIDevice.CurrentDevice.UserInterfaceIdiom`, and Android tablets are detected by the Android entry head through `Resources.Configuration.ScreenLayout` and `Configuration.ScreenLayoutSizeMask`. Both use the original `MainView` and desktop main-page registrations inside the mobile single-view host, while their settings command opens the existing mobile `SettingsView` MVE rather than creating a desktop window. iPhone and Android phones retain `MobileRootView`.

- `BuildHost()` registers logging, config, services, windows/views, ViewModels, attached settings controls,
  navigation pages, and real view-engine registrations. Built-in main/settings child pages use only
  `AddMainPage<T>()` / `AddSettingsPage<T>()` keyed `UserControl` registration and `FAFrame` navigation. Only
  independent lifecycle views such as `MainView`, `SettingsView`, and crash recovery use MVE.
  Its mobile branch directly registers mobile services, navigation, routes, settings pages, and the single-view host;
  do not move those registrations to a separate application-service extension.
- `BuildHost()` receives the root selected through `PlatformStartupContext` and registers it with `AddPlatformServices`. The shared App selects mutually exclusive desktop or mobile registrations; app code may adapt an Avalonia `TopLevel` into a neutral handle, but it must not contain Win32/X11/AppKit operations or platform selection logic.
- `BuildHost()` registers the desktop `IViewHostProvider` after `AddViewEngine()`. Named physical hosts are registered by `App` when a main/settings window is created. It does not manage embedded shell regions.
- `MainWindow` and `FloatingWindow` retain their presentation and configuration behavior but request topmost through `IWindowFeatureService`; `FloatingWindow` requests `ToolWindow` during construction while its native handle is available but before its first show, then requests task-switcher exclusion after opening where supported. Its transparent composition hint must be set during construction. Floating-window restoration must preserve `ShowActivated=False` and not steal focus. Linux implements tool/task-switcher semantics through X11 EWMH when `DISPLAY` is available, including XWayland sessions; native Wayland remains out of scope. macOS maps `ToolWindow` to an `NSWindow` utility window that joins all Spaces; Command-Tab remains application-level and is not modeled as a per-window task-switcher exclusion.
- `App` is shared by desktop and mobile. Its SingleView branch initializes only `MobileViewHost`, `MobileRootView`, mobile routes/media/update/status services, and the `SingleViewHostProvider`; it must not route through desktop single-instance, OOBE, floating-window, tray, shortcut, update scheduler, or protocol startup paths.
- Mobile code belongs to this project: `Mobile/` owns neutral platform contracts and lifecycle support, while `BuildHost()`
  owns mobile composition. `Views/Mobile/`, `Controls/Mobile/`, `Services/Mobile/`, and `Langs/Mobile/` own their corresponding
  app-layer responsibilities. Platform heads inject native media, update, and keyboard-occlusion implementations through
  `MobilePlatformServiceRoot`; shared mobile views must not reference Android or UIKit APIs. Do not recreate a separate mobile UI assembly.
- Mobile uses the same configurable application font path as desktop: the default is embedded MiSans, and
  `AppearanceSettingsConfig.Font` / `FontWeight` apply through `RefreshPersonalizedSettings()` on every platform.
- Mobile fixed layout is AXAML-first. Keep code-behind for runtime data, StorageProvider, dialogs, DataGrid setup, media, and service orchestration; do not add a second mobile style/token layer, `NavigationView`, or generic C# setting-row factory as the primary page layout.
- Mobile SettingsView is its own MVE page in `MobileViewHost`'s `NavigationPage`, not bottom-bar root-route state. It currently reuses the desktop settings layout and starts at hidden `settings.mobile`, whose catalog follows the registered `SettingsItems` order, inserting each registered group at its first child and keeping ungrouped or unknown-group pages as directly clickable expanders. The catalog uses the same hidden/separator filtering and registered-group fallback as desktop settings navigation; grouped expanders remain collapsed by default on mobile. `MobileRootView` directly owns draw/history/overview selection and navigates its `FAFrame` through `IFANavigationPageFactory`, with history and cache disabled. Catalog navigation must not programmatically set the desktop `FANavigationView` selection because the hidden catalog has no corresponding sidebar item; navigate only its `FAFrame` content.
- Crash recovery startup prompt handling runs before single-instance acquisition. Build the DI Host, but do not start normal hosted/runtime services, before showing the startup-only recovery prompt so its application-feedback action can open the same `FeedbackDrawer` Bug form with a prefilled crash report and standard diagnostics; normal app restart must release `SingleInstanceService` before launching the replacement process.
- Telemetry runtime policy belongs in app-layer services and should live-apply `MainConfigHandler.Data.General.PrivacySettings.SentryTelemetryEnabled`; do not move SDK-specific wiring into Core or Shared. Runtime code talks to the Sentry-free `ITelemetryTransaction` / `ITelemetrySdkAdapter` seam (`TelemetryRuntimeService` has no Sentry reference; the DSN comes from `GlobalConstants.SentryDsn`). The concrete Sentry adapter stays under `SecRandom/Services/Telemetry/SentryTelemetrySdkAdapter.cs`, with Sentry transaction extensions isolated in the desktop-only `TelemetryTransactionSentryExtensions` shim that mobile-linked builds exclude. `Sentry.AspNetCore` is desktop-only because it brings `Microsoft.AspNetCore.App`; mobile builds use `Sentry` and `Sentry.Extensions.Logging` without that framework reference. Explicit user feedback is separate: `Services/Feedback/` creates a short-lived feedback-only Sentry client on a direct user action, so it remains available while background telemetry is disabled and never changes that preference.
- Background app services such as `OnlineStatusService` are registered through Host and must honor `PrivacySettings.OnlineStatusMode` before doing network work.
- `UpdateCenterService` is the only update transaction entry point. It must verify raw manifest bytes with the embedded Ed25519 key and verify artifact length/SHA-512 before a complete ZIP deployment or native installer handoff; `UpdateScheduler` only checks and never downloads in the background.
- Security services are Host singletons. Keep secrets out of normal config and route protected window, tray, draw, and linkage operations through `ISecurityService` instead of duplicating checks in UI event handlers.
- Security credentials live only in `data/config/security/credentials.json`. Its internal `FormatVersion` governs the format; the user's password uses Argon2id to derive the AES-256-GCM key, so the file is portable between supported hosts. Do not add DPAPI, Keychain, `secret-tool`, platform key files, filename version suffixes, or legacy credential-file migration.
- Security settings mutations use `ISecurityService`'s password-gated update boundary once protection is active; pages project their state one-way and must not directly persist factor or protected-operation changes. `IRemovableStorageCatalog` is implemented by the Windows, Linux, and macOS platform projects (mobile uses an empty catalog); `IUsbDeviceCatalog` exposes only ready program-enumerated volumes, and the USB binding dialog selects its `UsbDeviceInfo` rows (drive letter, disk name, device ID) rather than arbitrary folders or paths. Current mount roots remain inside the catalog/security-service implementation and must not appear in `UsbDeviceInfo`, dialog results, public security APIs, or IPC. `ISecurityService.BindUsbAsync` accepts only the selected device ID and resolves the current mount internally.
- `ProtocolCommandRouter` is a Host singleton and is the only app-layer URL/IPC command dispatcher. Keep IPC request handling on the UI dispatcher for UI mutations; `data/*` must use `IProfileQueryService` snapshots and never switch an active profile.
- Roll-call, lottery, and quick-draw ViewModels are shared draw sessions for UI and IPC. Their protocol methods reuse nonvisual core paths after router authorization; do not resolve a detached transient ViewModel for IPC. List renames from settings and OOBE use the Core catalog manager so list, history, default-selection, and temporary-record file names stay aligned.
- `RollCallDrawService` and `LotteryDrawService` own the common desktop/mobile draw workflow: candidate snapshots, verification draws, and coordinator commits. Do not reintroduce platform-specific direct `DrawEngine` or `IDrawCommitService` paths in mobile or desktop page ViewModels; authorization, animation, media, notifications, and result projection remain presentation concerns.
- `SettingsView` read-only preview is entered only by the security verification prompt when `AllowSettingsPreview` is enabled. Freeze the page content host, not settings navigation, and exit preview on normal authorized navigation.
- `IVoiceAnnouncementService` is app-layer because Edge TTS and Windows SAPI are platform/runtime concerns. `ISpeechProvider` separates voice enumeration/synthesis from `ISpeechAudioPlayer`, which owns the ClassIsland SoundFlow/MiniAudio playback package. Edge synthesis comes from the `vendors/EdgeTtsSharp` submodule, while generated Edge MP3 and system WAV cache files live under `data/audio/voice` using readable `voice-text` keys; `SpeechRate` is applied by a single SoundFlow player during playback, not synthesis, so pitch remains unchanged. `VoiceSettings.VoiceEnable` is the total announcement switch, while default and per-draw settings determine whether a specific draw type requests an announcement. Per-record TTS alias/prefix/suffix belongs in attached settings on `Student`/`Prize`, not in a standalone settings page.
- Verification draws cannot wait for the network. `VerificationDrawCoordinator` must immediately generate and export an `OfflineReproducible` proof, then enqueue it for `DrawProofAttestationService` background retries. The service receipt binds a canonical hash of every proof field except the receipt itself, plus the proof ID, input hash, draw payload, audit payload, and mode; only an attested proof is eligible for the public proof URL. This catches later proof-file edits, but it does not establish that the local binary or real-world candidate pool was unmodified before drawing. Do not describe the receipt as pre-draw server witnessing.
- `NotificationService` is app-layer because it bridges completed draws to the optional ClassIsland `SecRandom4Ci` plugin through v2 IPC. Use the source-compatible `SecRandom4Ci.Interface` DTOs; each draw channel only controls whether it is enabled, while `NotificationSettings.Default` owns one global set of service, duration, threshold, and built-in auto-close values. The built-in notification channel is the existing QuickDraw window, not a separate notification window: automatic QuickDraw delivery opens it during the rolling preview only for built-in or dual service types, then reveals the result; external-only delivery does not open it unless `UseBuiltInOnServiceFailure` (default enabled) invokes the backend-neutral fallback. This does not restrict a user-initiated QuickDraw window.
- The behind-scene attached setting is intentionally excluded from startup registration. Ten clicks on the About-page banner temporarily register it for the current process, where it appears in the student and prize attached-settings menus; restart returns it to the hidden state. Runtime-only registration uses the attached-settings registry fallback rather than persisting a feature flag.
- Picking `ClearRecord` is about app-layer temporary draw records, not persistent profile histories. RollCall and QuickDraw share student temporary records; Lottery uses prize temporary records.
- `MusicLibraryService` owns `data/audio/music` and new selection IDs. Student/prize attached music settings can override only animation/result selections; a multi-record draw uses the first drawn record as the music source. Deleting a track clears global and per-record references. Keep SoundFlow types private to `DrawAudioService`; each resolved draw music setting owns `AnimationMusicLoop`, process playback must stop immediately on cancellation/disposal, and transitions to result music without delaying draw results. Mobile builds retain SoundFlow's managed assembly for the shared app but exclude its unused native assets; `IMobileMediaPlayer` owns Android/iOS playback. Legacy absolute paths are read-only compatibility entries; do not create new external-path selections.
- ViewModels must be registered in Host and inherit `ViewModelBase`; `ViewModelBase` exposes `Config`.
- Keep shell/profile/base ViewModels in `ViewModels/`; page-specific main ViewModels belong in `ViewModels/MainPages/`, and history settings page ViewModels belong in `ViewModels/SettingsPages/History/`.
- Mobile route ViewModels belong in `ViewModels/Mobile/`; register them in the mobile branch of `BuildHost()` and keep page code-behind limited to visual-host responsibilities such as dialogs and animations.
- Use `IAppHost.GetService<T>()` for existing service resolution patterns in views and services.
- Views usually set `DataContext = this` and expose a `ViewModel` property.
- Main default page: `main.rollCall`; settings default page: `settings.overview`, which separates aggregate roll-call list statistics from aggregate lottery-pool statistics.
- The roll-call main page is bottom-pinned in the main window sidebar (`PageLocation.Bottom`), full-width, and title-hidden. Keep its page chrome controlled by `MoreSettings`.
- Lottery main page ID is `main.lottery`; quick draw is not registered as a main navigation page, but its settings page remains `settings.picking.quickDraw`.
- More settings includes roll-call and lottery page management options for the control panel side and per-control visibility; wire built-in draw pages through `MainConfigModel.MoreSettings` instead of duplicating local UI flags.
- Global shortcuts are an app-layer hosted service. `GlobalShortcutService` registers the nine V2-parity actions through Windows `RegisterHotKey`, dispatches them to Avalonia's UI thread, and degrades without registrations on other platforms. Changes to `MoreSettings.EnableShortcut` or a binding apply without restarting.
- Floating window settings are live-applied to an open `FloatingWindow`: button selection, layout, display style, size, opacity, topmost mode, and dragging must not require reopening the window.
- Course linkage is app-layer only. `CourseLinkageHostedService` is the sole course-boundary scheduler; CSES parsing/file access stays behind `ICsesScheduleStore`, and ClassIsland types stay inside `ClassIslandScheduleSource`. Use `LinkageDrawCoordinator` for every local, protocol, or shortcut draw/reset path so confirmed non-class restrictions cannot be bypassed. Unknown/unavailable sources are permissive.
- Floating-window course hiding records linkage-owned prior visibility and current user intent. Do not restore a window that the user hid manually while a course break was active.
- Floating window edge docking uses `StickToEdge`: on drag release it snaps only when near the current display's horizontal work-area edge. Dragging stays in valid display work areas and can cross only to physically adjacent displays. At startup, a saved position that does not fully fit a current display work area is restored directly as a docked handle at an available display edge. `StickToEdgeRecoverSeconds` controls auto-collapse (`0` disables collapse); the collapsed handle and restoration transition remain anchored at the docked left/right side center.
- Basic settings are active behavior: the primary `MainWindow` alone consumes startup visibility, topmost mode, and background-resident close behavior; `AutoSaveWindowSize` preserves separate geometry/maximized state for the primary and settings windows. Selecting UIAccess topmost for either primary or floating windows persists the choice and requests a restart; `SecRandom.Desktop/UiAccessStartup.cs` follows `killtimer0/uiaccess` by setting `TokenUIAccess` on a duplicate of the elevated bootstrap token before Avalonia starts, while UAC cancellation or preparation failure uses ordinary topmost only for that run. `DesktopIntegrationService` owns user-level autostart and `secrandom://` registration for Windows, Linux, and macOS; a failed registration must leave its setting disabled rather than persisting a false success.
- `TopmostMode.UiAccess` is Windows-only process capability, not a separate window API. Release builds opt into it through the desktop UIAccess manifest, Authenticode-sign `SecRandom.Desktop.exe`, and install under Program Files; ordinary/debug builds retain the normal manifest and `DesktopIntegrationService` falls back to ordinary topmost when the token is absent.
- Page IDs follow root rules: `main.xxx`, `settings.xxx`, `settings.group.xxx`.
- V2-parity settings pages currently use root IDs such as `settings.personalized.floatingWindow`, `settings.notification`, `settings.general.security`, `settings.linkage`, `settings.notification.voiceMusic`, `settings.history`, `settings.update`, and `settings.more`; register them in `BuildHost()` instead of manually editing navigation UI. Hidden pages like `settings.logs` still need Host registration even though they are not shown in the sidebar.
- The log viewer page ID is `settings.logs`; keep it hidden from the settings sidebar and route the settings more-options “查看日志” command through navigation instead of opening a separate window. The same menu's final, separated `反馈` action opens a host-scoped transient `FeedbackDrawer` through the existing right-side `DrawerHost`; temporary close preserves its draft, while cancel clears it.
- Toasts are available from views via `this.ShowWarningToast(...)` / `ShowErrorToast(...)`; shell views inject
  `AppToastAdorner` on load.
- `SettingsView.RequestRestartApp()` is the pattern for settings that require app restart.
- Ordinary verification draws are immediately exported as `OfflineReproducible` proofs and replay-attested once in the background. Only a newly completed ordinary draw may enqueue that request: startup, mode changes, and failed requests must not scan or retry historical proofs. Formal notarization waits for `fair.sectl.cn` to durably lock the anonymous input, persist server random material, calculate the result, and return an `OnlineWitnessed` official proof; network interruption, service unavailability, device failure, or other force majeure must never substitute a local result or silently switch modes. Once the server lock exists, its candidate pool, rules, random material, and result cannot be replaced, supplemented, or recalculated within that draw. The formal server ledger is separate from retention-limited public proof storage. Neither mode proves the real-world candidate pool was complete or authentic before submission.
- Settings and all-data imports must create their corresponding recovery ZIP under `data/backup` after user confirmation and before changing live files. A backup failure aborts the import; `data/config/security` credentials, keys, and lockout state are excluded from every export/import. Only v3 producer-version envelopes and manifest archives are importable, and unsupported inputs must be rejected before creating a recovery snapshot.
- `DeviceUuidStore` owns the pseudo-anonymous UUID in `data/config/device-uuid.json`. It migrates the legacy value from settings on first access; full-data and configuration backups preserve it, while settings-only imports leave the local device UUID untouched.
- Current settings and archives carry `producer_version`; all import and restore paths accept only SecRandom v3 exports after inspection, then preserve the mandatory snapshot/staging workflow. Unsupported files must be rejected before any live-data mutation.
- First-run OOBE is a top-level window shown after `BuildHost()` and before `FloatingWindow` construction. Its fixed header/footer surround an animated carousel that starts with a non-blocking centered welcome screen and application icon; this welcome screen is outside the seven configuration pages, whose first item places the collapsed-by-default verifiable-draw notice before the privacy-policy/GPLv3 acknowledgement controls and requires its explicit read checkbox when its version has not been accepted. The full setup later has a separate privacy page after desktop integration and before completion, binding the distinct Sentry telemetry and online-status settings; a policy-version update remains limited to the acknowledgement page plus those privacy choices. The welcome screen's lower-left language selector saves `Basic.Language`, applies the selected culture, refreshes localized ViewModel-derived values, and replaces only the OOBE window so static localized XAML resources reload without restarting the application or leaving first-run setup. An active OOBE import drawer defers visual replacement until it closes, preserving its preview and mapping work. OOBE class/prize spreadsheet imports open the shared list-management import controls in the window's right-side `DrawerHost`, preserving the settings-page mapping, preview, duplicate, and cancel behavior. Its ViewModel and setup services are Host-registered. `GuideCompleted`, `AcceptedVerificationNoticeVersion`, `AcceptedPrivacyPolicyVersion`, and `AcceptedGplVersion` control whether acknowledgement is required. Completion must hand off to normal startup and explicitly show the primary main window before closing OOBE, so the application remains open.
- Core's `IFeatureAvailabilityService` is the sole runtime lottery-capability decision. `MoreSettings.LotteryEnabled` must gate sidebar navigation, floating-window controls, shortcuts, `App` page selection, and `ProtocolCommandRouter` lottery commands.
- `Styles.axaml` should stay a thin include of `avares://SecRandom.Core/StylesBase.axaml` unless app-only styling is
  needed.
- Application-owned UI icons use Fluent `Filled` variants, including settings entries, command buttons, menus, navigation items, and floating-window actions. Prefer `sr:Fi NameFilled` or `FluentIcons.NameFilled`; do not introduce new raw Unicode glyphs.
- If a required Filled icon has no same-name variant, choose the closest semantic Filled icon and keep the mapping traceable to the source icon or code point. Logo, author/organization, window, and taskbar image assets remain unchanged.

## LOCALIZATION

- On Android, `MobileViewHost` handles `TopLevel.BackRequested` only when an independent MVE is above `MobileRootView`, closing that MVE back to the root. Back requests at the root remain unhandled so Android performs its normal exit behavior; the event does not alter the settings shell's internal page history or bottom-bar routes.

- Each page has its own folder under `Langs/`.
- Crash recovery has an app-level localization folder at `Langs/CrashRecovery/` because the prompt is a top-level window, not a settings page.
- Privacy settings localization lives under `Langs/SettingsPages/General/Privacy/` and follows the same base `.resx` + designer registration pattern as other settings pages.
- IPC response localization lives under `Langs/Ipc/`; it supplies human-readable response messages while protocol `code` values remain invariant for automation clients.
- List management pages currently include roll-call lists (`data/list/roll_call_list`) and lottery prize pools (`data/list/lottery_list`). A roll-call import may map student number or name; it requires at least one and excludes rows where both are blank.
- Roll-call and lottery list/history files are stored as plain JSON on disk; keep their `.json` paths stable and use the Host-registered Core file config service instead of direct serialization.
- Required files for a localized resource set: `Resources.resx` and `Resources.Designer.cs`; culture files such as
  `Resources.en-US.resx` / `Resources.ja-JP.resx` are optional and must keep exact on-disk casing.
- Register only base `.resx` and designer in `SecRandom.csproj` using existing `EmbeddedResource` / `Compile` pattern.
- Use `PublicResXFileCodeGenerator` for resource designers.
- Settings keys: `S_`, `S_xxx_D`, `S_xxx_R`; options `O_`; messages `M_`; controls/content `C_`.
- Chinese i18n values must not use the Chinese full stop (`。`).
- Settings-page explanation values (`*_D`, including `S_*_D` and `C_*_D`) must not use sentence-ending or sentence-separating full stops (`。` or `.`); preserve technical dots in file names, domains, process names, and version identifiers.

## COMMENTS

- Keep comments that describe lifecycle, DI, localization, or platform constraints.
- Avoid comments that simply repeat obvious UI event names or assignments.

## ANTI-PATTERNS

- Do not add a page class without `[PageInfo]` and Host registration.
- Do not add a settings page without its localization folder.
- Do not instantiate reusable services directly in pages.
- Do not assume config dictionary/list mutations auto-save; call save at the mutation boundary or unload.
- Do not require either student number or name in roll-call column mapping; a candidate needs at least one nonblank value, while `RecordId` remains the internal identity.
- Do not put reusable controls/styles here when they are intended for Core consumers.
- `DebugSettingsPage` is registered in every build so the About-page activation path can expose it in release builds; it is hidden by default outside `#if DEBUG`, and its user-facing text lives under `Langs/SettingsPages/Debug/`.
