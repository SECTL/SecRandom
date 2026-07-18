# Mobile Shell Design

## Objective

Show a clear, calm first mobile surface while SecRandom's draw workflow is still desktop-only.

## Product Context

This is an Android/iOS platform-shell boundary for an education random-draw application. It must communicate availability without implying that desktop workflows already work on a phone.

## Visual Foundations

- Reuse the SecRandom application icon and a restrained system/Avalonia control palette.
- Center one narrow content column with the product mark, title, status, and a factual explanation.
- Use the platform default font rendering and no gradients, cards, or decorative effects.

## Accessibility

- Content wraps at phone widths and has no hidden gesture or color-only meaning.
- The screen exposes status as text and contains no inactive imitation controls.

## Voice & Tone

- State exactly what is ready and what is deliberately deferred.
- Avoid dates, promotional copy, and desktop-only terminology as mobile actions.

## Implementation Practices

- Keep the shell read-only and independent from the desktop application assembly.
- Build the view from standard Avalonia controls so the platform host has no desktop-window dependency.
- Resolve display strings from the mobile project's page-local resources.

## Anti-Patterns

- No dashboard cards, feature checklist, hero gradient, fake navigation, or call to action.
- Do not render tray, topmost, global shortcut, or desktop window settings on mobile.

## Decision-Making

- A single status column makes the incomplete boundary explicit without presenting a dead-end desktop UI.
- Reusing the existing product mark preserves identity while avoiding a second mobile design system.

## Workflow

Replace this shell only after a mobile workflow has an explicit service boundary, persistence plan, and Android/iOS validation.
