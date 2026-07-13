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
├── Utils.cs           # Shared file path helper used by config/data paths
└── SecRandom.Shared.csproj  # net8.0, nullable, CommunityToolkit.Mvvm
```

## WHERE TO LOOK

| Task                         | Location                                                                     | Notes                                                                                  |
|------------------------------|------------------------------------------------------------------------------|----------------------------------------------------------------------------------------|
| Config base path/model       | `Abstraction/`                                                               | `ConfigBase` and `ProfileConfigBase` used by Core handlers.                            |
| Profile data contracts       | `Models/Profile/`                                                            | Student/prize lists, hidden stable item identity, and histories.                       |
| Attached settings contracts  | `Interfaces/IAttachedSettings.cs`, `Interfaces/IAttachableSettingsObject.cs` | Used by Core draw/attached-settings logic.                                             |
| Serialization helpers        | `Extensions/`                                                                | Shared extension methods; keep dependency-light.                                       |
| IPC/shared model boundary    | `Models/Ipc/`                                                                | Structured URL-request and response DTOs shared by Core transport and app routing.     |
| File/data path helper        | `Utils.cs`                                                                   | Central path helper used by config conventions.                                        |
| Observable collection helper | `ComponentModels/ObservableDictionary.cs`                                    | Use carefully with persistence; dictionary mutation may not trigger outer config save. |

## CONVENTIONS

- Keep this project UI-free and Avalonia-free; it targets `net8.0` while app/Core target `net10.0`.
- Shared models are data contracts used across projects; avoid Host, logging, windows, or app service dependencies.
- Profile models may be observable/serializable contract types; keep property defaults safe for missing JSON.
- `Student` and `Prize` include hidden persisted `RecordId` values used as stable history/fairness identities. Keep visible `Id` optional; it is display/import metadata, not a required identity.
- `ProfileRecordIdentity` is the boundary helper for filling missing/duplicate `RecordId` values and resolving legacy `Id`/`Name` history keys without ambiguous fallback.
- `Student` and `Prize` include persisted optional metadata fields such as `Tags`; keep new fields backward-compatible with empty defaults.
- IPC DTOs under `Models/Ipc/` are serialization-only contracts. Keep them free of UI/runtime services and do not emit internal `RecordId` values in external projections.
- `HistoryItem.DrawRoundId` is an additive persisted field. New multi-record draws share one value so IPC history can group a logical draw; empty legacy values require conservative fallback grouping.
- Attached settings objects use `Guid` keys and `Dictionary<Guid, object?>`; coordinate changes with Core draw/settings
  consumers.
- Prefer small extension methods and plain contracts here; richer behavior belongs in `SecRandom.Core`.
- `ConfigBase` / `ProfileConfigBase` define paths and identity for persisted files; handlers and desktop storage live
  outside Shared.
- `Utils.GetFilePath(...)` is the expected route for data/config paths; root docs rely on this rule.
- If adding a shared model that will be persisted, consider backward-compatible defaults and nullable behavior first.
- Comments should document serialization/backward-compatibility constraints, not obvious property names.

## ANTI-PATTERNS

- Do not reference `SecRandom` or `SecRandom.Core` from Shared.
- Do not add Avalonia/FluentAvalonia dependencies here.
- Do not hide persistence side effects inside Shared models; persistence belongs in Core/app config services.
- Do not change shared contract shapes casually; Core draw/profile/config code may deserialize persisted data into them.
- Do not make visible student/prize `Id` mandatory again; lists must support records with empty IDs.
- Do not add platform-specific path logic outside `Utils` / config abstractions.
