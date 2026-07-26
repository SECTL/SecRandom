# Context: Sentry User Feedback

## Intent

- Add an in-app Sentry user-feedback workflow in the settings window's top-right overflow menu.
- Place a separator before the final `反馈` menu item.
- Open feedback in the existing settings `DrawerHost`; users can choose Bug or feature/experience improvement, complete the relevant structured form, submit, or clear the in-progress draft with a bottom-right cancel button.
- For Bug reports, automatically upload the same ZIP produced by the application's existing diagnostic-data export.
- Change the crash recovery feedback affordance to a dropdown with `应用内反馈` first and `GitHub` second.

## Decisions

- No screenshot or recording attachment UI. The only automatic binary attachment is the standard diagnostic ZIP for Bug reports.
- Match the repository issue templates rather than using a generic freeform message:
  - Bug: concise title, required `期望的行为`, `实际结果`, and numbered `重现步骤`.
  - Feature/experience improvement: concise title, required `背景与动机` and `想要实现或优化的功能`.
  - Compose these fields into a readable Markdown `SentryFeedback.Message`, and include the category as a Sentry tag/context.
- Reuse `IImportExportService.ExportDiagnosticAsync(tempPath, includeExtendedData: false)` to generate each Bug attachment. This is exactly the application’s standard, manifested diagnostic ZIP: runtime metadata plus redacted logs; it excludes extended settings/summary/crash exports, credentials, list/profile/history content, and private plugin configuration. Attach it as `SecRandom_diagnostic_<utc>.zip`, await Sentry flush, then delete the temporary file on all outcomes.
- Extract any needed archive generation seam so the diagnostic ZIP remains a single source of truth rather than duplicating its redaction/archive format for feedback.
- Manual feedback is an explicit user action and remains available when `SentryTelemetryEnabled` is off. Do not initialize the always-on telemetry runtime, enable tracing, profiling, session tracking, logs, or automatic error collection for it. Instead, the feedback service creates a short-lived, feedback-only `SentryClient` with the same DSN/release/environment and privacy scrubbers, sends one `SentryFeedback` plus the optional diagnostic ZIP attachment, awaits a bounded flush, and disposes it. The UI states this upload explicitly.
- Keep Sentry SDK calls in app-layer telemetry/feedback services, never in views. Isolate short-lived client setup from `TelemetryRuntimeService` so the privacy policy for background telemetry remains intact and testable.
- Create one reusable `FeedbackDrawer` control and transient ViewModel under the app layer. Its header contains only the `反馈` title and the `暂时关闭` close command; remove descriptive subtitle copy. Opening it reuses the current draft; `暂时关闭` only closes the drawer. `取消反馈` clears every structured field/category and closes it. Successful submission clears and closes, then shows a toast; failure preserves the draft.
- Place a lower-left secondary button with a local GitHub brand-mark bitmap and `GitHub` text in the drawer footer. It opens `https://github.com/SECTL/SecRandom/issues` through the existing `IExternalLauncher` and preserves the draft; keep `取消反馈` and `提交反馈` right-aligned. The brand mark is not substituted with a generic Fluent glyph.
- For crash recovery, build the DI Host in the startup-prompt-only branch before assigning `CrashRecoveryWindow`, but still before single-instance acquisition and without starting the normal Host/runtime services. This makes the same diagnostic-export and feedback services available without changing crash-recovery startup ordering.
- Replace the direct GitHub button with a `反馈问题` menu button: first `应用内反馈`, then `GitHub`. Selecting application feedback submits a crash Bug through the same service, automatically attaching the standard diagnostic ZIP and current crash report context; it reports success/failure in the recovery window. GitHub retains the current generated issue URL as an independent fallback.

## Constraints

- Avalonia + FluentAvalonia; compiled bindings enabled.
- Reuse `DrawerHost`; do not create an unrelated window or manually create reusable services in views.
- New user-facing text must be localized: settings-shell resources for the drawer and menu, crash-recovery resources for the dropdown and status text. Register only base `.resx` and designer entries in the project file.
- ViewModels and reusable services must be registered in `App.axaml.cs` `BuildHost()`.
- Manual feedback must not mutate the persisted telemetry preference, and the ordinary telemetry runtime must continue to obey it.
- Existing diagnostic export redacts bearer tokens, password/secret/token/authorization values, absolute paths, and email addresses. The feedback attachment must use that code path, not a parallel redactor.
- No product-code changes during plan mode.

## Open questions

- None. The user confirmed no screenshots, explicit feedback while telemetry is off, and reuse of the normal diagnostic ZIP.

## Discarded options

- Opening GitHub Issues from settings: rejected because the request explicitly asks for Sentry user feedback.
- A separate feedback settings page: rejected because the requested entry point and workflow are the settings overflow menu and `DrawerHost`.
- A small custom `diagnostics.txt`: rejected because the user requires the same ZIP generated by the application diagnostic exporter.
- Screenshot uploads: rejected by the user.
- Routing manual feedback through `TelemetryRuntimeService`: rejected because it would make explicit submissions unavailable when background telemetry is off.
- Silently changing the telemetry preference or starting the normal telemetry runtime for feedback: rejected because manual feedback must be isolated from background collection.

## Blast radius

- No removals, renames, or narrowed public surfaces planned.
