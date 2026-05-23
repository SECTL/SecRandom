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

**Last Update:** 2026-05-03
**Last Submit:** 175e36f0
**Last modified model:** deepseek-v4-pro

## OVERVIEW
SecRandom is a GPLv3 C#/.NET desktop app for fair random drawing in education scenarios. Stack: .NET solution, Avalonia + FluentAvalonia UI, Microsoft.Extensions.Hosting DI, xUnit v3 placeholder tests.

## STRUCTURE
```
SecRandom-C/
├── SecRandom/             # Avalonia app layer: App host, views, viewmodels, app services, localization, assets
├── SecRandom.Core/        # Core/domain + reusable UI controls/styles + config/logging/draw services
├── SecRandom.Shared/      # Cross-project contracts, base config/model types, IPC/profile models
├── SecRandom.Desktop/     # Tiny executable launcher; Program.cs only bootstraps Avalonia
├── SecRandom.Core.Tests/  # xUnit v3 test project; currently placeholder coverage
├── docs/                  # Project rules, localization, namespace boundaries
├── CHANGELOG/             # Versioned release notes, mostly v3 tree
├── resources/             # README mirrors, screenshots, banners, root static assets
├── Global.props           # Main shared MSBuild policy; imported by projects
├── Directory.Build.props  # Avalonia version pin only
└── SecRandom.sln          # Build/test solution entrypoint
```

Nested instruction files:
- `SecRandom/AGENTS.md`: app layer, DI composition, views/viewmodels, app services, localization.
- `SecRandom/Views/SettingsPages/AGENTS.md`: settings page subtree, page IDs, restart semantics, grouped localization expectations.
- `SecRandom.Core/AGENTS.md`: plugin-facing core, draw/config/logging services, shared controls/styles.
- `SecRandom.Core/Services/Camera/AGENTS.md`: camera preview loop, detector model loading, and device/restart invariants.
- `SecRandom.Shared/AGENTS.md`: UI-free shared contracts and persistence model boundaries.

## WHERE TO LOOK
| Task | Location | Notes |
|------|----------|-------|
| Run/build/test | `SecRandom.sln`, `.github/workflows/Build.yml` | Use solution commands; no Makefile/CMake. |
| Desktop startup | `SecRandom.Desktop/Program.cs` | Process entry → Avalonia lifetime. |
| App composition / DI | `SecRandom/App.axaml.cs` | `BuildHost()` is the registration source of truth. |
| Main navigation | `SecRandom/Views/MainView.axaml.cs` | Default page `main.rollCall`; keyed DI page factory. |
| Settings navigation | `SecRandom/Views/SettingsView.axaml.cs` | Default page `settings.basic`; has back stack + restart dialog. |
| Page registration helpers | `SecRandom.Core/Extensions/Registry/` | `AddMainPage`, `AddSettingsPage`, groups, separators. |
| Page registry state | `SecRandom.Core/Services/PagesRegistryService.cs` | Main/settings/group collections. |
| Fair draw logic | `SecRandom.Core/Services/Draw/` | Partial `DrawEngine`, weighted draw, filters, crypto RNG. |
| Camera draw logic | `SecRandom.Core/Services/Camera/` | `CameraDrawEngine` partial: face detection, camera discovery, camera-based draw loop. |
| Config persistence | `SecRandom.Core/Services/Config/`, `SecRandom/Services/Config/DesktopConfigService.cs` | Handler in Core, desktop JSON storage in app layer. |
| Reusable controls/styles | `SecRandom.Core/Controls/`, `SecRandom.Core/Styles/`, `SecRandom.Core/StylesBase.axaml` | App style entrypoint includes Core bundle. |
| Localization rules | `SecRandom/Langs/`, `SecRandom.Core/Langs/`, `docs/localization.md` | Per-page resource folders; `.csproj` registers base resx/designer only. |
| Shared contracts | `SecRandom.Shared/` | Keep UI/runtime dependencies out. |
| Project rules | `docs/project_rules.md` | Strongest local convention source. |

## CODE MAP
Keep this map short and stable. When code moves, AI agents should re-read the moved files and update this map in the same task.

| Symbol | Type | Location | Role |
|--------|------|----------|------|
| `Program.Main` | entry | `SecRandom.Desktop/Program.cs` | Starts Avalonia desktop lifetime. |
| `Program.BuildAvaloniaApp` | entry helper | `SecRandom.Desktop/Program.cs` | Platform detect, MiSans default font, trace logging. |
| `App` | Avalonia app | `SecRandom/App.axaml.cs`, `App.Consts.cs` | Culture, XAML load, Host/DI, windows, restart/stop, theme/font refresh. |
| `IAppHost` | static service access | `SecRandom.Core/Abstraction/IAppHost.cs` | Holds Host and exposes `GetService<T>()` / `TryGetService<T>()`. |
| `MainView` | shell view | `SecRandom/Views/MainView.axaml.cs` | Main NavigationView, drawer, default page, settings window bridge. |
| `SettingsView` | shell view | `SecRandom/Views/SettingsView.axaml.cs` | Settings NavigationView, history/back, restart prompt. |
| `PagesRegistryService` | registry | `SecRandom.Core/Services/PagesRegistryService.cs` | Static collections backing generated navigation menus. |
| `DrawEngine` | domain service | `SecRandom.Core/Services/Draw/DrawEngine*.cs` | Student/prize drawing, fairness weights, repeat/avg-gap filtering. |
| `WeightedDrawEngine<T>` | algorithm | `SecRandom.Core/Services/Draw/WeightedDrawEngine.cs` | Validates weights and samples without replacement. |
| `CameraDrawEngine` | domain service | `SecRandom.Core/Services/Camera/CameraDrawEngine*.cs` | Camera-based face detection, device discovery, and draw loop. |
| `MainConfigHandler` | config handler | `SecRandom.Core/Services/Config/MainConfigHandler.cs` | Main config wrapper over `ConfigHandlerBase<MainConfigModel>`. |
| `ProfileService` | app service | `SecRandom/Services/ProfileService.cs` | Current profile runtime state and persistence. |
| `IProfileService` | service contract | `SecRandom.Core/Abstraction/Services/IProfileService.cs` | Current lists/history + profile save boundary. |
| `SettingsSearchService` | app service | `SecRandom/Services/SettingsSearchService.cs` | Indexes settings pages via reflected localization resources. |
| `AttachedSettingsRegistryService` | registry | `SecRandom.Core/Services/AttachedSettingsRegistryService.cs` | Static collections for attached-settings controls. |
| `ViewModelBase` | base VM | `SecRandom/ViewModels/ViewModelBase.cs` | Base VM exposing `MainConfig`; inherits `ObservableRecipient`. |
| `GlobalConstants` | constants | `SecRandom.Core/GlobalConstants.cs` | Version, platform, and development-mode constants. |

## CONVENTIONS
- `docs/project_rules.md` overrides inference when adding features.
- ViewModels must be registered in `SecRandom/App.axaml.cs` `BuildHost()`; reusable services also go through Host.
- Resolve shared services via `IAppHost.GetService<T>()` / `TryGetService<T>()` unless constructor injection is already the local style.
- Navigation pages need `[PageInfo(...)]` plus `services.AddMainPage<T>()` or `services.AddSettingsPage<T>()` in `BuildHost()`.
- Page IDs: `main.xxx`, `settings.xxx`, `settings.group.xxx`.
- File/data paths should go through `Utils.GetFilePath(...)`; data lands under `AppContext.BaseDirectory/data/...`.
- Localization is per page folder: `Resources.resx`, `Resources.Designer.cs`, and optional culture files such as `Resources.en-US.resx` / `Resources.ja-JP.resx`; preserve exact filename casing used on disk.
- In `.csproj`, register only `Resources.resx` and `Resources.Designer.cs`; do not register every language variant.
- Resource designer generator must be `PublicResXFileCodeGenerator`.
- Localization keys: `S_` settings, `S_xxx_D` description, `S_xxx_R` real key, `O_` options, `M_` messages, `C_` controls.
- Views commonly set `DataContext = this` and expose `ViewModel`; bindings use `ViewModel.*`.
- ViewModels use CommunityToolkit MVVM (`ObservableRecipient`, `[ObservableProperty]`); app VMs inherit `SecRandom.ViewModels.ViewModelBase`.
- `Global.props` carries the main MSBuild behavior: unsafe enabled, Windows targeting enabled, SourceLink, full debug symbols, shared `AssemblyInfo.cs`.
- `Directory.Build.props` only pins `AvaloniaVersion`.

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
- Do not merge page localization into a shared resource bucket.
- Do not assume dictionary mutations auto-save config; call save after collection mutations or on unload.
- Do not treat `SecRandom.Desktop` as the app logic layer; it is only the launcher.
- Do not edit `bin/`, `obj/`, `artifacts/`, `publish/`, packaging scratch dirs, or generated build output.
- Do not encode temporary local environment facts in AGENTS files; prefer durable rules that future AI agents can maintain.

## UNIQUE STYLES
- Avalonia compiled bindings are enabled in `SecRandom/SecRandom.csproj`.
- `SecRandom/Styles.axaml` is only the app style entrypoint; shared styles live under `SecRandom.Core/StylesBase.axaml` and `SecRandom.Core/Styles/`.
- Shared UI controls are in Core, not only app: `DrawerHost`, `Field`, `IconText`, `AppToastAdorner`, `AttachedSettingsControlPresenter`, `FluentIcon`, `FluentIconSource`, `StickyScrollViewer`, `TouchDragThumb`, `Empty`, `Emptiable`, `EmptySplashScreen`, `DevelopmentBuildAdorner`.
- UI uses FluentAvalonia `NavigationView`, `SettingsExpander`, `FluentIconSource`, and custom selector classes (`compact`, `nav-back`, `drawer-left/right`, `FullWidth`).
- Default font is MiSans from Avalonia resource URI `avares://SecRandom/Assets/Fonts/MiSans/#MiSans`.
- README/docs are Chinese-first with English/ZH-TW mirrors under `resources/`.

## COMMANDS
```bash
dotnet restore SecRandom.sln
dotnet build SecRandom.sln -c Release --no-restore
dotnet test SecRandom.sln -c Release --no-build
dotnet run --project SecRandom.Desktop/SecRandom.Desktop.csproj
dotnet publish SecRandom.Desktop/SecRandom.Desktop.csproj -c Release -r <rid> --self-contained true -o artifacts/SecRandom-<rid> /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
```

CI RIDs: `win-x64`, `win-x86`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`.

## NOTES
- Build workflow uses .NET SDK `10.0.100`; publish job currently uses `9.0.311`; CodeQL uses `10.0.x`.
- Release workflow triggers on tags `v*`, manual dispatch, PR/push build, or commit message containing `开始构建`.
- Test project exists but `UnitTest1.cs` is empty placeholder coverage.
- README mentions `vendors/pythonnet-stub-generator/`; treat it as third-party if present in future snapshots.
- CodeQL workflow (`codeQL.yml`) runs on push, PR, and weekly schedule; C# scans use manual build mode with .NET SDK `10.0.x`.
- `SecRandom.Core` is plugin-facing per `docs/namespaces.md`; keep its public contracts stable.
