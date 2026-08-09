# SecRandom.Shared/ AGENTS.md

<!--
Shared-contract supplement to ../AGENTS.md. Update this file when persisted model shapes,
contract interfaces, or path helper semantics change. AI agents touching those areas must
update this file in the same task.
-->

## OVERVIEW

Cross-project contract layer for shared config bases, profile/list/history models, attached-settings interfaces, and
utility extensions.

## STRUCTURE

```
SecRandom.Shared/
├── Abstraction/       # ConfigBase + ProfileConfigBase path/name contracts
├── ComponentModels/   # ObservableDictionary helper type
├── Extensions/        # Dependency-light helpers for shared interfaces
├── Interfaces/        # Attached settings contracts
├── Models/Profile/    # Student/prize list/history data models
├── Models/            # AttachableSettingsObject base model
├── Updates/           # UI-free signed release manifest and package-marker DTOs
├── Utils.cs           # Shared file path helper used by config/data paths
└── SecRandom.Shared.csproj  # net8.0, nullable, CommunityToolkit.Mvvm
```

## WHERE TO LOOK
| Task | Location | Notes |
|------|----------|-------|
| Run/build/test | `SecRandom.sln`, `.github/workflows/Build.yml` | Use solution commands; no Makefile/CMake. |
| Desktop startup | `SecRandom.Desktop/Program.cs` | Process entry → Avalonia lifetime. |
| Platform capability contracts | `SecRandom.Platforms.Abstractions/`, `SecRandom.Platforms/` | App-internal platform root, window feature requests/results, startup context, and DI bridge. |
| Native window features | `SecRandom.Platforms.Windows/`, `SecRandom.Platforms.Linux/`, `SecRandom.Platforms.MacOs/` | Each platform owns native feature handling; views must not add platform API calls. |
| Mobile startup | `SecRandom/App.axaml.cs`, `SecRandom/Mobile/`, `SecRandom/Views/Mobile/`, `SecRandom.Android/`, `SecRandom.iOS/` | The shared `App` owns the one Host and branches by Avalonia lifetime; `SecRandom` owns the mobile root, routes, services, and platform-neutral seams while heads own entry points. |
| Mobile UI tests | `SecRandom.Mobile.Tests/` | Avalonia Headless smoke tests load mobile styles and lay out native shell controls at phone dimensions. |
| Mobile point-call orchestration | `SecRandom/Services/Mobile/MobileRollCallService.cs` | Mobile-only list/scope/count orchestration over existing Core filtering, sampling, and transactional commit services. |
| App composition / DI | `SecRandom/App.axaml.cs` | `BuildHost()` is the registration source of truth. |
| Main navigation | `SecRandom/Views/MainView.axaml.cs` | Default page `main.rollCall`; keyed DI page factory. Built-in draw pages are `main.rollCall` and `main.lottery`; quick draw opens from the floating window instead of the main sidebar. |
| Settings navigation | `SecRandom/Views/SettingsView.axaml.cs` | Default page `settings.overview`; has back stack + restart dialog. General group now includes `settings.general.basic`, `settings.general.privacy`, and `settings.general.backup`. |
| Page registration helpers | `SecRandom.Core/Extensions/Registry/` | `AddMainPage`, `AddSettingsPage`, group, and separator helpers. |
| ClassIsland notifications | `SecRandom/Services/Notification/`, `SecRandom4Ci.Interface/` | Typed v2 IPC client for the installed SecRandom4Ci ClassIsland plugin. |
| Crash recovery | `SecRandom/Services/CrashRecovery/`, `SecRandom/Views/CrashRecoveryWindow.axaml.cs` | Fatal/dispatcher crash report prompt, guarded auto-restart, and shared desktop relaunch logic. |
| Page registry state | `SecRandom.Core/Services/PagesRegistryService.cs` | Main/settings/group collections. |
| Cross-platform view engine | `SecRandom.Core/Views/` | Logical view/session contracts; desktop and mobile shells provide DI-registered physical hosts. |
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
| `SettingsSearchService` | app service | `SecRandom/Services/Settings/SettingsSearchService.cs` | Indexes current settings controls via reflected localization resources; searchable visual targets use stable `x:Name` IDs and nested targets may be found through the visual tree. |
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
| `MobileRollCallService` | mobile service | `SecRandom/Services/Mobile/MobileRollCallService.cs` | Mobile list/scope/count snapshots, multi-member draws, remaining list, and scoped temporary reset without changing the Core session contract. |
| `MobileMediaLibraryService` / `MobileDrawMediaService` | mobile services | `SecRandom/Services/Mobile/` | Mobile-private media import/reference cleanup and draw-time per-record image/music/voice orchestration through head-injected native playback. |

## CONVENTIONS

- Keep this project UI-free and Avalonia-free; it targets `net8.0` while app/Core target `net10.0`.
- Shared models are data contracts used across projects; avoid Host, logging, windows, or app service dependencies.
- Profile models may be observable/serializable contract types; keep property defaults safe for missing JSON.
- `Student` and `Prize` include hidden persisted `RecordId` values used as stable history/fairness identities. Keep visible `Id` optional; it is display/import metadata, not a required identity. Their `IsCandidate` checks require the item to be enabled and to have a nonblank `Id` or `Name`.
- `ProfileRecordIdentity` is the boundary helper for filling missing/duplicate `RecordId` values and resolving legacy `Id`/`Name` history keys without ambiguous fallback.
- `Student` and `Prize` include persisted optional metadata fields such as `Tags`; keep new fields backward-compatible with empty defaults.
- `ProfileListOrderingExtensions` is the shared presentation ordering for students and prizes: numeric IDs sort first, followed by other IDs, then records without IDs by `CurrentCulture` name order. Use it in list-management and remaining-list UI so every language and draw surface stays consistent.
- IPC DTOs under `Models/Ipc/` are serialization-only contracts. Keep them free of UI/runtime services and do not emit internal `RecordId` values in external projections.
- Update DTOs under `Updates/` remain serialization-only. Manifest signature verification, network access, package extraction, and installer process execution belong in the app layer.
- Mobile Android releases use the same signed manifest with `android-apk` artifacts. The manifest contract remains distribution-neutral; downloading, package installation, and iOS distribution handling stay in the corresponding app layer.
- Draw proofs serialize their product algorithm release as `algorithmEngineVersion`; retain the nullable legacy `kernelVersion` reader only for historical proof files. User-visible labels must call it an algorithm engine version, not a kernel version.
- New proof files are locally `OfflineReproducible`; `DrawProofWitness` receipt fields are nullable so the same contract can carry a later server replay attestation without writing empty historical challenge fields. The receipt is excluded from the canonical proof hash, while challenge/key fields remain historical compatibility data.
- `HistoryItem.DrawRoundId` is an additive persisted field. New multi-record draws share one value so IPC history can group a logical draw; empty legacy values require conservative fallback grouping.
- `HistoryItem.CourseName` is an additive persisted course-history field. Empty values represent legacy/global history; new linkage writes use it only for a resolved subject or the stable `__break__` marker while record identity remains `RecordId`.
- Attached settings objects use `Guid` keys and `Dictionary<Guid, object?>`; coordinate changes with Core draw/settings
  consumers.
- Prefer small extension methods and plain contracts here; richer behavior belongs in `SecRandom.Core`.
- `ConfigBase` / `ProfileConfigBase` define paths and identity for persisted files; handlers and desktop storage live
  outside Shared.
- `Utils.GetFilePath(...)` is the expected route for data/config paths. Its default desktop/portable root remains `<PackageRoot>/data`. `Utils.ConfigureMobileDataRoot()` is a startup-only Android/iOS operation with a fixed `LocalApplicationData/SecRandom/data` target; shared `SecRandom.App` calls it exactly once in its mobile branch before any path is read. No Shared model, Core service, plugin, or desktop flow may change the root. Accessing `data/config` also applies filesystem hiding: Windows uses `Hidden|System`, while Unix-like hosts maintain the parent `.hidden` entry because the stable `config` name cannot be changed.
- If adding a shared model that will be persisted, consider backward-compatible defaults and nullable behavior first.
- Comments should document serialization/backward-compatibility constraints, not obvious property names.

## ANTI-PATTERNS

- Do not reference `SecRandom` or `SecRandom.Core` from Shared.
- Do not add Avalonia/FluentAvalonia dependencies here.
- Do not hide persistence side effects inside Shared models; persistence belongs in Core/app config services.
- Do not change shared contract shapes casually; Core draw/profile/config code may deserialize persisted data into them.
- Do not make visible student/prize `Id` mandatory again; lists must support records with empty IDs.
- Do not add platform-specific path logic outside `Utils` / config abstractions.
