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

## CI and releases

The repository uses GitHub Actions:

- `.github/workflows/build_publish.yml` handles multi-platform builds, packaging, signed-manifest generation, and manual release flow.
- `.github/workflows/codeQL.yml` runs CodeQL security analysis.

Normal contributions do not trigger releases through commit-message keywords. Maintainers publish from the manually dispatched GitHub Actions workflow with an explicit release tag.

Thank you for contributing.
