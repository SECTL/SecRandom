# Mobile Application Design

## Objective

Provide a usable mobile-first SecRandom workflow for roster management, fair point-call, lottery, and draw history.

## Product Context

This is an Android/iOS SingleView application for education random draws. It is independent from the desktop application assembly and uses only the Core runtime service boundary.

## Visual Foundations

- Palette: ink `#1D2939`, muted text `#667085`, canvas `#F7F8FA`, primary `#006E5B`, primary wash `#E8F4F0`, warm draw wash `#FFF4E5`, rules `#E4E7EC`.
- Layout: a quiet top app bar, one scrollable content column, and a fixed bottom bar for `抽取`, `历史记录`, `概览`, and `设置`.
- The `抽取` page has a top-left capsule switcher: `点名` on the left and `抽奖` on the right.
- The `设置` page is a catalog with exactly eight sections: `通用`, `个性化`, `名单管理`, `抽取`, `公平抽取`, `备份`, `更新`, and `关于`.

## Accessibility

- Navigation and draw-mode selection have visible labels, not icon-only controls.
- Primary actions are at least `48px` high and secondary actions are at least `44px` high.
- Content wraps and scrolls at phone widths; enabled state, text, and color together communicate state.

## Voice & Tone

- Use concise classroom verbs such as `抽取一人`, `添加学生`, and `管理奖池`.
- Empty states direct users to the corresponding management surface instead of explaining platform internals.

## Implementation Practices

- `MobileShellView` owns mobile-only visual navigation and uses constructor-injected Core services.
- Profile mutations save through `IProfileService`; draws record both persistent history and temporary records.
- The `LotteryEnabled` Core capability remains the only decision for whether the lottery segment can be selected.
- Theme selection applies the saved `Appearance.Theme` immediately. Draw and fair-draw controls save directly to the Core settings that `DrawEngine` consumes.
- The backup section explains its current boundary instead of exposing an action: mobile does not yet have a system-authorized import/export transaction.

## Anti-Patterns

- Do not copy desktop navigation, tray controls, window controls, shortcuts, OOBE, or settings pages into mobile.
- Do not use decorative gradients, fake controls, or visible student/prize identifiers as internal identity.

## Decision-Making

- A fixed four-item bottom bar makes mobile destinations stable without duplicating desktop navigation.
- Combining point-call and lottery into `抽取` keeps the primary classroom task in one place while the capsule switcher makes the mode explicit before drawing.
- The large result panel gives the selected record classroom prominence without importing desktop preview, audio, or notification behavior.

## Workflow

The initial workflow supports local student/prize editing, theme selection, single-record Core draws, repeat/fairness rules, history review, temporary record clearing, overview, and Android update checks. Import, mobile backup/restore, multi-record draw, proof export, audio, notifications, and desktop integrations remain separate work.
