# SecRandom.Core/ AGENTS.md

<!--
Core supplement to ../AGENTS.md. Update this file when
draw/config/logging services, shared controls/styles, or registry helpers move.
AI agents touching those areas must update this file in the same task.
-->

## OVERVIEW

Core module for domain logic, config/logging services, shared Avalonia controls/styles,
behaviors, enums, and models.

## STRUCTURE

```
SecRandom.Core/
├── Abstraction/          # Host/service contracts, including IAppHost and IProfileService
├── Attributes/           # PageInfo, attached-settings usage/info metadata attributes
├── Behaviors/            # Shared Avalonia behaviors
├── Controls/             # Reusable Avalonia controls/templates
├── Converters/           # Shared Avalonia converters
├── Assets/               # Icon mapping JSON inputs for generated Fluent/Lucide icon enums
├── Helpers/              # Core helper utilities
├── Interfaces/           # Core-facing interfaces
├── Views/                # Logical view/session contracts; app shells provide physical hosts
├── Styles/               # Modular shared style files
├── StylesBase.axaml      # Shared style hub imported by app
├── Services/Draw/        # Fair/random draw engine, filters, commit coordinator, shared repeat/candidate rules
├── Services/Archive/     # Platform-neutral v3 backup/archive engine (DataArchiveService + post-import hooks)
├── Services/Config/      # Config handlers over Shared config models
├── Services/Profiles/    # Host-internal profile persistence runtime shared by desktop/mobile
├── Services/Ipc/         # Strict URL/IPC request parsing and normalization
├── Services/Logging/     # Console/file logging providers/formatters
├── Extensions/Registry/  # DI/page registration helpers
├── Enums/                 # Draw settings types, page location, config model trees
├── Models/               # Page info, draw models, subconfig models
├── GlobalConstants.cs     # Version/platform/development constants
└── Langs/                # Core common localization resources
```

## WHERE TO LOOK

| Task                       | Location                                                                         | Notes                                                                   |
|----------------------------|----------------------------------------------------------------------------------|-------------------------------------------------------------------------|
| Service access             | `Abstraction/IAppHost.cs`                                                        | Static Host holder and service helpers.                                 |
| Profile contract           | `Abstraction/Services/IProfileService.cs`                                        | Current profile/list/history boundary, including active student-profile switching. |
| Page metadata              | `Attributes/PageInfoAttribute.cs`, `Models/PageInfo.cs`                          | Used by registration extensions.                                        |
| Page registration          | `Extensions/Registry/`                                                           | `AddMainPage`, `AddSettingsPage`, group/separator helpers.              |
| Navigation registry        | `Services/PagesRegistryService.cs`                                               | Static main/settings/group collections.                                 |
| Attached-settings registry | `Services/AttachedSettingsRegistryService.cs`                                    | Static attached-settings control collections.                           |
| Draw algorithm             | `Services/Draw/DrawEngine*.cs`, `WeightedDrawEngine.cs`, `CryptoRandomSource.cs` | Fairness, filters, weighted sampling; history lookup uses `RecordId`. Repeat thresholds and candidate filtering live in the shared `DrawRepeatPolicy`/`DrawCandidateFilter`. |
| Draw commit boundary       | `Services/Draw/DrawCommitCoordinator.cs`, `Abstraction/Services/IDrawCommitService.cs` | Single-`DrawRoundId` transactional commit: temporary records before history, failure snapshot compensation, serialized commit gate. |
| Config handlers            | `Services/Config/`                                                               | `FileConfigService`, `MainConfigHandler`, and `ProfileConfigs` implement host-internal JSON persistence. |
| Profile runtime            | `Services/Profiles/ProfileService.cs`                                            | Injected current-list/history runtime shared by desktop and mobile hosts. |
| Profile catalog            | `Services/Profiles/ProfileCatalogManager.cs`                                     | List/profile CRUD, rename/file migration, and student/prize history clearing behind `IProfileCatalogManager`. |
| Roster import              | `Services/Profiles/RosterImportParser.cs`                                        | Shared roster spreadsheet parsing/column mapping used by desktop and mobile import flows. |
| Temporary draw records     | `Services/Draw/DrawTemporaryRecordService.cs`                                   | Host-internal student/prize temporary records shared by desktop and mobile hosts. |
| Feature availability       | `Services/FeatureAvailabilityService.cs`                                        | `MoreSettings.LotteryEnabled` runtime gate behind `IFeatureAvailabilityService`. |
| Data archive runtime       | `Services/Archive/`                                                              | `DataArchiveService` (v3 settings/data transfer, manifest/SHA-256, staging commit/rollback, snapshots) with `IArchivePostImportHooks` seam for platform follow-up; registered by `AddCoreRuntimeServices`. |
| Protocol parsing           | `Services/Ipc/ProtocolRequestParser.cs`                                          | Bounded route/query parser shared by URL and IPC routing. |
| Logging providers          | `Services/Logging/`                                                              | Console/file logging; file logs live under `data/logs`, current log path is exposed by `FileLoggerProvider` for viewer/diagnostics. |
| Config schema              | `Enums/Configs/`, `Models/SubConfigs/`                                           | Many settings model types live here, including v2-parity models for floating window, notification, security, linkage, voice, history, update, and more settings. |
| Shared controls            | `Controls/*.axaml(.cs)`                                                          | Reusable app controls; keep templates and code-behind paired.           |
| Cross-platform view engine | `Views/`                                                                         | Logical view lifecycle, presentation intent, DI factory/service, and host contracts. |
| Shared styles              | `StylesBase.axaml`, `Styles/*.axaml`                                             | Imported by `SecRandom/Styles.axaml`.                                   |
| Constants/helpers          | `GlobalConstants.cs`, `Helpers/`                                                 | Keep cross-cutting values here only when Core consumers need them.      |

## CONVENTIONS

- `Views/` is the public logical view-engine boundary. It may use Avalonia `Control` but must not expose `Window`, application lifetimes, native platform APIs, or raw `IServiceProvider`. Physical desktop/mobile hosts are registered by their application shells through DI.
- Existing Core services may use `IAppHost.GetService<T>()` during the transition, but `DrawEngine` and new reusable runtime services use constructor injection. Construct `DrawEngine` with `MainConfigHandler`, `IProfileService`, and `ILogger<DrawEngine>`; do not add a new static-Host dependency.
- `IProfileService.LoadStudentProfile(name)` switches the app-layer active point-call student list and matching history; callers should use it instead of constructing profile configs directly when changing the active roll-call list.
- Registration helpers are responsible for both keyed DI and `PagesRegistryService` metadata.
- `DrawEngine` is partial: keep filtering in `DrawEngine.Filter.cs`, weight math in `DrawEngine.WeightCalculator.cs`,
  orchestration in `DrawEngine.cs`.
- Weighted drawing validates count, candidates, and weights before sampling; preserve explicit `DrawStatus` returns over
  exceptions at public boundary.
- Draw fairness/repeat history for students and prizes must use `ProfileRecordIdentity`/`RecordId` first. Legacy `Id`/`Name` history fallback is only for backward compatibility and must stay ambiguity-safe.
- Draw commits are coordinator-only: app sessions and page ViewModels must call `IDrawCommitService` (`DrawCommitCoordinator`) instead of pairing temporary-record writes with `IProfileService.Record*History`. The optional `drawRoundId` (and `drawMethod` on `RecordPrizeHistory`) parameters exist for coordinator use; a mid-commit failure rolls back through snapshot compensation, and commits serialize behind the coordinator gate.
- Student fair-draw execution must support explicit internal policy snapshots. Desktop public `DrawEngine` methods stay byte-compatible by deriving a `DesktopConfigured` snapshot from live `MainConfigModel.FairDrawSettings`; mobile fair draws use the fixed `MobileDesktopDefaultsV1` snapshot and must not read persisted fair-setting values.
- Verification proof inputs commit a `VerificationSamplingMode` and `VerificationAlgorithmProfile`. The profile must match the draw kind and sampler: fair/random students use history-balanced or unit-weight sampling when no behind-scene rule is active, student behind-scene weighting uses a dedicated profile, Count lottery uses equal-probability partial inventory permutation without behind-scene rules, and Pan or an internal-rule fallback uses weighted-without-replacement. Any internal rule, including zero-probability exclusions, must stay visible in the ordinary verification draw's anonymous audit payload. Formal notarization is the explicit exception: it must ignore internal rules completely and omit them from its locked input and audit payload.
- Config handlers derive from `ConfigHandlerBase<TModel>`; config model defaults should be safe without existing data
  files.
- `FileConfigService`, `ProfileService`, `DrawTemporaryRecordService`, and the concrete feature-availability service are private implementation details behind `AddCoreRuntimeServices()`. Desktop and mobile call that registration extension from their composition roots while exposing only the established narrow contracts to consumers.
- `Services/Archive/` owns all v3 settings/data ZIP and settings-JSON transfer: `DataArchiveService` performs producer-version/manifest/SHA-256 validation, staging commit/rollback, and mandatory pre-import snapshots. Platform-specific follow-up stays behind `IArchivePostImportHooks`; `AddCoreRuntimeServices()` registers the Null hooks and shells override them (desktop: `DesktopArchivePostImportHooks`).
- IPC parser code is UI-free and must reject ambiguous routes, malformed percent escapes, control characters, oversized frames, and unsupported schemes. Keep route execution in the app layer.
- File logging should keep user-facing log messages in Chinese for app events. Avoid logging student/prize names or full config payloads; prefer counts, status, file names, and operation names.
- V2-parity settings models that are shared by app settings pages but not yet backed by services live directly under `Models/SubConfigs/` and hang off `MainConfigModel` until their runtime service boundaries settle. `MoreSettingsConfig` also owns built-in draw page chrome toggles such as roll-call/lottery control panel placement and visibility.
- Linkage enums and course snapshot DTOs are UI-free Core contracts. The app owns CSES files, ClassIsland IPC, timers, and window behavior. Student draw APIs may accept an optional course name to project `HistoryItem.CourseName`; empty course input must retain global-history behavior.
- Attached settings models under `Models/AttachedSettings/` may target students and prizes. `SpecificAnnouncementAttachedSettings` stores per-record TTS alias, prefix, and suffix; `DrawMusicAttachedSettings` stores per-record animation/result track IDs. App-layer controls/services render and consume both. `DrawSettingsConfigBase.VoiceAnnouncementEnabled` uses the existing default-and-per-draw override pattern; `VoiceSettings.VoiceEnable` remains the runtime total switch. The app-facing `ISpeechProvider` and `ISpeechAudioPlayer` contracts live under `Abstraction/Services` so voice acquisition remains separate from playback; synthesis stays at normal speed, while the single SoundFlow player applies the configured speed through pitch-preserving WSOLA time stretching.
- Attached-settings presenter behavior separates editing from activation: the expander content should remain openable/editable even when `IsAttachSettingsEnabled` is false, while the switch only controls whether the saved settings take effect.
- `MainConfigModel.General` is the canonical general-settings subtree. Legacy root `Basic` / `Backup` bridges may remain temporarily for backward-compatible callers and JSON migration, but new general config splits should be nested under `Models/SubConfigs/General/`. Privacy settings belong under `General.PrivacySettings`; keep Sentry upload (`SentryTelemetryEnabled`) separate from online status reporting (`OnlineStatusMode`).
- Controls should keep `.axaml` and `.axaml.cs` side by side and expose reusable Avalonia properties/templates.
- `TouchInputModeAssist` tracks the latest top-level pointer mode, and `TouchTextCommandBarFlyout` provides the touch-only horizontal cut/copy/paste editor toolbar with overflow undo/redo/select-all. `Styles/TouchTextCommandBarFlyout.axaml` applies it to `TextBox` only while touch mode is active; do not replace mouse/keyboard text context menus.
- Shared styles are modular; add new broad styles under `Styles/` and include from `StylesBase.axaml`.
- `MultiComboBox` (`SecRandom.Core/Controls/MultiComboBox.cs`, `MultiComboBoxItem.cs`) is the in-house multi-select control; its theme lives in `Styles/MultiComboBox.axaml` and uses a shared maximum width cap so selected tags do not stretch the control indefinitely; override locally only when a page really needs a wider selection box.
- `MultiComboBox` settings should use `ItemsSource`/`SelectedItems` with plain data objects for selectable options; do not put visual controls like `TextBlock` or shapes into selectable item content, because the control stores item `DataContext` in `SelectedItems` and visual content can surface as type names in selected tags.
- Fluent icon names come from `Assets/FluentSystemIcons-Resizable.json` and are exposed through generated `sr:Fi` enum values plus `FluentIcons.*` string constants; application-owned Core controls and styles should use `Filled` variants through `{sr:FluentIconSource {sr:Fi NameFilled}}`, `FluentIcon`, or `FluentIcons.NameFilled` instead of raw glyphs.
- Raw Fluent glyph migrations must reverse-map the code point through `Assets/FluentSystemIcons-Resizable.json` before selecting a Filled replacement. Do not modify the font, mapping JSON, or source generator for ordinary style migration; framework-owned template glyphs may remain when they use a different framework font.
- Comments should explain public-contract constraints, draw fairness reasoning, or platform quirks; avoid restating
  obvious property wiring.

## ANTI-PATTERNS

- Do not put app-window or desktop-launcher behavior in Core.
- Do not bypass draw status handling with uncaught exceptions for normal no-candidate/repeat-limit outcomes.
- Do not add page registration logic outside `Extensions/Registry/` unless changing the navigation architecture.
- Do not break `SecRandom.Shared` contract assumptions from Core models/services.
- Do not duplicate app-only localization; Core has only common/shared resources.
