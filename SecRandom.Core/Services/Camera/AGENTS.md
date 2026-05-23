# SecRandom.Core/Services/Camera/ AGENTS.md

<!--
Camera-engine supplement to ../../AGENTS.md. Update this file when camera preview flow,
detector backends, model-path lookup, or config-triggered restart behavior changes.
-->

## OVERVIEW

Camera preview and face-detection subtree: device discovery, preview loop, detector lifecycle, frame emission, and config-driven restart/reload behavior.

## WHERE TO LOOK

| Task | Location | Notes |
|------|----------|-------|
| Engine state and config hooks | `CameraDrawEngine.Base.cs` | Subscribes to `FaceDetectorSettingsConfig` and resolution-map changes. |
| Preview loop / camera lifecycle | `CameraDrawEngine.Core.cs` | `StartPreviewAsync`, `StopPreviewAsync`, restart path, frame pump, cleanup. |
| Detector lifecycle | `CameraDrawEngine.Detector.cs` | Backend selection, model-path resolution, ONNX/OpenCV detector loading. |
| Config-change reactions | `CameraDrawEngine.EventsHandler.cs` | Flips restart/reload flags from config mutations. |
| Device enumeration helpers | `CameraDrawEngine.Helpers.cs` | FlashCap descriptor enumeration and resolution parsing. |
| Runtime messages | `CameraDrawEngine.Resources.resx` | User-facing detector/camera text resolved via `CameraText(...)`. |
| Shared payload types | `../../Models/Camera/` | `CameraFramePacket`, `CameraDevice`, detection-session types. |

## CONVENTIONS

- `CameraDrawEngine` stays split by responsibility: base state, core loop, detector, event handling, helper utilities.
- Config comes from `MainConfigHandler.Data.FaceDetectorSettings`; do not cache a second source of truth outside `ConfigData`.
- Restart and reload are cooperative flags (`RequireCameraRestart`, `RequireDetectorReload`) consumed by the worker loop; do not mutate hardware state directly from config callbacks.
- Device lookup is currently by descriptor identity string stored in `CameraSource`; preserve that contract when changing selection logic.
- Detector model files are expected under `data/cv_models/` via `Utils.GetFilePath(...)`, with upward fallback search from `AppContext.BaseDirectory`.
- Model validation must keep the Git LFS pointer guard; tiny placeholder files are treated as invalid detector payloads.
- Public preview flow favors logging and `DetectionState`/frame events over crashing the app for transient camera/detector failures.

## ANTI-PATTERNS

- Do not move camera preview UI behavior into this subtree; this directory is engine/runtime only.
- Do not bypass `CleanupCameraResources()` when restarting or stopping preview.
- Do not add hardcoded absolute model paths or platform-specific camera paths; keep path logic behind `Utils.GetFilePath(...)` and descriptor helpers.
- Do not swallow detector/backend changes in random files; backend selection belongs in `CameraDrawEngine.Detector.cs`.
- Do not remove the config subscriptions in `CameraDrawEngine.Base.cs`; live settings changes depend on them.
