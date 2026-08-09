# 贡献指南

感谢你为 SecRandom 提交问题、提出建议、完善文档或贡献代码。

**语言** [ **简体中文** | [English](resources/CONTRIBUTING_EN.md) | [日本語](resources/CONTRIBUTING_JA.md) ]

## 提交 Issue

- 使用 [GitHub Issues](https://github.com/SECTL/SecRandom/issues) 报告缺陷或提出功能请求。
- 提交缺陷时，请提供复现步骤、预期行为、实际行为、SecRandom 版本、系统环境和必要日志或截图。
- 提交功能请求时，请说明使用场景、预期行为和现有功能无法满足需求的原因。
- 不要在公开 Issue、日志或截图中提交密码、TOTP 密钥、USB 绑定令牌或其他敏感数据。

## 开发环境

SecRandom v3 是基于 .NET 的桌面应用：

| 类别 | 技术 | 用途 |
| --- | --- | --- |
| 语言与运行时 | C# / .NET 10 | 应用、核心服务与测试 |
| 桌面 UI | Avalonia + FluentAvalonia | 跨平台桌面界面 |
| 依赖注入 | Microsoft.Extensions.Hosting | 应用服务与 ViewModel 组合 |
| 测试 | xUnit v3 | 单元测试 |
| 构建与发布 | GitHub Actions | 多平台构建、打包与发布 |

### 前置条件

- [.NET SDK 10.0.x](https://dotnet.microsoft.com/download/dotnet/10.0)
- Git
- 目标平台对应的桌面运行环境；实际打包与安装方式以发行版本说明为准。

### 获取与运行

```bash
git clone https://github.com/<your-account>/SecRandom.git
cd SecRandom
git remote add upstream https://github.com/SECTL/SecRandom.git

dotnet restore SecRandom.sln
dotnet build SecRandom.sln -c Release --no-restore
dotnet test SecRandom.sln -c Release --no-build
dotnet run --project SecRandom.Desktop/SecRandom.Desktop.csproj
```

如需只验证某个测试项目，可运行：

```bash
dotnet test SecRandom.Core.Tests/SecRandom.Core.Tests.csproj -c Release --no-restore
```

## 代码约定

提交前请阅读仓库根目录的 [AGENTS.md](AGENTS.md) 和 [项目规则](docs/project_rules.md)。其中的约束优先于一般编码习惯。以下规则尤其重要：

- `SecRandom.Desktop` 仅负责启动，不要把应用业务逻辑放入启动壳。
- 可复用服务和 ViewModel 必须在 `SecRandom/App.axaml.cs` 的 `BuildHost()` 中注册；不要在页面中直接 `new` 可复用服务。
- 新导航页需要 `[PageInfo(...)]`，并通过 `AddMainPage<T>()` 或 `AddSettingsPage<T>()` 注册，不要手写侧边栏菜单项。
- UI 文本按页面放入 `Langs` 下对应的资源目录。基础资源、英文和日文资源保持同一键集合；不要把所有页面文本放进共享资源文件。
- 使用 `RecordId` 维护学生、奖品和历史的内部身份；显示的 `Id` 或名称不是必需的唯一标识。
- 配置字典和集合的直接修改不会自动保存，负责该变更的代码必须在合适的生命周期调用保存。
- 安全授权统一通过 `ISecurityService`；凭据不得写入普通设置、日志、导出数据或诊断信息。
- 新应用图标使用项目的 Fluent Filled 图标系统，不要加入原始 Unicode Fluent 字形。

## 修改范围与测试

- 将改动保持在解决问题所需的最小范围，避免顺带重构无关代码。
- 为新增或修复的核心行为补充聚焦测试，特别是抽取、配置、导入导出、安全、证明和共享契约。
- UI、系统集成、权限、更新和跨平台行为需要实际运行验证；编译通过不能替代运行验证。
- 不要提交 `bin/`、`obj/`、`artifacts/`、`publish/` 或打包过程生成的文件。
- 修改文档时，保持简体中文、英文和日文 README/贡献指南中的事实、链接和结构同步。

## 提交与 Pull Request

- 从当前目标分支创建主题分支，并在开始前同步上游。
- 提交信息建议遵循 [Conventional Commits](https://www.conventionalcommits.org/zh-hans/v1.0.0/)，例如 `fix: correct proof retention cleanup`。
- PR 应说明问题、方案和验证方式；涉及 UI 时附上截图或录屏，涉及行为变化时说明兼容性和迁移影响。
- 提交 PR 前至少运行与改动范围相符的还原、构建和测试命令，并在 PR 描述中说明未运行的检查及原因。
- 不要未经人工审阅直接提交生成式 AI 产出的代码、测试或文档；贡献者对提交内容的正确性和许可合规性负责。

## CI 与发布

仓库使用 GitHub Actions：

- `.github/workflows/build_publish.yml` 负责多平台构建、打包、签名清单生成和手动发布流程。
- `.github/workflows/codeQL.yml` 负责 CodeQL 安全扫描。

常规贡献无需通过提交信息触发发布。发布由维护者在 GitHub Actions 的手动工作流中指定发布标签后执行。

感谢你的贡献。
