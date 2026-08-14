# SecRandom/Views/SettingsPages/ AGENTS.md

<!--
Settings-page supplement to ../../AGENTS.md. Update this file when settings page folders,
page IDs, restart behavior, or settings-page localization layout changes.
-->

## OVERVIEW

Settings navigation subtree: top-level settings pages, grouped settings pages, and page-local UI behavior that hangs off `SettingsView`.

## STRUCTURE

```
SecRandom/Views/SettingsPages/
|-- HomeSettingsPage.axaml(.cs)           # Top-level student-history overview
|-- DebugSettingsPage.axaml(.cs)          # Runtime-hidden bottom debug page
|-- General/                              # settings.general.*: basic/security/backup/privacy
|-- ListManagement/                       # settings.listManagement.*: roll-call/lottery list entries
|-- Personalized/                         # settings.personalized.*: appearance/floatingWindow/music library
|-- Picking/                              # settings.picking.*: draw settings + face detector
|-- Notification/                         # settings.notification.*: voice and per-draw notification channels
|-- History/                              # settings.history.*: management + roll-call/lottery history pages
|-- About/                                # settings.about: about page with external links
|-- Linkage/                              # settings.linkage: linkage settings
|-- More/                                 # settings.more: more settings
|-- Plugins/                              # settings.plugin: local SRPX plugin management
|-- Update/                               # settings.update: shared update settings
|-- LogViewer/                            # settings.logs: hidden log viewer
```

## WHERE TO LOOK

| Task | Location | Notes |
|------|----------|-------|
| Register settings page | `SecRandom/App.axaml.cs` | `BuildHost()` plus `[PageInfo(...)]` are both required. |
| Top-level settings overview | `HomeSettingsPage.axaml(.cs)` | Page ID `settings.overview`; no group; separately summarizes all roll-call lists and lottery pools. |
| General settings behavior | `General/BasicSettingsPage.axaml(.cs)` | Language change triggers `SettingsView.Current?.RequestRestartApp()`. |
| Privacy settings behavior | `General/PrivacySettingsPage.axaml(.cs)` | Binds to `MainConfigModel.General.PrivacySettings`; Sentry telemetry changes apply live through `TelemetryRuntimeService`, and online status changes apply live through `OnlineStatusService`. |
| Backup settings UI | `General/BackupSettingsPage.axaml(.cs)` | Lists real backup ZIPs under app data and delegates create/restore to `IImportExportService`, which validates archives and creates the required pre-restore snapshot. |
| Security settings | `General/SecuritySettingsPage.axaml(.cs)` | Page ID `settings.general.security`; belongs to the General navigation group. |
| Managed draw music | `SecRandom/Services/Music/`, `SecRandom/Services/Draw/DrawAudioService.cs` | Library files live under `data/audio/music`; app-layer service resolves selections and owns cross-platform playback. |
| Course linkage | `SecRandom/Services/Linkage/` | CSES schedule storage/parsing, ClassIsland IPC adapter, course-boundary runtime, draw authorization, and pre-class reset. |
| List management settings | `ListManagement/RollCallListSettingsPage.axaml(.cs)`, `ListManagement/LotteryListSettingsPage.axaml(.cs)` | Point-call list and lottery prize-pool viewing/import. |
| Draw settings | `Picking/DefaultDrawSettingsPage.axaml(.cs)` etc. | Default, roll-call, quick-draw, and lottery draw settings are registered; visible effects must reflect on built-in draw pages. |
| Personalized appearance settings | `Personalized/AppearanceSettingsPage.axaml(.cs)` | Mutations call `App.Current.RefreshPersonalizedSettings()`. |
| Personalized music library | `Personalized/MusicSettingsPage.axaml(.cs)` | Page ID `settings.personalized.music`; imports, deletes, and previews managed MP3/WAV/FLAC tracks. |
| Linkage settings | `Linkage/LinkageSettingsPage.axaml(.cs)` | Top-level `settings.linkage` entry. |
| More settings | `More/MoreSettingsPage.axaml(.cs)` | `settings.more` top-level entry. |
| Plugins | `Plugins/PluginsSettingsPage.axaml(.cs)` | Desktop-only `settings.plugin` page; left list splits into 已安装/市场, stages local `.srpx` packages, shows load status, toggles plugin enablement, and uninstalls selected plugins for the next restart (config directory preserved). Plugin README renders through Core `MarkdownConvertHelper` + `Styles/RichTextStyles.axaml`. |
| Update settings | `Update/UpdateSettingsPage.axaml(.cs)` | Shared `settings.update` bottom-nav entry for desktop and mobile. |
| Notification settings | `Notification/VoiceSettingsPage.axaml(.cs)` etc. | Voice/music and notification channel entries under `settings.notification`. |
| History management | `History/HistoryManagementSettingsPage.axaml(.cs)` | Clears roll-call/lottery histories through active-profile or named-profile handlers; `settings.history.management`. |
| Log viewer | `LogViewer/LogViewerSettingsPage.axaml(.cs)` | Hidden page `settings.logs`; opened from the settings shell more-options menu. |
| About / external links | `About/AboutSettingsPage.axaml(.cs)` | `settings.about` bottom-nav; `Process.Start` for external URLs. |
| Shell navigation semantics | `../SettingsView.axaml.cs` | Default page `settings.overview`, history stack, generated menu. |
| Localization pairing | `../../Langs/SettingsPages/` | Page folders mirror settings-page domains, including the runtime-hidden Debug page. |

## CONVENTIONS

- Every non-debug settings page needs `[PageInfo]`, Host registration, and a matching localization folder under `SecRandom/Langs/SettingsPages/` when user-facing text is localized.
- Every new or changed settings-page user-facing string must be translated in the matching `Resources.resx`, `Resources.en-US.resx`, and `Resources.ja-JP.resx` files. Simplified Chinese, English, and Japanese are all required; no language may rely on a fallback key.
- Privacy page localization lives under `General/Privacy/` and is registered like other settings pages with only `Resources.resx` + `Resources.Designer.cs` in the project file.
- Chinese settings-page i18n values must not use the Chinese full stop (`。`).
- Settings-page explanation values (`*_D`, including `S_*_D` and `C_*_D`) must not use sentence-ending or sentence-separating full stops (`。` or `.`); preserve technical dots in file names, domains, process names, and version identifiers.
- Page IDs here follow `settings.xxx` or `settings.group.xxx`; historical grouping notes live in `docs/settings-pages-plan.md`.
- `settings.plugin` uses `IPluginManager` and the app-layer `PluginMarketService` for the 已安装/市场 tabs. The market tab loads the signed index from the fixed `generated` release in `SECTL/SecRandom-PluginIndex`, verifies the Ed25519 signature and each package SHA-256, resolves dependencies in topological order, then stages through `IPluginManager.StagePackage`; installs request a restart. Do not restore the older unsigned plugin-market/catalog abstractions.
- Group membership is owned by the `groupId` in `[PageInfo(...)]` and by `services.AddGroup(...)` in `BuildHost()`; do not handwire grouping in the page.
- Pages usually resolve `ViewModelBase` via `IAppHost.GetService<ViewModelBase>()`, set `DataContext = this`, and expose `Settings` from `ViewModel.Config.*`.
- Basic-settings platform switches must route through `DesktopIntegrationService`; do not manipulate registry keys, XDG desktop files, or macOS launch services from the settings page. Revert a switch when the platform operation fails.
- Security settings must display credential state before factor selection. Password, TOTP, and USB setup are command-driven; selected factors use the shared `MultiComboBox` pattern with plain option data, and protected-operation controls are driven by `ISecurityService` state.
- Settings pages using `MultiComboBox` must subscribe to the backing settings model on construction and every `Loaded` event, unsubscribe on `Unloaded`, and save at the selection mutation boundary. `MultiComboBox` mutates its bound `SelectedItems` collection directly, so persist multi-select changes from that collection's `CollectionChanged` event rather than `SelectionChanged`. Follow `SecuritySettingsPage` for lifecycle and use plain option-data binding.
- Searchable setting rows must use a stable `x:Name` matching their active `S_` localization ID; nested rows may use the visual-tree fallback, while stale localization entries should not be exposed as searchable targets.
- V2-parity settings pages should keep the same `ScrollViewer` + `StackPanel.page-container animated-intro` + `FASettingsExpander` rhythm as existing settings pages.
- A page containing multiple settings categories should use `IconText` category headings followed by sibling `FASettingsExpander` rows. Override groups follow the committed draw-settings pattern: one outer `FASettingsExpander` whose direct rows are `FASettingsExpanderItem` controls; never nest another `FASettingsExpander` inside it.
- Default notification settings follow the default draw-settings layout: category `IconText` headings with sibling non-grouping `FASettingsExpander` rows. Only per-draw notification pages use collapsible override groups.
- Linkage settings owns only the CSES picker, import/summary/clear commands, and config bindings. It must delegate schedule validation and file replacement to `ICsesScheduleStore`; ClassIsland state, timing, and draw enforcement belong to the linkage services.
- Voice/music owns the global TTS engine, voice, volume, and content switches. Per-student/per-prize specific announcement controls belong in list management attached settings for both roll-call and lottery records.
- The voice page registers OmniTTS (engine 2) on every non-iOS platform (`!OperatingSystem.IsIOS()` stays). Its OmniTTS-only rows (provider, API base URL, API key, model, voice, instructions, MiMo design description/clone reference, batch cache, clear cache) live in one section bound to `IsOmniTtsSelected` and appear only while `VoiceEngine == 2`; model and voice rows are editable ComboBoxes so the user can always type values, with a fetch button pulling the model list from the provider API. MiMo clone imports require explicit authorization and WAV input; the reference is private under `data/config/voice/` and is never serialized into settings. API keys never enter the settings model — they go through `OmniTtsCredentialStore` (`data/config/voice/omnitts-keys.json`).
- Draw music configuration remains on the four picking settings pages through `Picking/DrawMusicSettingsContent`; use its managed-library dropdowns and slider controls rather than editable paths. Each page must resubscribe its settings persistence on `Loaded` after detaching on `Unloaded`.
- Draw settings pages must subscribe to their settings object before constructor-time normalization so repaired values flow through the existing `PropertyChanged` save boundary.
- If a settings change needs a restart, request it through `SettingsView.Current?.RequestRestartApp()` instead of restarting directly. Selecting UIAccess topmost in basic or floating-window settings persists the mode and uses this restart flow so the desktop launcher can run the `killtimer0/uiaccess`-style token preparation before UI initialization.
- If a settings change only needs live UI refresh, follow `AppearanceSettingsPage` and route through `App.Current.RefreshPersonalizedSettings()`.
- `settings.logs` should stay hidden from the sidebar and reachable from the settings shell more-options menu; keep that menu action as a navigation jump.
- Backup pages and settings-shell import actions must delegate archive/file-system work to `IImportExportService`; do not bypass manifest validation, v2 migration, staging, or mandatory pre-import snapshots.

## ANTI-PATTERNS

- Do not add a settings page file here without registering it in `BuildHost()`.
- Do not invent a new page-ID shape that breaks `settings.xxx` / `settings.group.xxx`.
- `DebugSettingsPage` is available in all builds for the About-page activation path; keep it hidden by default in release builds and localize all user-facing text under `Langs/SettingsPages/Debug/`.
- Do not put backup/config persistence logic in these pages when the boundary belongs in Core handlers or app services.
- Do not open navigation targets by manually editing menu items in `SettingsView`; register pages/groups and let the registry build the menu.
