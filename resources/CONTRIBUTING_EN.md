# Contributing to SecRandom

Thank you for reporting issues, suggesting improvements, improving documentation, or contributing code to SecRandom.

**Language** [ [简体中文](../CONTRIBUTING.md) | **English** | [日本語](CONTRIBUTING_JA.md) ]

## Opening an issue

- Use [GitHub Issues](https://github.com/SECTL/SecRandom/issues) to report bugs or request features.
- For a bug, include reproduction steps, expected and actual behavior, your SecRandom version, system environment, and relevant logs or screenshots.
- For a feature request, explain the use case, expected behavior, and why the current product does not meet the need.
- Do not place passwords, TOTP secrets, USB-binding tokens, or other sensitive data in public issues, logs, or screenshots.

## Development environment

SecRandom v3 is a .NET desktop application:

| Category | Technology | Purpose |
| --- | --- | --- |
| Language and runtime | C# / .NET 10 | Application, core services, and tests |
| Desktop UI | Avalonia + FluentAvalonia | Cross-platform desktop UI |
| Dependency injection | Microsoft.Extensions.Hosting | Application-service and ViewModel composition |
| Testing | xUnit v3 | Unit tests |
| Build and release | GitHub Actions | Multi-platform builds, packages, and releases |

### Prerequisites

- [.NET SDK 10.0.x](https://dotnet.microsoft.com/download/dotnet/10.0)
- Git
- Desktop runtime support for the target platform. Refer to the relevant release notes for packaging and installation details.

### Get and run the project

```bash
git clone https://github.com/<your-account>/SecRandom.git
cd SecRandom
git remote add upstream https://github.com/SECTL/SecRandom.git

dotnet restore SecRandom.sln
dotnet build SecRandom.sln -c Release --no-restore
dotnet test SecRandom.sln -c Release --no-build
dotnet run --project SecRandom.Desktop/SecRandom.Desktop.csproj
```

To validate only the core test project:

```bash
dotnet test SecRandom.Core.Tests/SecRandom.Core.Tests.csproj -c Release --no-restore
```

## Code conventions

Read the root [AGENTS.md](../AGENTS.md) and [project rules](../docs/project_rules.md) before changing code. Their requirements take precedence over general coding preferences. The following rules are especially important:

- `SecRandom.Desktop` is a startup shell only; do not put application business logic there.
- Reusable services and ViewModels must be registered in `BuildHost()` in `SecRandom/App.axaml.cs`. Do not instantiate reusable services directly from pages.
- A navigation page needs `[PageInfo(...)]` and `AddMainPage<T>()` or `AddSettingsPage<T>()` registration. Do not hard-code sidebar menu items.
- Place UI text in the page-specific resource directory under `Langs`. Base, English, and Japanese resources must keep the same key set; do not move every page's text into a shared resource file.
- Use `RecordId` for student, prize, and history identity. Displayed IDs and names are not required unique identifiers.
- Direct dictionary and collection changes do not automatically persist configuration; the owning code must save at the appropriate lifecycle boundary.
- All security authorization goes through `ISecurityService`. Credentials must never appear in ordinary settings, logs, exports, or diagnostics.
- New application icons must use the project's Fluent Filled icon system, not raw Unicode Fluent glyphs.

## Scope and testing

- Keep changes scoped to the problem being solved and avoid unrelated refactors.
- Add focused tests for new or fixed core behavior, especially around drawing, configuration, import/export, security, proofs, and shared contracts.
- UI, system integration, privileged behavior, updates, and cross-platform behavior need runtime verification. A successful compilation is not enough.
- Do not commit generated `bin/`, `obj/`, `artifacts/`, `publish/`, or packaging output.
- When documentation changes, keep factual content, links, and structure aligned across the Simplified Chinese, English, and Japanese README/contribution-guide set.

## Commits and pull requests

- Create a topic branch from the relevant current branch and sync with upstream before starting work.
- Prefer [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/), for example: `fix: correct proof retention cleanup`.
- A pull request should explain the problem, approach, and verification. Include screenshots or a recording for UI changes, and explain compatibility or migration effects for behavior changes.
- Before opening a PR, run restore, build, and test commands appropriate to the changed scope. State any checks you did not run and why.
- Do not submit generative-AI output without human review. Contributors remain responsible for correctness and license compliance.

## Contributor License Agreement (CLA)

[`CLA.md`](../CLA.md) is this project's Contributor License Agreement (v1.0, effective 1 November 2026).

- Your first pull request will ask you to sign it in a PR comment. Signatures are recorded by the CLA Assistant workflow (`.github/workflows/cla.yml`) in `signatures/version1/cla.json`.
- An unsigned pull request will not be merged. This does not affect your GPLv3 rights; it only means the contribution falls outside what the maintainers may relicense.
- The signing statement is attached automatically to the bot's comment. If all your contributions are new, post it as is:

  ```text
  I have read the CLA Document and I hereby sign the CLA
  ```

- If you also want this signature to cover your **earlier** contributions (`CLA.md` §9.5, optional), append one clause:

  ```text
  I have read the CLA Document and I hereby sign the CLA, and I agree that this CLA also covers my prior contributions
  ```

- The CLA is a license grant, not a copyright assignment: you keep the copyright and may do anything with your contribution under GPLv3. The maintainers gain one extra sublicensing right, usable only for a possible future outbound license change, which requires 30 days' public notice and an objection period (`CLA.md` §4).
- The CLA does not change the licensing of any released version under GNU GPLv3, and does not cover submodules under `vendors/` or other third-party dependencies.

## CI and releases

The repository uses GitHub Actions:

- `.github/workflows/build_publish.yml` handles multi-platform builds, packaging, signed-manifest generation, and manual release flow.
- `.github/workflows/build_ios.yml` builds iOS separately: after the other platforms are published, it attaches the IPA, a re-signed update manifest, and a refreshed release note to the matching release.
- `.github/workflows/codeQL.yml` runs CodeQL security analysis.

Normal contributions do not trigger releases through commit-message keywords. Maintainers publish from the manually dispatched GitHub Actions workflow with an explicit release tag.

Thank you for contributing.
