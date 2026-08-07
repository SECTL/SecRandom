# SecRandom.Platforms.Abstractions/ AGENTS.md

## Scope

This project defines app-internal, platform-neutral contracts. It is intentionally outside `SecRandom.Core` and `SecRandom.Shared`, and it is not a plugin API.

## Rules

- Keep this project free of Avalonia `Window`/lifetime types, native handles beyond `PlatformWindowHandle`, Win32/X11/AppKit APIs, desktop services, and `IServiceProvider`.
- Model a requested capability explicitly through `WindowFeatureRequest` and report each feature as applied, unsupported, or failed through `WindowFeatureApplyResult`.
- New capabilities must first be expressed here, then implemented only in the matching `SecRandom.Platforms.<OS>` project. Do not claim support in a platform root before the implementation exists.
- Removable-storage discovery is represented by `IRemovableStorageCatalog` and `RemovableStorageDevice`; implementations belong in each matching desktop platform project and may use that platform's native APIs or standard system commands. The catalog must return opaque stable device IDs, display names, an optional hardware/product name for user-facing selection, a user-facing display location, and current mount roots. Mount roots remain internal to the catalog/security service and must not be projected into public security models. Mobile/unsupported roots use the explicit empty catalog.
- Removable-storage binding markers are represented by `IRemovableStorageBindingMarker`. Every desktop platform uses the one `.SecRandom.safety.key` file name; Windows adds hidden/system attributes while Linux/macOS use the dot-file convention. The marker contract must not expose mount roots or secret contents.
- Platform projects that invoke system discovery commands must use a bounded output reader/timeout and terminate timed-out child processes; do not call synchronous `ReadToEnd()` before waiting for process exit.
- `TopmostMode.UiAccess` remains a desktop process-token startup concern and must not be represented as a generic window feature.
