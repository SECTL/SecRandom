# Mobile Application Design

## Objective

Provide a usable mobile-first SecRandom workflow for roster management, fair point-call, lottery, and draw history.

## Product Context

This is an Android/iOS SingleView application built by the shared `SecRandom.App` Host. Mobile code is organized under the main app assembly and uses the same Core runtime services without starting desktop-only workflows.

## Visual Foundations

- Layout: a quiet top app bar, one scrollable content column, and a fixed bottom bar for `抽取`, `历史记录`, `概览`, and `设置`.
- The `抽取` page uses a native tab strip for `点名` and `抽奖`, a result-first surface, then the narrow-screen action panel.
- The point-call surface keeps a large result area, then a compact operation panel with list/group/gender selectors, remaining counts, a count stepper, Start, Remaining List, and More. It preserves desktop behavior without importing desktop left/right panel geometry.
- History and list management use horizontally scrollable tables: history offers profile plus overview/records modes; list rows expose common fields, a desktop-compatible attached-settings column, and a distinct More/delete operation column.
- The `设置` page is a catalog with exactly seven destinations: `通用`, `个性化`, `名单管理`, `抽取`, `备份`, `更新`, and `关于`, grouped under `偏好` / `数据` / `应用` section headers. The `更新` destination is capability-projected and hidden when in-app update is unsupported (iOS).

## Accessibility

- Navigation and draw-mode selection have visible labels, not icon-only controls. The bottom bar uses four fixed Avalonia buttons with Fluent icons, while settings rows use `FASettingsExpanderItem`; this preserves the Fluent visual language without exposing Android accessibility to FluentAvalonia's repeater peer defect.
- Primary actions are at least `48px` high and secondary actions are at least `44px` high.
- Content wraps and scrolls at phone widths; enabled state, text, and color together communicate state.
- Page content owns one vertically inertial scroll surface between the fixed app bar and bottom navigation. Dragging a navigation/settings row scrolls the page and never activates the row after movement.

## Voice & Tone

- Use concise classroom verbs such as `抽取一人`, `添加学生`, and `管理奖池`.
- Empty states direct users to the corresponding management surface instead of explaining platform internals.

## Implementation Practices

- `MobileViewHost` owns the one `NavigationPage`; it presents `MobileRootView` and independent MVE pages. `MobileRootView` owns the bottom navigation and displays the regular draw/history/overview keyed `UserControl` routes directly.
- `settings.mobile` is a hidden mobile-only catalog generated from registered mobile groups/pages. `SettingsView` is an independent MVE page and currently reuses the desktop settings layout, starting at that catalog.
- Profile mutations save through `IProfileCatalogManager` / `IProfileService`; draws record both persistent history and temporary records. Multi-member mobile point-call orchestration stays in `MobileRollCallService` and composes the existing Core filtering, sampling, and commit services without changing the Core session contract.
- The `LotteryEnabled` Core capability remains the only decision for whether the lottery segment can be selected.
- Theme selection applies the saved `Appearance.Theme` immediately. Mobile keeps the `公平抽取` / `随机抽取` choice in roll-call settings, but when `公平抽取` is selected it runs the Core algorithm with the fixed `MobileDesktopDefaultsV1` policy snapshot and ignores persisted `MainConfigModel.FairDrawSettings` values.
- The backup section exports/imports full-data ZIP archives and settings envelopes through the system StorageProvider pickers (stream-only, SAF-safe on Android); Core `DataArchiveService` validates the SecRandom v3 manifest before any import is confirmed, and busy operations disable all actions with progress text.

## Anti-Patterns

- Do not copy desktop navigation, tray controls, window controls, shortcuts, OOBE, or settings pages into mobile.
- Do not use decorative gradients, fake controls, or visible student/prize identifiers as internal identity.

## Decision-Making

- A fixed four-item bottom bar makes mobile destinations stable without duplicating desktop navigation.
- Combining point-call and lottery into `抽取` keeps the primary classroom task in one place while the capsule switcher makes the mode explicit before drawing.
- The large result panel gives the selected record classroom prominence. When enabled by the mobile draw setting, it renders per-record display images; native media playback and TTS stay behind the mobile platform seam.

## Workflow

The workflow supports tabular student/prize editing, desktop-compatible per-record image/music/voice settings, profile-aware history tables, theme selection, scoped multi-member point-call, single-prize lottery, repeat/fairness rules, remaining-list review, temporary record clearing, overview, StorageProvider-based backup/restore, and Android update checks. Spreadsheet import, proof export, notifications, and desktop integrations remain separate work.

## UI Foundation Tokens

- Mobile uses the shared application resources and the default Avalonia/Fluent control themes. It has no independent mobile style bundle or mobile-specific theme tokens.
- Page layout values are defined directly by each AXAML page or by code-behind fallback values. A page can bind a shared application resource through `DynamicResource`, but must remain usable when that resource is absent.

## Component Inventory

- `MobileCard`: lightweight content container that uses the default control appearance; an optional application-resource key can supply a result/metric background.
- `FASettingsExpander` / `FASettingsExpanderItem`: fixed settings layouts and catalog entries are declared in AXAML. Runtime code only projects capability-dependent data, storage/file flows, dialogs, tables, and media operations.
- `MobileEmptyState`: icon + title + optional description + optional guidance button that routes to the matching management surface.
- `MobileSectionHeader`: primary-tinted icon + semibold section title, replacing the `CreateLabel` IconText usage.
- `MobileSettingsPageBase`: capability projection and optional page-enter helper; SettingsView owns the mobile Back/Home controls rather than individual child pages.
- `MobileNavigationBar`: four equal native Avalonia toggle buttons hosted in the fixed bottom row, switching each Fluent icon from Regular to Filled when selected; glyphs load from `avares://SecRandom/Assets/Fonts/`. Native controls keep Android accessibility traversal and touch dispatch out of FluentAvalonia's repeater implementation.
- No generic mobile view factory remains. Stable page hierarchy belongs in AXAML; runtime construction is limited to DataGrid, dialogs, media, and StorageProvider workflows.

## Animation Primitives

`MobileAnimations` provides the mobile motion vocabulary (light Fluent-style opacity transitions, not the desktop rolling animation). Every primitive is interruptible: starting a new animation on a control cancels the previous one, and detaching from the visual tree cancels automatically. Visual animation failures are non-fatal and must not terminate the app.

```csharp
MobileAnimations.PlayPageEnter(scroll);                        // page enter: opacity fade (320 ms)
MobileAnimations.PlayResultReveal(resultText);                 // result reveal: opacity fade (250-400 ms, CircleEaseOut)
CancellationTokenSource roll = MobileAnimations.StartNameRoll(resultText, names);  // rapid candidate rolling while drawing
MobileAnimations.Cancel(resultText);                           // stop rolling/animations on a control
MobileAnimations.CrossFade(button, () => button.IsEnabled = false);  // state-change cross fade
```

- After stopping a name roll, write the final result text and then play `PlayResultReveal`.
- Animations run on the UI thread (except the name-roll timer loop) and must not block draw logic.

## Font Decision

Mobile keeps the platform system font. Linking the desktop MiSans family would add ~78 MB of TTF payloads (about 23 MB even for a Regular/Medium/Semibold subset) to every APK/IPA, which is unacceptable for mobile distribution. Android (Noto Sans CJK) and iOS (PingFang) system fonts provide adequate CJK rendering and match each platform's look. Revisit only if a glyph-subsetting pipeline becomes available.
