# PROJECT KNOWLEDGE BASE

<!--
Maintenance contract:
- Keep this file durable: prefer stable conventions over environment-specific observations.
- AI agents must maintain this file automatically: after changing project structure, target
  frameworks, localization layout, workflows, navigation/DI rules, or public module boundaries,
  update the relevant AGENTS.md in the same change before finishing.
- Root instructions apply repository-wide. Nested AGENTS.md files add subtree-specific rules.
- `docs/project_rules.md` is the source of truth when a convention conflicts with this summary.
-->

**Last Update:** 2026-07-20
**Last Submit:** 8547cddf
**Last modified model:** Kimi

## OVERVIEW
SecRandom is a GPLv3 C#/.NET desktop app for fair random drawing in education scenarios. Stack: .NET solution, Avalonia + FluentAvalonia UI, Microsoft.Extensions.Hosting DI, xUnit v3 tests.

## STRUCTURE
```
SecRandom-C/
├── SecRandom/             # Avalonia app layer: App host, views, viewmodels, app services, localization, assets
├── SecRandom.Core/        # Core/domain + reusable UI controls/styles + config/logging/draw services
├── SecRandom.Shared/      # Cross-project contracts, base config/model types, IPC/profile models
├── SecRandom4Ci.Interface/ # Shared ClassIsland v2 IPC contract for the SecRandom4Ci plugin
├── SecRandom.Desktop/     # Tiny executable launcher; Program.cs bootstraps Avalonia and UiAccessStartup.cs prepares Windows UIAccess
├── SecRandom.Launcher/    # Minimal portable-package version selector; starts an activated app-* payload only
├── SecRandom.Platforms.Abstractions/ # Platform-neutral window capability contracts and result DTOs
├── SecRandom.Platforms/   # Startup context, DI registration, and unsupported-platform stub
├── SecRandom.Platforms.Windows/ # Windows-native window feature implementation
├── SecRandom.Platforms.Linux/ # Linux-native window feature implementation boundary
├── SecRandom.Platforms.MacOs/ # macOS-native window feature implementation boundary
├── SecRandom.Mobile.Shared/ # Neutral net10.0 mobile shared library (assembly/namespace stay SecRandom.Mobile): SingleView shell, views, mobile-only UI
├── SecRandom.Mobile.Tests/  # Avalonia Headless tests for mobile styles, native controls, and phone-size layout
├── SecRandom.Android/       # Android entry head: net10.0-android Exe with BuildMobile=true, otherwise empty neutral library
├── SecRandom.iOS/           # iOS entry head: net10.0-ios Exe with BuildMobile=true, otherwise empty neutral library
├── SecRandom.Core.Tests/  # xUnit v3 test project; currently covers legacy privacy/telemetry migration
├── scripts/               # Standalone tooling and verification scripts, including fairness audits
├── docs/                  # Project rules, localization, namespace boundaries
├── CHANGELOG/             # Versioned release notes, mostly v3 tree
├── resources/             # README mirrors, screenshots, banners, root static assets
├── vendors/EdgeTtsSharp/  # Edge TTS synthesis submodule; app supplies a cross-platform transport seam
├── Global.props           # Main shared MSBuild policy; imported by projects
├── Directory.Build.props  # Avalonia version pin only
└── SecRandom.sln          # Build/test solution entrypoint
```

Nested instruction files:
- `SecRandom/AGENTS.md`: app layer, DI composition, views/viewmodels, app services, localization.
- `SecRandom/Views/SettingsPages/AGENTS.md`: settings page subtree, page IDs, restart semantics, grouped localization expectations.
- `SecRandom.Core/AGENTS.md`: plugin-facing core, draw/config/logging services, shared controls/styles.
- `SecRandom.Shared/AGENTS.md`: UI-free shared contracts and persistence model boundaries.

## WHERE TO LOOK
| Task | Location | Notes |
|------|----------|-------|
| Run/build/test | `SecRandom.sln`, `.github/workflows/Build.yml` | Use solution commands; no Makefile/CMake. |
| Desktop startup | `SecRandom.Desktop/Program.cs` | Process entry → Avalonia lifetime. |
| Platform capability contracts | `SecRandom.Platforms.Abstractions/`, `SecRandom.Platforms/` | App-internal platform root, window feature requests/results, startup context, and DI bridge. |
| Native window features | `SecRandom.Platforms.Windows/`, `SecRandom.Platforms.Linux/`, `SecRandom.Platforms.MacOs/` | Each platform owns native feature handling; views must not add platform API calls. |
| Mobile startup | `SecRandom.Mobile.Shared/`, `SecRandom.Android/`, `SecRandom.iOS/` | Shared library holds the independent SingleView shell (`MobileApp` owns its minimal Host and root view; it does not reference desktop `SecRandom`); the Android/iOS heads own platform entry points and platform seams. |
| Mobile UI tests | `SecRandom.Mobile.Tests/` | Avalonia Headless smoke tests load mobile styles and lay out native shell controls at phone dimensions. |
| Mobile point-call orchestration | `SecRandom.Mobile.Shared/Services/MobileRollCallService.cs` | Mobile-only list/scope/count orchestration over existing Core filtering, sampling, and transactional commit services. |
| App composition / DI | `SecRandom/App.axaml.cs` | `BuildHost()` is the registration source of truth. |
| Main navigation | `SecRandom/Views/MainView.axaml.cs` | Default page `main.rollCall`; keyed DI page factory. Built-in draw pages are `main.rollCall` and `main.lottery`; quick draw opens from the floating window instead of the main sidebar. |
| Settings navigation | `SecRandom/Views/SettingsView.axaml.cs` | Default page `settings.overview`; has back stack + restart dialog. General group now includes `settings.general.basic`, `settings.general.privacy`, and `settings.general.backup`. |
| Page registration helpers | `SecRandom.Core/Extensions/Registry/` | `AddMainPage`, `AddSettingsPage`, plugin page registration, groups, separators. |
| Plugin contracts | `SecRandom.Core/Plugins/` | Public plugin API surface: manifest, runtime context, page registration, plugin catalog DTOs, and draw invocation DTOs. |
| Plugin runtime | `SecRandom/Services/Plugins/` | App-layer plugin discovery, enable state, runtime startup, original-log integration, and restricted draw invoker. |
| ClassIsland notifications | `SecRandom/Services/Notification/`, `SecRandom4Ci.Interface/` | Typed v2 IPC client for the installed SecRandom4Ci ClassIsland plugin. |
| Crash recovery | `SecRandom/Services/CrashRecovery/`, `SecRandom/Views/CrashRecoveryWindow.axaml.cs` | Fatal/dispatcher crash report prompt, guarded auto-restart, and shared desktop relaunch logic. |
| Page registry state | `SecRandom.Core/Services/PagesRegistryService.cs` | Main/settings/group collections. |
| Cross-platform view engine | `SecRandom.Core/Views/` | Plugin-facing logical view/session contracts; desktop and mobile shells provide DI-registered physical hosts. |
| Fair draw logic | `SecRandom.Core/Services/Draw/` | Partial `DrawEngine`, weighted draw, filters, crypto RNG, plus `DrawCommitCoordinator` (`IDrawCommitService`) transactional commits and shared `DrawRepeatPolicy`/`DrawCandidateFilter`. |
| Config persistence | `SecRandom.Core/Services/Config/` | `FileConfigService` and handlers are host-internal Core runtime services; desktop keeps its existing package-root data path. v3 backup/archive transfer lives in `SecRandom.Core/Services/Archive/` (`DataArchiveService`). |
| Audit tooling | `scripts/FairnessAudit/` | Standalone fairness/performance validation script and HTML report generator. |
| Release update signing | `scripts/ReleaseManifest/`, `.github/workflows/build_publish.yml` | Ed25519 key-generation helper and CI manifest signer; private key is Actions-secret-only. Release intermediates and final artifacts are grouped under `artifacts/release/`. |
| Reusable controls/styles | `SecRandom.Core/Controls/`, `SecRandom.Core/Styles/`, `SecRandom.Core/StylesBase.axaml` | App style entrypoint includes Core bundle. |
| Localization rules | `SecRandom/Langs/`, `SecRandom.Core/Langs/`, `docs/localization.md` | Per-page resource folders; `.csproj` registers base resx/designer only. Privacy page resources live under `SecRandom/Langs/SettingsPages/General/Privacy/`. |
| Shared contracts | `SecRandom.Shared/` | Keep UI/runtime dependencies out. Profile list items use hidden stable `RecordId` keys; visible `Id`/student number/prize number is optional metadata. |
| Project rules | `docs/project_rules.md` | Strongest local convention source. |

## CODE MAP
Keep this map short and stable. When code moves, AI agents should re-read the moved files and update this map in the same task.

| Symbol | Type | Location | Role |
|--------|------|----------|------|
| `Program.Main` | entry | `SecRandom.Desktop/Program.cs` | Starts Avalonia desktop lifetime. |
| `UiAccessStartup` | startup helper | `SecRandom.Desktop/UiAccessStartup.cs` | When UIAccess topmost is configured on Windows, elevates a bootstrap process and starts a replacement process with a UIAccess token before Avalonia initializes. |
| `Program.BuildAvaloniaApp` | entry helper | `SecRandom.Desktop/Program.cs` | Platform detect, MiSans default font, trace logging. |
| `App` | Avalonia app | `SecRandom/App.axaml.cs`, `App.Consts.cs` | Culture, XAML load, Host/DI, windows, restart/stop, theme/font refresh. |
| `IAppHost` | static service access | `SecRandom.Core/Abstraction/IAppHost.cs` | Holds Host and exposes `GetService<T>()` / `TryGetService<T>()`. |
| `MainView` | shell view | `SecRandom/Views/MainView.axaml.cs` | Main NavigationView, drawer, default page, settings window bridge. |
| `SettingsView` | shell view | `SecRandom/Views/SettingsView.axaml.cs` | Settings NavigationView, history/back, restart prompt. |
| `PagesRegistryService` | registry | `SecRandom.Core/Services/PagesRegistryService.cs` | Static collections backing generated navigation menus. |
| `DrawEngine` | domain service | `SecRandom.Core/Services/Draw/DrawEngine*.cs` | Student/prize drawing, fairness weights, repeat/avg-gap filtering. |
| `WeightedDrawEngine<T>` | algorithm | `SecRandom.Core/Services/Draw/WeightedDrawEngine.cs` | Validates weights and samples without replacement. |
| `MainConfigHandler` | config handler | `SecRandom.Core/Services/Config/MainConfigHandler.cs` | Main config wrapper over `ConfigHandlerBase<MainConfigModel>`; persists the canonical `General` subtree and still loads legacy root `basic`/`backup` JSON. |
| `ProfileService` | runtime service | `SecRandom.Core/Services/Profiles/ProfileService.cs` | Current profile runtime state, active student-list/history switching, and persistence for desktop and mobile hosts. |
| `IProfileService` | service contract | `SecRandom.Core/Abstraction/Services/IProfileService.cs` | Current lists/history + student profile switch + profile save boundary. |
| `SettingsSearchService` | app service | `SecRandom/Services/Settings/SettingsSearchService.cs` | Indexes settings pages via reflected localization resources. |
| `CrashRecoveryRuntime` | app service helper | `SecRandom/Services/CrashRecovery/CrashRecoveryRuntime.cs` | Reads crash recovery mode, writes bounded crash reports, and builds restart process plans. |
| `ISecurityService` | app service contract | `SecRandom/Services/Security/` | Owns credential verification, lockout policy, selected-factor authorization, and protected-operation gating. |
| `ProtocolCommandRouter` | app service | `SecRandom/Services/Ipc/ProtocolCommandRouter.cs` | Normalizes URL/IPC routes, routes protected commands, and returns structured IPC results. |
| `DeviceUuidStore` | app service | `SecRandom/Services/Config/DeviceUuidStore.cs` | Persists the pseudo-anonymous device UUID separately in `data/config/device-uuid.json` and migrates legacy settings values. |
| `AttachedSettingsRegistryService` | registry | `SecRandom.Core/Services/AttachedSettingsRegistryService.cs` | Static collections for attached-settings controls. |
| `ViewModelBase` | base VM | `SecRandom/ViewModels/ViewModelBase.cs` | Base VM exposing `MainConfig`; inherits `ObservableRecipient`. |
| `GlobalConstants` | constants | `SecRandom.Core/GlobalConstants.cs` | Version, platform, and development-mode constants. |
| `DrawCommitCoordinator` | domain service | `SecRandom.Core/Services/Draw/DrawCommitCoordinator.cs` | `IDrawCommitService` implementation: single `DrawRoundId`, temp→history commit order, snapshot compensation, serialized gate. |
| `DrawRepeatPolicy` / `DrawCandidateFilter` | draw rules | `SecRandom.Core/Services/Draw/` | Shared repeat-threshold and candidate-filter rules; replaces formerly duplicated copies. |
| `DataArchiveService` | domain service | `SecRandom.Core/Services/Archive/DataArchiveService.cs` | Platform-neutral v3 backup/archive engine: validation, staging commit/rollback, snapshots. |
| `IArchivePostImportHooks` | seam | `SecRandom.Core/Services/Archive/IArchivePostImportHooks.cs` | Platform follow-up after archive import; Core registers Null hooks, desktop overrides them. |
| `ProfileCatalogManager` | domain service | `SecRandom.Core/Services/Profiles/ProfileCatalogManager.cs` | List/profile CRUD and student/prize history clearing behind `IProfileCatalogManager`. |
| `RosterImportParser` | parser | `SecRandom.Core/Services/Profiles/RosterImportParser.cs` | Shared roster spreadsheet parsing and column mapping for desktop/mobile imports. |
| `MobileRollCallService` | mobile service | `SecRandom.Mobile.Shared/Services/MobileRollCallService.cs` | Mobile list/scope/count snapshots, multi-member draws, remaining list, and scoped temporary reset without changing the Core session contract. |
| `MobileMediaLibraryService` / `MobileDrawMediaService` | mobile services | `SecRandom.Mobile.Shared/Services/` | Mobile-private media import/reference cleanup and draw-time per-record image/music/voice orchestration through head-injected native playback. |

## CONVENTIONS
- `docs/project_rules.md` overrides inference when adding features.
- General settings now live under `MainConfigModel.General`; `MainConfigModel.Basic` / `Backup` remain compatibility bridges for existing callers while new config splits belong under `SecRandom.Core/Models/SubConfigs/General/`.
- Basic settings are functional runtime controls: `ShowStartupWindow`, primary-window topmost mode, and background residency apply only to the primary `MainWindow`; `AutoSaveWindowSize` preserves independent geometry/maximized state for the primary and settings windows. Cross-platform autostart and `secrandom://` protocol registration belong to app-layer `DesktopIntegrationService`, which must use user-level Windows/Linux/macOS mechanisms and not silently persist a failed integration request.
- Selecting `TopmostMode.UiAccess` for the primary or floating window persists the setting and requests a restart. `SecRandom.Desktop/UiAccessStartup` is the Windows-only pre-Avalonia launcher boundary: following `killtimer0/uiaccess`, it elevates a bootstrap process, impersonates same-session `winlogon.exe`, sets `TokenUIAccess` on a duplicate of the bootstrap's current token, and starts the UIAccess replacement before Avalonia initializes. The original process waits only for the bootstrap to create the replacement, never for Avalonia or Host initialization; denied elevation, preparation failure, or timeout keeps the original process in ordinary topmost and preserves the configured mode for a later retry.
- Windows UIAccess topmost builds may use `SecRandom.Desktop/app.uiaccess.manifest` when `EnableUiAccess=true`; that manifest requires Authenticode signing and installation under Program Files. Independently, the runtime `UiAccessStartup` token preparation follows `killtimer0/uiaccess` and works from the ordinary manifest after UAC authorization, so debug and portable Windows builds can exercise UIAccess as well. The release workflow enables the manifest only when Windows code-signing secrets are available.
- Point-call students and lottery prizes use `RecordId` as the internal stable identity for history/fairness. The visible `Id` field is optional display metadata only and must not be required by import, draw, or history logic.
- A roll-call student or lottery prize candidate must be enabled and have a nonblank `Id` or `Name`. Import may map either column and excludes rows where both are blank.
- Picking `ClearRecord` controls temporary draw records only; do not clear persistent profile histories from that setting. RollCall and QuickDraw share the same student temporary record store, while Lottery uses prize temporary records.
- Privacy settings split Sentry upload from online status reporting: `SentryTelemetryEnabled` only controls `SecRandom/Services/Telemetry/`, while `OnlineStatusMode` only controls `SecRandom/Services/OnlineStatusService.cs`.
- Telemetry access is seam-based: runtime code depends on `ITelemetryTransaction` / `ITelemetrySdkAdapter` and must not reference Sentry types directly (the DSN lives in `GlobalConstants.SentryDsn`). `TelemetryRuntimeService` is Sentry-free; the desktop-only `TelemetryTransactionSentryExtensions` shim is excluded from mobile builds, while mobile links the same telemetry sources, adds `MobileProfilingIntegrationStub`, and owns its Sentry DI/init/shutdown plus Android/iOS unhandled-exception hooks.
- The pseudo-anonymous device UUID belongs in `data/config/device-uuid.json`; legacy settings values are migration-only and must not be serialized back into `settings.json`. Full-data and configuration backups include this file, while settings-only exports do not overwrite a device identity.
- ClassIsland notifications require the installed `SecRandom4Ci` v2 plugin. `NotificationService` uses the local `SecRandom4Ci.Interface` contract and `ClassIsland.IPC.v2.Server`; it must not use loopback TCP. SecRandom's built-in notification channel is the existing QuickDraw result window, shared by roll-call, QuickDraw, and lottery notifications. Each draw channel only controls whether it is enabled; `NotificationSettings.Default` owns the one global service, duration, threshold, fallback, and built-in auto-close configuration. `UseBuiltInOnServiceFailure` is the backend-neutral fallback policy for a selected external notification service and defaults to enabled; current ClassIsland-only delivery uses it when v2 IPC fails. For automatic QuickDraw delivery, the built-in or dual service opens this window before the draw to show the rolling animation and then its result; external-only delivery must not open the QuickDraw window unless the fallback runs. Notification settings must not duplicate the QuickDraw window's placement, opacity, or draw-animation controls.
- Crash recovery mode lives under `MainConfigModel.General.CrashRecovery`; prompt startup handling must run before single-instance acquisition, while normal restart must release the single-instance service before relaunch.
- First-run OOBE is a top-level window shown immediately after `BuildHost()` and before floating-window/main-app initialization. Its carousel begins with a non-blocking, centered welcome screen with the application icon; it is outside the seven configuration pages, whose first item places the collapsed-by-default verifiable-draw notice before the privacy-policy and GPLv3 acknowledgement controls. Each requires explicit acknowledgement. The welcome page owns the lower-left language selector; changing it persists the language, refreshes ViewModel-derived localized values, then recreates only the OOBE visual tree into localized resources without restarting or leaving the first-run flow. An active import drawer defers the visual replacement until its preview/mapping flow closes. `GuideCompleted`, `AcceptedVerificationNoticeVersion`, `AcceptedPrivacyPolicyVersion`, and `AcceptedGplVersion` determine whether an OOBE acknowledgement is required. After the full guide has completed, a verifiable-draw notice, privacy-policy, or GPL version update opens only the acknowledgement page and its privacy controls, not unrelated setup pages. OOBE class/prize spreadsheet imports reuse the list-management import controls in its own right-side drawer, so their file mapping, preview, duplicate handling, and cancel behavior stay aligned with the settings list pages. Completing OOBE continues into normal startup and explicitly opens the primary main window; it must not exit merely because the OOBE window closes. Verifiable-draw notice, privacy-policy, and GPL changes increment their app-layer OOBE versions independently.
- Settings JSON, full-data ZIP, and backup restore only accept manifest/envelope exports produced by SecRandom v3. Validate `producer_version` before taking an import snapshot or changing data; unsupported sources must be explained in a modal dialog.
- Security credentials must never be stored in `MainConfigModel` or `settings.json`. Keep passwords, TOTP seeds, USB binding tokens, and lockout state in `SecRandom/Services/Security`'s separate credential store; ordinary settings only select factors and protected operations.
- Security authorization always flows through `ISecurityService`; do not add direct validation checks to tray handlers, windows, ViewModels, plugins, or linkage code. Passwords require at least 6 characters, with no artificial character-class rule.
- Full IPC/URL compatibility is app-layer routed through `ProtocolCommandRouter`: structured current-user named-pipe IPC is additive to legacy `ShowMainWindow`/`Restart`/`Url:` delivery, and all external mutations must use `ISecurityService`. `data/*` queries must use non-mutating profile snapshots, never active-profile loading APIs.
- `HistoryItem.DrawRoundId` identifies every record committed by one logical draw. Populate it for new history writes; IPC history projections group by it and must never expose internal `RecordId`.
- Draw commits must go through `IDrawCommitService` (`DrawCommitCoordinator`): one logical draw gets exactly one `DrawRoundId` (a caller may supply one), temporary records commit before persistent history, a mid-commit failure rolls back through snapshot compensation, and commits serialize behind the coordinator gate. Never reintroduce bare two-step writes of `IProfileService.Record*History` plus temporary-record calls; the optional `drawRoundId` parameter (and `drawMethod` on `RecordPrizeHistory`) exists for coordinator use.
- File persistence saves are atomic replacements: `FileConfigService.SaveConfig` and `DrawTemporaryRecordService.SaveState` write a temporary file and atomically replace the target; new file-persistence code must follow the same pattern instead of overwriting files in place.
- Draw proofs use the SecRandom v3 algorithm ID and an `algorithmEngineVersion`. Engine v3.1 commits the sampling mode into the request: student draws use history-balanced weighted sampling, count-based lottery inventory uses equal-probability partial permutation when no internal rule applies, and pan or internal-rule lottery draws use weighted sampling without replacement. Keep the verifier's replay logic byte-compatible with each supported proof engine version.
- Ordinary draws immediately save an `OfflineReproducible` proof, then submit that newly generated proof once to `fair.sectl.cn` for background replay/signing. Startup, mode changes, and failed requests must not scan or retry historical proof files. A current server receipt signs a canonical hash of all proof fields except the receipt itself, plus the proof ID, input hash, payload hash, anonymous audit-payload hash, and mode; public sharing requires that receipt. This detects changes after the signed submission, but it does not attest to an unmodified local executable, an authentic real-world candidate pool, or absence of pre-draw result selection. Do not call it a pre-draw server witness. Local proof retention applies both the configured age and total storage limits; after every new save, delete oldest `.srproof.json` files until the configured storage cap is met.
- Formal notarization is the explicit alternative to ordinary replay attestation: the client sends a zero-seed anonymous request, `fair.sectl.cn` durably locks it, persists server random material after the lock, calculates the result, and returns an immutable `OnlineWitnessed` proof. Formal mode waits for that response and must never substitute a local draw after an error; its ledger is independent of retention-limited public proof sharing. It protects the locked flow against local code/seed/proof replacement, but cannot establish real-world roster authenticity, completeness, or pre-submission pool integrity.
- Proof ordering TODO: attestation is still enqueued when a draw completes rather than strictly after the commit boundary; deferring proof finalization/attestation until after the `IDrawCommitService` commit remains an open task. Keep this note until that ordering lands.
- Settings preview is a security-prompt outcome, not URL authorization bypass. When enabled, it freezes page content while preserving settings navigation and must not mutate configuration.
- ViewModels must be registered in `SecRandom/App.axaml.cs` `BuildHost()`; reusable services also go through Host.
- The cross-platform view engine lives in `SecRandom.Core/Views/`. It separates logical Avalonia `Control` sessions, presentation intent, close/result handling, factories, and hosts. Core contracts must not expose `Window`, platform lifetimes, native APIs, or raw `IServiceProvider`; desktop and mobile shells register their physical presenters through DI. Plugins may receive only a future restricted view service, never the app-wide route service or host registry.
- Desktop embedded navigation goes through `IViewEngine.ShowExclusiveAsync(hostId, viewId)` on single-stack embedded hosts. `MainView`/`SettingsView` decide Frame↔MVE routing by `IViewRegistry` lookup (registration means MVE-hosted) with explicit `SetEmbeddedMode` mutual exclusion; both windows synchronously tear down embedded hosts in `Closing` (respecting `e.Cancel`), and settings read-only preview freezes the embedded host's parent container rather than Frame page content.
- Platform feature callers must resolve narrow platform contracts from Host. `PlatformStartupContext` is startup-only: desktop `Program` sets it before Avalonia starts, and desktop `App` reads it once to register the selected root. On mobile, the `SecRandom.Android` / `SecRandom.iOS` entry points set it with a `MobilePlatformServiceRoot` before `SecRandom.Mobile.MobileApp` builds and consumes its independent minimal Host. Do not use it from views, ViewModels, Core, or business services.
- Window feature requests use `IWindowFeatureService` with a neutral `PlatformWindowHandle`, `WindowFeatureRequest`, and explicit `Applied`/`Unsupported`/`Failed` result. Keep Win32/X11/AppKit operations in the matching `SecRandom.Platforms.<OS>` project.
- `TopmostMode.UiAccess` remains a Windows process-token capability controlled by `SecRandom.Desktop/UiAccessStartup.cs`; it is not a generic window feature or a responsibility of platform window services.
- Mobile is split into the neutral `SecRandom.Mobile.Shared` library (always net10.0; assembly name and namespaces stay `SecRandom.Mobile`) plus the `SecRandom.Android` / `SecRandom.iOS` entry heads, so normal desktop solution builds do not require mobile workloads. `BuildMobile=true` enables the mobile TFM on the two head projects (`net10.0-android` / `net10.0-ios` Exe); the `MobileTargetFramework` switch no longer exists — build the head project directly for a platform-specific CI build. Platform-specific code lives only in the heads behind shared seams (`IMobileUpdateInstaller`, `IMobileMediaPlayer`, `MobilePlatformServiceRoot.StartupErrorLogger`); the shared library contains no `#if ANDROID` / `#if IOS` code. The independent `MobileApp` calls `Utils.ConfigureMobileDataRoot()` before it builds its minimal Host, calls `AddCoreRuntimeServices()`, assigns/clears `IAppHost.Host` for the existing transitional Core handlers, starts `ISingleViewApplicationLifetime` with its own `MobileRootView`, and must not start desktop-only services or reference the desktop application assembly. CI builds Android packages and unsigned iOS arm64 IPAs; the iOS job is a required input to manual release publication, and the IPA is included in the signed release manifest and GitHub Release assets. iOS device distribution and update delivery remain deferred.
- Mobile UI, language, backup, media, and telemetry conventions (component library, theme dictionaries, animation primitives, culture reload, StorageProvider archive/media flows, native media seam, Sentry wiring) are documented in `SecRandom.Mobile.Shared/AGENTS.md`; keep mobile-specific rules there instead of duplicating them here.
- Resolve shared services via `IAppHost.GetService<T>()` / `TryGetService<T>()` unless constructor injection is already the local style.
- Navigation pages need `[PageInfo(...)]` plus `services.AddMainPage<T>()` or `services.AddSettingsPage<T>()` in `BuildHost()`. `AddMainPage`/`AddSettingsPage` now carry navigation metadata only; every built-in main/settings page is a Core `ViewBase` additionally registered with `AddView<T>(pageId)` so the view engine can host it. Plugin `plugin.<id>.*` pages intentionally keep the FAFrame fallback path (the Frame dual-track is deliberate).
- Built-in main navigation entries may use `PageLocation.Bottom` for bottom-pinned sidebar items; roll-call (`main.rollCall`) and lottery (`main.lottery`) are bottom-pinned and full-width/title-hidden. Quick draw is not a main navigation page and opens from the floating window.
- Page IDs: `main.xxx`, `settings.xxx`, `settings.group.xxx`.
- Picking animation style is unified: settings expose it as `AnimationStyle` / “动画样式”, and RollCall, QuickDraw, and Lottery use the same style for both rolling preview/process animation and final result reveal. Do not split process/result animation style settings.
- Managed draw music uses a platform-specific runtime boundary: desktop `settings.personalized.music` imports/deletes/previews MP3/WAV/FLAC files through `MusicLibraryService`/private SoundFlow playback, while mobile `MobileMediaLibraryService` uses StorageProvider streams into `data/audio/music` and `IMobileMediaPlayer` supplied by Android/iOS heads. Draw-settings and student/prize attached settings use the same managed IDs, no-music, or random-play options; the first drawn record supplies both overrides for a multi-record result. Deleting a managed track clears global and per-record references. Mobile display images likewise import through StorageProvider streams into `data/images`; image/music/voice failures must not invalidate a committed draw.
- Course linkage uses fixed v2 data-source values: `0=Off`, `1=CSES`, `2=ClassIsland`. CSES schedules live at `data/CSES/cses_schedule.yml`; ClassIsland is accessed only through the app-layer official IPC adapter. Only a confirmed course break restricts local draw/reset or hides the floating window. Missing/invalid CSES data, ClassIsland connection loss, or an unknown state must permit normal operation.
- CSES schedule errors follow the “`InvalidDataException` + `Data` error code” convention: `CsesScheduleException` is a static factory that throws `InvalidDataException` (sealed, so it cannot be subclassed) with a `CsesScheduleError` code and optional argument stored in `Exception.Data`; UI retrieves them through `CsesScheduleException.TryGetError` and localizes from resources.
- `LinkageSettings.VerificationRequired` governs the course-time bypass prompt and is distinct from `SecuritySettings.ProtectLinkage`, which continues to protect only external SecRandom URL/IPC mutations. Student course history uses `HistoryItem.CourseName` with `RecordId` identity; empty legacy course values remain global history.
- Plugin pages are runtime-registered through `AddPluginMainPage` / `AddPluginSettingsPage`; their IDs must start with `plugin.<plugin-id>.` and must not occupy built-in `main.*` or `settings.*` IDs.
- Plugin contracts live under `SecRandom.Core/Plugins`; plugin runtime/loading state lives under `SecRandom/Services/Plugins` and is registered from `BuildHost()`.
- Plugin logs must use the original logging pipeline. Plugin categories use `SecRandom.Plugin[<plugin-id>].*`; plugin detail views may only filter their own category prefix.
- Fair draw internals are not plugin API. Plugins may only call `IPluginDrawInvoker` with declarative DTOs; do not expose `DrawEngine`, `WeightedDrawEngine<T>`, random sources, weight calculators, histories, or writable draw config to plugins.
- File/data paths should go through `Utils.GetFilePath(...)`. Desktop and portable deployments keep mutable data under the package root's `data/` directory. `SecRandom.Mobile.MobileApp` is the controlled exception: before any path is read, it sets `Utils` once to its app-private local-data root; no Core service, view, plugin, or desktop code may redirect it later.
- Portable ZIP updates use `SecRandom.Launcher` at the stable package root and activated `app-*` payload directories. The application, not Launcher, validates/downloads/extracts/activates a complete ZIP; `Utils.PackageRoot` / `DataRoot` keep user data stable across payload versions.
- Update discovery uses a fixed GitHub mirror first, with GitHub direct access as fallback. A detached Ed25519-signed release manifest and each artifact's length/SHA-512 must verify before deployment or a system installer is started; signing keys remain outside the repository. Desktop artifacts, the signed Android arm64 APK, and the unsigned iOS arm64 IPA share this manifest. Android downloads its verified APK then delegates installation to the system package installer; iOS device distribution and in-app update delivery remain deferred.
- Localization is per page folder: `Resources.resx`, `Resources.Designer.cs`, and optional culture files such as `Resources.en-US.resx` / `Resources.ja-JP.resx`; preserve exact filename casing used on disk.
- In `.csproj`, register only `Resources.resx` and `Resources.Designer.cs`; do not register every language variant.
- Resource designer generator must be `PublicResXFileCodeGenerator`.
- Localization keys: `S_` settings, `S_xxx_D` description, `S_xxx_R` real key, `O_` options, `M_` messages, `C_` controls.
- Views commonly set `DataContext = this` and expose `ViewModel`; bindings use `ViewModel.*`.
- ViewModels use CommunityToolkit MVVM (`ObservableRecipient`, `[ObservableProperty]`); app VMs inherit `SecRandom.ViewModels.ViewModelBase`.
- `Global.props` carries the main MSBuild behavior: unsafe enabled, Windows targeting enabled, SourceLink, full debug symbols, and default exclusion of project-local `artifacts/` / `publish/` output trees from SDK item globbing. Its `GitInfo` generator and shared `AssemblyInfo.cs` are enabled only by final entry assemblies (`SecRandom.Desktop`, `SecRandom.Android`, and `SecRandom.iOS`), because generated `SecRandom.GitInfo` types must not leak from referenced class libraries.
- `Directory.Build.props` only pins `AvaloniaVersion`.
- Standalone verification scripts live under `scripts/`; keep them self-contained and write outputs under `artifacts/`.
- Release CI keeps generated material under `artifacts/release/`: RID publish trees under `publish/`, portable ZIP assembly under `portable/`, Windows installer staging under `installer/` and `setup/`, platform package workspaces under `linux/` / `macos/`, upload candidates under `dist/`, and release-job downloads/final signed assets under `downloaded/` / `output/`. The Android job stages its signed arm64 APK separately, then the release job includes it in `output/`, the signed manifest, and the GitHub release. Portable ZIP contents must remain a root `SecRandomLauncher` plus one valid `app-*` payload directory; do not rearrange that runtime package contract.
- More settings owns built-in draw page chrome options such as roll-call/lottery control panel position and control visibility; do not hard-code those controls outside the page/config binding.
- `MoreSettings.LotteryEnabled` is the single capability switch for lottery. It must consistently gate main navigation, floating-window buttons, shortcuts, and URL/IPC routes.
- Application-owned Fluent System Icons use the `Filled` variant by default across navigation, settings, buttons, menus, floating windows, and empty states. Use the closest semantic `Filled` icon when no same-name variant exists.
- New application icon references must use `FluentIcon`, `FluentIconSource`, `sr:Fi`, or `FluentIcons.*`; do not add raw Fluent Unicode glyphs. When migrating a raw glyph, reverse-map its code point through `SecRandom.Core/Assets/FluentSystemIcons-Resizable.json` before choosing the Filled replacement.
- Brand images, author/organization images, window icons, and taskbar icons are separate resources and are not part of Fluent glyph style migration. FluentAvalonia template glyphs remain framework-owned unless an application-owned style explicitly overrides them.

## COMMENT STYLE
- Keep comments that explain non-obvious project constraints, platform quirks, or AI-prone rules.
- Do not add comments that merely restate a method name or obvious assignment.
- Existing emphatic comments in `BuildHost()` about ViewModel registration reflect a real project invariant; preserve the rule even if wording is later cleaned up.

## MAINTENANCE CHECKLIST
AI agents must update AGENTS files when they:
- Add, remove, or rename top-level folders or projects.
- Change `TargetFramework`, package family, `AvaloniaVersion`, CI SDK versions, or publish RIDs.
- Change Host/DI registration, navigation page registration, page IDs, or default pages.
- Change localization folder layout, resource designer registration, or culture filename casing.
- Move draw/config/profile services or alter Core/Shared namespace boundaries.
- Discover a durable convention while fixing code that is not yet documented here.
- Remove stale facts encountered during work instead of leaving them for a later cleanup.

## ANTI-PATTERNS (THIS PROJECT)
- Do not add ViewModels without Host registration in `BuildHost()`.
- Do not hardwire navigation menu items; register pages/groups and let registry/services build menus.
- Do not `new` reusable services from pages; put reusable/singleton/testable services in Host.
- Do not give plugins raw `IAppHost.Host`, full `IServiceProvider`, writable `MainConfigHandler`, writable `IProfileService`, shell/process helpers, or direct file access outside their private plugin data directory.
- Do not merge page localization into a shared resource bucket.
- Do not assume dictionary mutations auto-save config; call save after collection mutations or on unload.
- Do not use `Student.Id` / `Prize.Id` as a required identity key; use `ProfileRecordIdentity` / `RecordId` and keep legacy `Id`/`Name` history fallback ambiguity-safe.
- Do not treat `SecRandom.Desktop` as the app logic layer; it is only the launcher.
- Do not edit `bin/`, `obj/`, `artifacts/`, `publish/`, packaging scratch dirs, or generated build output.
- Do not encode temporary local environment facts in AGENTS files; prefer durable rules that future AI agents can maintain.

## UNIQUE STYLES
- Avalonia compiled bindings are enabled in `SecRandom/SecRandom.csproj`.
- `SecRandom/Styles.axaml` is only the app style entrypoint; shared styles live under `SecRandom.Core/StylesBase.axaml` and `SecRandom.Core/Styles/`.
- Shared UI controls are in Core, not only app: `DrawerHost`, `Field`, `IconText`, `AppToastAdorner`, `AttachedSettingsControlPresenter`, `FluentIcon`, `FluentIconSource`, `StickyScrollViewer`, `TouchDragThumb`, `Empty`, `Emptiable`, `DevelopmentBuildAdorner`.
- UI uses FluentAvalonia `NavigationView`, `SettingsExpander`, `FluentIconSource`, and custom selector classes (`compact`, `nav-back`, `drawer-left/right`, `FullWidth`).
- Default font is MiSans from Avalonia resource URI `avares://SecRandom/Assets/Fonts/MiSans/#MiSans`.
- README/docs are Chinese-first with English/ZH-TW mirrors under `resources/`.

## COMMANDS
```bash
dotnet restore SecRandom.sln
dotnet build SecRandom.sln -c Release --no-restore
dotnet test SecRandom.sln -c Release --no-build
dotnet run --project SecRandom.Desktop/SecRandom.Desktop.csproj
dotnet run --project scripts/FairnessAudit/SecRandom.FairnessAudit.csproj -c Release
dotnet publish SecRandom.Desktop/SecRandom.Desktop.csproj -c Release -r <rid> --self-contained true -o artifacts/SecRandom-<rid> /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
```

CI RIDs: `win-x64`, `win-x86`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`.

## NOTES
- Build workflow uses .NET SDK `10.0.100`; publish job currently uses `9.0.311`; CodeQL uses `10.0.x`.
- Release workflow triggers on tags `v*`, manual dispatch, PR/push build, or commit message containing `开始构建`.
- Test project currently includes `UnitTest1.cs` coverage for legacy privacy/telemetry migration behavior.
- `vendors/EdgeTtsSharp/` is the Edge TTS synthesis submodule. Its embedded voice list and synthesis source are compiled by the app; `Services/Voice/EdgeTtsSharpCompatibility.cs` supplies the cross-platform transport seam, and all playback remains in ClassIsland's `SoundFlow` MiniAudio package from the repository-wide MyGet source.
- README mentions `vendors/pythonnet-stub-generator/`; treat it as third-party if present in future snapshots.
- CodeQL workflow (`codeQL.yml`) runs on push, PR, and weekly schedule; C# scans use manual build mode with .NET SDK `10.0.x`.
- `SecRandom.Core` is plugin-facing per `docs/namespaces.md`; keep its public contracts stable.
