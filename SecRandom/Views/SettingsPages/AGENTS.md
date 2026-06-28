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
|-- HomeSettingsPage.axaml(.cs)           # Top-level landing page
|-- PluginSettingsPage.axaml(.cs)         # Top-level plugin placeholder, settings.plugin
|-- LogViewerSettingsPage.axaml(.cs)      # Hidden log viewer, settings.logs
|-- AboutSettingsPage.axaml(.cs)          # Bottom-nav about page; opens external links
|-- DebugSettingsPage.axaml(.cs)          # DEBUG-only bottom page
|-- General/                              # settings.general.*: basic/security/backup/privacy
|-- ListManagement/                       # settings.listManagement.*: roll-call/lottery list entries
|-- Personalized/                         # settings.personalized.*: appearance
`-- Picking/                              # settings.picking.* draw setting pages, including face-detector
```

## WHERE TO LOOK

| Task | Location | Notes |
|------|----------|-------|
| Register settings page | `SecRandom/App.axaml.cs` | `BuildHost()` plus `[PageInfo(...)]` are both required. |
| Top-level settings landing | `HomeSettingsPage.axaml(.cs)` | Page ID `settings.home`; no group. |
| General settings behavior | `General/BasicSettingsPage.axaml(.cs)` | Language change triggers `SettingsView.Current?.RequestRestartApp()`. |
| Privacy settings behavior | `General/PrivacySettingsPage.axaml(.cs)` | Binds to `MainConfigModel.General.PrivacySettings`; Sentry telemetry changes apply live through `TelemetryRuntimeService`, and online status changes apply live through `OnlineStatusService`. |
| Backup settings UI | `General/BackupSettingsPage.axaml(.cs)` | Lists real backup ZIPs under app data, creates/deletes backups, and restores selected data with a pre-restore snapshot plus restart prompt. |
| Security settings | `SecuritySettingsPage.axaml(.cs)` | Page ID `settings.general.security`; currently remains at subtree root but belongs to the General navigation group. |
| List management settings | `ListManagement/RollCallListSettingsPage.axaml(.cs)`, `ListManagement/LotteryListSettingsPage.axaml(.cs)` | Point-call list and lottery prize-pool viewing/import; separate table entries are intentionally not registered. |
| Draw settings | `Picking/DefaultDrawSettingsPage.axaml(.cs)`, `Picking/RollCallDrawSettingsPage.axaml(.cs)`, `Picking/QuickDrawSettingsPage.axaml(.cs)`, `Picking/LotteryDrawSettingsPage.axaml(.cs)`, `Picking/FaceDetectorSettingsPage.axaml(.cs)` | Default draw settings are flat grouped sections; specific draw pages show unique settings first and expandable override sections. |
| Personalized appearance settings | `Personalized/AppearanceSettingsPage.axaml(.cs)` | Mutations call `App.Current.RefreshPersonalizedSettings()`. |
| Linkage settings | `LinkageSettingsPage.axaml(.cs)` | Top-level `settings.linkage` entry between Personalized and ListManagement groups. |
| Notification settings | `VoiceSettingsPage.axaml(.cs)`, `DefaultNotificationSettingsPage.axaml(.cs)`, `RollCallNotificationSettingsPage.axaml(.cs)`, `QuickDrawNotificationSettingsPage.axaml(.cs)`, `LotteryNotificationSettingsPage.axaml(.cs)` | Voice/music and notification channel entries live under `settings.notification`; channel pages share `NotificationChannelSettingsContent`. Specific announcements are edited from list-page attached settings, not a standalone settings page. |
| Plugin settings | `Plugins/PluginOverviewSettingsPage.axaml(.cs)` | `settings.plugin` is an expandable sidebar group with a single compact management page rather than a marketplace. Plugin overview should keep one direct local-import action that copies a selected plugin directory into the app plugin store. |
| Log viewer | `LogViewerSettingsPage.axaml(.cs)` | Hidden page `settings.logs`; opened from the settings shell more-options menu. Reads `.log` and `.log.gz` from `data/logs`, supports filtering/search/copy/delete non-current logs. |
| Legacy v2-parity pages | `NotificationSettingsPage`, `HistoryManagementSettingsPage`, `MoreSettingsPage` | Kept on disk for compatibility/reference; whether they appear in navigation depends on Host registration. |
| About / external links | `AboutSettingsPage.axaml(.cs)` | Platform-specific `Process.Start` flow for URLs. |
| Shell navigation semantics | `../SettingsView.axaml.cs` | Default page `settings.home`, history stack, generated menu. |
| Localization pairing | `../../Langs/SettingsPages/` | Page folders mirror settings-page domains, except DEBUG-only pages. |

## CONVENTIONS

- Every non-debug settings page needs `[PageInfo]`, Host registration, and a matching localization folder under `SecRandom/Langs/SettingsPages/` when user-facing text is localized.
- Privacy page localization lives under `General/Privacy/` and is registered like other settings pages with only `Resources.resx` + `Resources.Designer.cs` in the project file.
- Page IDs here follow `settings.xxx` or `settings.group.xxx`; grouped Plan.md pages use `settings.general.*`, `settings.personalized.*`, `settings.listManagement.*`, `settings.picking.*`, and `settings.notification.*`.
- Group membership is owned by the `groupId` in `[PageInfo(...)]` and by `services.AddGroup(...)` in `BuildHost()`; do not handwire grouping in the page.
- Pages usually resolve `ViewModelBase` via `IAppHost.GetService<ViewModelBase>()`, set `DataContext = this`, and expose `Settings` from `ViewModel.Config.*`.
- V2-parity settings pages should keep the same `ScrollViewer` + `StackPanel.page-container animated-intro` + `FASettingsExpander` rhythm as existing settings pages.
- Voice/music owns the global TTS engine, voice, volume, and content switches. Per-student/per-prize specific announcement controls belong in list management attached settings for both roll-call and lottery records.
- If a settings change needs a restart, request it through `SettingsView.Current?.RequestRestartApp()` instead of restarting directly.
- If a settings change only needs live UI refresh, follow `AppearanceSettingsPage` and route through `App.Current.RefreshPersonalizedSettings()`.
- `settings.logs` should stay hidden from the sidebar and reachable from the settings shell more-options menu; keep that menu action as a navigation jump.
- `settings.plugin` is the visible expandable plugin group in the sidebar. Keep the single-page management layout aligned with settings-page density and avoid marketplace-style presentation.
- Plugin overview should stay toolbar-first and management-focused, with one local plugin import entry that copies a selected plugin directory into the app plugin store; do not surface promotional copy or a store layout.

## ANTI-PATTERNS

- Do not add a settings page file here without registering it in `BuildHost()`.
- Do not invent a new page-ID shape that breaks `settings.xxx` / `settings.group.xxx`.
- Do not localize DEBUG-only pages by accident; `DebugSettingsPage` intentionally remains debug-scoped.
- Do not put backup/config persistence logic in these pages when the boundary belongs in Core handlers or app services.
- Do not open navigation targets by manually editing menu items in `SettingsView`; register pages/groups and let the registry build the menu.
- Do not turn `settings.plugin` into a store or marketplace page; it is a settings group for installed plugin management.
