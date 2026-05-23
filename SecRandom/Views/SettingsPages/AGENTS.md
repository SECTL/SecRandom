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
├── HomeSettingsPage.axaml(.cs)           # Top-level landing page
├── AboutSettingsPage.axaml(.cs)          # Bottom-nav about page; opens external links
├── DebugSettingsPage.axaml(.cs)          # DEBUG-only bottom page
├── General/                              # Grouped settings.general.* pages
└── Personalized/                         # Grouped settings.personalized.* pages
```

## WHERE TO LOOK

| Task | Location | Notes |
|------|----------|-------|
| Register settings page | `SecRandom/App.axaml.cs` | `BuildHost()` plus `[PageInfo(...)]` are both required. |
| Top-level settings landing | `HomeSettingsPage.axaml(.cs)` | Page ID `settings.home`; no group. |
| General settings behavior | `General/BasicSettingsPage.axaml(.cs)` | Language change triggers `SettingsView.Current?.RequestRestartApp()`. |
| Backup settings UI | `General/BackupSettingsPage.axaml(.cs)` | Currently UI/demo-facing backup list; real persistence lives elsewhere. |
| Personalized appearance settings | `Personalized/AppearanceSettingsPage.axaml(.cs)` | Mutations call `App.Current.RefreshPersonalizedSettings()`. |
| About / external links | `AboutSettingsPage.axaml(.cs)` | Platform-specific `Process.Start` flow for URLs. |
| Shell navigation semantics | `../SettingsView.axaml.cs` | Default page `settings.basic`, history stack, generated menu. |
| Localization pairing | `../../Langs/SettingsPages/` | Page folders mirror settings-page domains, except DEBUG-only pages. |

## CONVENTIONS

- Every non-debug settings page needs `[PageInfo]`, Host registration, and a matching localization folder under `SecRandom/Langs/SettingsPages/` when user-facing text is localized.
- Page IDs here follow `settings.xxx` or `settings.group.xxx`; current grouped pages use `settings.general.*` and `settings.personalized.*`.
- Group membership is owned by the `groupId` in `[PageInfo(...)]` and by `services.AddGroup(...)` in `BuildHost()`; do not handwire grouping in the page.
- Pages usually resolve `ViewModelBase` via `IAppHost.GetService<ViewModelBase>()`, set `DataContext = this`, and expose `Settings` from `ViewModel.Config.*`.
- If a settings change needs a restart, request it through `SettingsView.Current?.RequestRestartApp()` instead of restarting directly.
- If a settings change only needs live UI refresh, follow `AppearanceSettingsPage` and route through `App.Current.RefreshPersonalizedSettings()`.

## ANTI-PATTERNS

- Do not add a settings page file here without registering it in `BuildHost()`.
- Do not invent a new page-ID shape that breaks `settings.xxx` / `settings.group.xxx`.
- Do not localize DEBUG-only pages by accident; `DebugSettingsPage` intentionally remains debug-scoped.
- Do not put backup/config persistence logic in these pages when the boundary belongs in Core handlers or app services.
- Do not open navigation targets by manually editing menu items in `SettingsView`; register pages/groups and let the registry build the menu.
