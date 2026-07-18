# SecRandom.Platforms.Abstractions/ AGENTS.md

## Scope

This project defines app-internal, platform-neutral contracts. It is intentionally outside `SecRandom.Core` and `SecRandom.Shared`, and it is not a plugin API.

## Rules

- Keep this project free of Avalonia `Window`/lifetime types, native handles beyond `PlatformWindowHandle`, Win32/X11/AppKit APIs, desktop services, and `IServiceProvider`.
- Model a requested capability explicitly through `WindowFeatureRequest` and report each feature as applied, unsupported, or failed through `WindowFeatureApplyResult`.
- New capabilities must first be expressed here, then implemented only in the matching `SecRandom.Platforms.<OS>` project. Do not claim support in a platform root before the implementation exists.
- `TopmostMode.UiAccess` remains a desktop process-token startup concern and must not be represented as a generic window feature.
