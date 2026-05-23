# SecRandom.Core/ AGENTS.md

<!--
Core supplement to ../AGENTS.md. Update this file when plugin-facing contracts,
draw/config/logging services, shared controls/styles, or registry helpers move.
AI agents touching those areas must update this file in the same task.
-->

## OVERVIEW

Core module for domain logic, plugin-facing contracts, config/logging services, shared Avalonia controls/styles,
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
├── Styles/               # Modular shared style files
├── StylesBase.axaml      # Shared style hub imported by app
├── Services/Draw/        # Fair/random draw engine and filters
├── Services/Camera/      # Camera-based face detection and draw loop
├── Services/Config/      # Config handlers over Shared config models
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
| Profile contract           | `Abstraction/Services/IProfileService.cs`                                        | Current profile/list/history boundary.                                  |
| Page metadata              | `Attributes/PageInfoAttribute.cs`, `Models/PageInfo.cs`                          | Used by registration extensions.                                        |
| Page registration          | `Extensions/Registry/`                                                           | `AddMainPage`, `AddSettingsPage`, group/separator helpers.              |
| Navigation registry        | `Services/PagesRegistryService.cs`                                               | Static main/settings/group collections.                                 |
| Attached-settings registry | `Services/AttachedSettingsRegistryService.cs`                                    | Static attached-settings control collections.                           |
| Draw algorithm             | `Services/Draw/DrawEngine*.cs`, `WeightedDrawEngine.cs`, `CryptoRandomSource.cs` | Fairness, filters, weighted sampling.                                   |
| Camera draw engine         | `Services/Camera/CameraDrawEngine*.cs`                                           | Face detection, camera discovery, draw loop, config-change reactions.   |
| Config handlers            | `Services/Config/`                                                               | `MainConfigHandler` and `ProfileConfigs` wrap config model persistence. |
| Config schema              | `Enums/Configs/`, `Models/SubConfigs/`                                           | Many settings model types live here.                                    |
| Shared controls            | `Controls/*.axaml(.cs)`                                                          | Reusable app controls; keep templates and code-behind paired.           |
| Shared styles              | `StylesBase.axaml`, `Styles/*.axaml`                                             | Imported by `SecRandom/Styles.axaml`.                                   |
| Constants/helpers          | `GlobalConstants.cs`, `Helpers/`                                                 | Keep cross-cutting values here only when Core consumers need them.      |

## CONVENTIONS

- Core is plugin-facing per `docs/namespaces.md`; avoid app-only dependencies and unstable public contracts.
- `IAppHost.GetService<T>()` is used in Core services like `DrawEngine`; Host is built by app layer.
- Registration helpers are responsible for both keyed DI and `PagesRegistryService` metadata.
- `DrawEngine` is partial: keep filtering in `DrawEngine.Filter.cs`, weight math in `DrawEngine.WeightCalculator.cs`,
  orchestration in `DrawEngine.cs`.
- `CameraDrawEngine` is partial: keep core state in `CameraDrawEngine.Base.cs`, loop/init in `CameraDrawEngine.Core.cs`,
  face detection in `CameraDrawEngine.Detector.cs`, config-change reactions in `CameraDrawEngine.EventsHandler.cs`, and
  device discovery/utilities in `CameraDrawEngine.Helpers.cs`.
- Weighted drawing validates count, candidates, and weights before sampling; preserve explicit `DrawStatus` returns over
  exceptions at public boundary.
- Config handlers derive from `ConfigHandlerBase<TModel>`; config model defaults should be safe without existing data
  files.
- Controls should keep `.axaml` and `.axaml.cs` side by side and expose reusable Avalonia properties/templates.
- Shared styles are modular; add new broad styles under `Styles/` and include from `StylesBase.axaml`.
- Fluent icon names come from `Assets/FluentSystemIcons-Resizable.json` and are exposed through generated `sr:Fi` enum values plus `FluentIcons.*` string constants; prefer `{sr:FluentIconSource {sr:Fi NameRegular}}` in XAML and `FluentIcons.NameRegular` in attributes/code instead of raw glyphs.
- Comments should explain public-contract constraints, draw fairness reasoning, or platform quirks; avoid restating
  obvious property wiring.

## ANTI-PATTERNS

- Do not put app-window or desktop-launcher behavior in Core.
- Do not bypass draw status handling with uncaught exceptions for normal no-candidate/repeat-limit outcomes.
- Do not add page registration logic outside `Extensions/Registry/` unless changing the navigation architecture.
- Do not break `SecRandom.Shared` contract assumptions from Core models/services.
- Do not duplicate app-only localization; Core has only common/shared resources.
