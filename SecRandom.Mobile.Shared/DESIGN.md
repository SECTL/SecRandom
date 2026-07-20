# Mobile Application Design

## Objective

Provide a usable mobile-first SecRandom workflow for roster management, fair point-call, lottery, and draw history.

## Product Context

This is an Android/iOS SingleView application for education random draws. It is independent from the desktop application assembly and uses only the Core runtime service boundary.

## Visual Foundations

- Palette: ink `#1D2939`, muted text `#667085`, canvas `#F7F8FA`, primary `#006E5B`, primary wash `#E8F4F0`, warm draw wash `#FFF4E5`, rules `#E4E7EC`.
- Layout: a quiet top app bar, one scrollable content column, and a fixed bottom bar for `抽取`, `历史记录`, `概览`, and `设置`.
- The `抽取` page has a top-left capsule switcher: `点名` on the left and `抽奖` on the right.
- The `设置` page is a catalog with exactly seven destinations: `通用`, `个性化`, `名单管理`, `抽取`, `备份`, `更新`, and `关于`, grouped under `偏好` / `数据` / `应用` section headers. The `更新` destination is capability-projected and hidden when in-app update is unsupported (iOS).

## Accessibility

- Navigation and draw-mode selection have visible labels, not icon-only controls. The bottom bar uses native Avalonia `ToggleButton` controls in four equal columns; mobile surfaces do not use FluentAvalonia templates.
- Primary actions are at least `48px` high and secondary actions are at least `44px` high.
- Content wraps and scrolls at phone widths; enabled state, text, and color together communicate state.

## Voice & Tone

- Use concise classroom verbs such as `抽取一人`, `添加学生`, and `管理奖池`.
- Empty states direct users to the corresponding management surface instead of explaining platform internals.

## Implementation Practices

- `MobileRootView` owns mobile-only fixed chrome; business destinations and every mobile settings page are independent MVE sessions shown through its inner host.
- Profile mutations save through `IProfileService`; draws record both persistent history and temporary records.
- The `LotteryEnabled` Core capability remains the only decision for whether the lottery segment can be selected.
- Theme selection applies the saved `Appearance.Theme` immediately. Mobile keeps the `公平抽取` / `随机抽取` choice in roll-call settings, but when `公平抽取` is selected it runs the Core algorithm with the fixed `MobileDesktopDefaultsV1` policy snapshot and ignores persisted `MainConfigModel.FairDrawSettings` values.
- The backup section exports/imports full-data ZIP archives and settings envelopes through the system StorageProvider pickers (stream-only, SAF-safe on Android); Core `DataArchiveService` validates the SecRandom v3 manifest before any import is confirmed, and busy operations disable all actions with progress text.

## Anti-Patterns

- Do not copy desktop navigation, tray controls, window controls, shortcuts, OOBE, or settings pages into mobile.
- Do not use decorative gradients, fake controls, or visible student/prize identifiers as internal identity.

## Decision-Making

- A fixed four-item bottom bar makes mobile destinations stable without duplicating desktop navigation.
- Combining point-call and lottery into `抽取` keeps the primary classroom task in one place while the capsule switcher makes the mode explicit before drawing.
- The large result panel gives the selected record classroom prominence without importing desktop preview, audio, or notification behavior.

## Workflow

The initial workflow supports local student/prize editing, theme selection, single-record Core draws, repeat/fairness rules, history review, temporary record clearing, overview, StorageProvider-based backup/restore, and Android update checks. Spreadsheet import, multi-record draw, proof export, audio, notifications, and desktop integrations remain separate work.

## UI Foundation Tokens

- Color tokens live in `Styles/MobileStyles.axaml` as Light/Dark theme dictionaries: `MobileCanvasBrush`, `MobileSurfaceBrush`, `MobileSurfaceMutedBrush`, `MobileBorderBrush`, `MobilePrimaryBrush` (`#006E5B` light / `#72D8C2` dark), `MobilePrimaryWashBrush`, `MobileWarmWashBrush`, `MobileMutedTextBrush`, `MobileDangerBrush`, and `MobileTextBrush`.
- Metric tokens: spacing `MobileSpacingXs/Sm/Md/Lg/Xl` = 4/8/14/20/28; corner radii `MobileCornerRadiusSmall/Medium/Large` = 10/15/18; font sizes `MobileFontSizeCaption/Body/Section/Title/Result` = 12/16/20/24/34; touch heights `MobileMinHeightSecondary/Primary` = 44/48; `MobilePagePadding` = 20,20,20,28; `MobileCardPadding` = 16.
- Pages and factories consume colors only through `MobileTheme.BindBrush(...)` / `MobileTheme.Resource(key)` DynamicResource bindings, so a theme switch refreshes page content automatically. The legacy `MobileTheme.*` snapshot brushes remain only for the shell (`MobileRootView`, `MobilePageHeader`, `MobileNavigationBar`), which already re-reads them in `RefreshTheme()` on theme change. Page content must not use the snapshot brushes.

## Component Inventory

- `MobileCard`: rounded surface container; its default look comes from the type-keyed ControlTheme in `MobileStyles.axaml`, and a wash resource key (`MobileTheme.Keys.PrimaryWash` / `WarmWash`) produces tinted result/metric cards.
- `MobileSettingRow`: title/description/footer settings row with factories `Toggle`, `Integer`, `Choice` (unique radio group per row by default, fixing the old cross-group `GroupName` interference), `Navigation`, and `Simple`.
- `MobileSegmentedControl`: capsule segmented selector with primary-wash selection visuals, replacing the hand-assembled segment buttons.
- `MobileEmptyState`: icon + title + optional description + optional guidance button that routes to the matching management surface.
- `MobileSectionHeader`: primary-tinted icon + semibold section title, replacing the `CreateLabel` IconText usage.
- `MobileSettingsPageBase`: settings-page skeleton (lightweight title/description header instead of an FAInfoBar, optional back button, scroll, and page-enter transition) with capability projection pass-throughs; root-level destinations such as the settings catalog pass `showBackButton: false`.
- The old `MobileUi` factory methods keep their signatures and delegate to these controls, so existing pages keep working until their staged reskin.

## Animation Primitives

`MobileAnimations` provides the mobile motion vocabulary (light Fluent-style transitions, not the desktop rolling animation). Every primitive is interruptible: starting a new animation on a control cancels the previous one, and detaching from the visual tree cancels automatically.

```csharp
MobileAnimations.PlayPageEnter(scroll);                        // page enter: fade in + rise (320 ms)
MobileAnimations.PlayResultReveal(resultText);                 // result reveal: scale + fade (250-400 ms, CircleEaseOut)
CancellationTokenSource roll = MobileAnimations.StartNameRoll(resultText, names);  // rapid candidate rolling while drawing
MobileAnimations.Cancel(resultText);                           // stop rolling/animations on a control
MobileAnimations.CrossFade(button, () => button.IsEnabled = false);  // state-change cross fade
```

- After stopping a name roll, write the final result text and then play `PlayResultReveal`.
- Animations run on the UI thread (except the name-roll timer loop) and must not block draw logic.

## Font Decision

Mobile keeps the platform system font. Linking the desktop MiSans family would add ~78 MB of TTF payloads (about 23 MB even for a Regular/Medium/Semibold subset) to every APK/IPA, which is unacceptable for mobile distribution. Android (Noto Sans CJK) and iOS (PingFang) system fonts provide adequate CJK rendering and match each platform's look. Revisit only if a glyph-subsetting pipeline becomes available.
