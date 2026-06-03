# 贡献指南

❤️感谢您向 SecRandom 做出贡献，您可以为 SecRandom 项目做出包括但不限于反馈 Bug、提出功能请求、贡献代码等贡献。在进行贡献前，请务必阅读以下指南。

**语言选择** [ **✔简体中文** | [English](resources/CONTRIBUTING_EN.md) | [繁體中文](resources/CONTRIBUTING_ZH_TW.md) ]

## 反馈 Bug

如果在使用 SecRandom 的过程中遇到 Bug，可以在 GitHub Issues 中提交 Bug 反馈。

**请务必准确地按照 Issues 模板中的要求和示例填写相关字段**，否则开发者可能难以诊断您遇到的问题。

## 提交功能请求

如果您有关于 SecRandom 新功能的想法，欢迎在 GitHub Issues 提交功能请求。

## 贡献代码

在为 SecRandom 贡献代码之前，请务必阅读以下指南。

### 技术栈

在开始贡献之前，了解项目的技术栈将有助于您更快地上手：

| 类别 | 技术/工具 | 用途 |
|------|-----------|------|
| 编程语言 | Python 3.13.5 | 项目的主要开发语言 |
| 包管理器 | uv | 依赖管理和虚拟环境创建 |
| UI 框架 | PySide6 + PyQt-Fluent-Widgets | 现代化桌面应用界面开发 |
| 日志管理 | loguru | 高效的日志记录 |
| 数据处理 | numpy & pandas | 数据处理和分析 |
| 语音合成 | edge-tts | 文本转语音功能 |
| Excel 处理 | openpyxl | Excel 文件导入导出 |
| 安全认证 | pyotp | 双因素认证 |

### 开发环境搭建

#### 1. 前置条件

确保您的系统已安装以下软件：

- Python 3.13.5
- Git
- uv 包管理器 ([安装指南](https://docs.astral.sh/uv/getting-started/))

#### 2. 准备工作

1. **Fork 项目**
   - 访问 [SecRandom GitHub 仓库](https://github.com/SECTL/SecRandom)
   - 点击右上角的 "Fork" 按钮，创建您自己的仓库副本

2. **克隆仓库**

   ```bash
   git clone https://github.com/您的用户名/SecRandom.git
   cd SecRandom
   ```

3. **添加上游仓库**

   ```bash
   git remote add upstream https://github.com/SECTL/SecRandom.git
   ```

#### 3. 安装依赖

使用 uv 安装项目依赖：

```bash
uv sync
```

#### 4. 运行项目

安装依赖后，您可以直接运行项目：

```bash
uv run main.py
```

#### 5. C# IPC 相关开发

您需要 .NET 9.0 来生成 .NET 程序集的 Python 存根（用于 IDE 提示）。如果您选择放弃 IDE 提示，无需安装 .NET 9.0。
程序集存放在 `data/dlls` 目录。下面是存根生成方法：

```bash
powershell ./scripts/generate-stubs.ps1
```

如果您正在使用 Linux 发行版，请先安装 [PowerShell 7](https://github.com/PowerShell/PowerShell)，并将上方命令中的 `powershell` 替换为 `pwsh`。

### 贡献准则

**您为 SecRandom 贡献的功能须遵循以下准则：**

- **稳定**：您贡献的功能需要能尽可能稳定工作。
- **具有泛用性**：您贡献的功能需要面向大部分用户。
- **激进功能需添加开关**：如果您贡献的功能比较激进，请添加功能开关，并默认禁用此功能。
- **能用**：在提交补丁前，请在本地测试您实现的功能是否能正常使用。
- 尽量不要提交仅包含文案修复的补丁。

### 补丁质量

随着本项目的规模逐渐扩大，有些用户提交了一些低质量的补丁。这些补丁有的完全不能实现预期的功能，有的甚至根本不能通过编译，浪费了开发者的时间和精力对这些补丁进行代码审阅和问题排查。我们接受有瑕疵的补丁， **但我们希望您在提交补丁前，您的补丁至少应该满足以下的要求：**

- 实现的功能能够工作，在提交补丁前请至少在本地机器测试一次补丁的功能是否可以正常工作。
- 我们不建议在没有人为干预的情况下完全地使用生成式人工智能实现您要贡献的功能。

如果您持续提交低质量的补丁，我们可能会限制您继续向本项目/组织提交补丁。

### 分支与开发周期

SecRandom 代码仓库目前具有以下分支：

- `master`：SecRandom 主要开发分支。

当开始下个版本的 SecRandom 时，会将当前的主分支分叉到对应的维护分支。在开发下一个版本的 SecRandom 过程中，也会在维护分支上并行维护当前稳定版本的功能。

由于不同开发分支上的代码接口可能存在差异。因此，**根据您做出的贡献类型，您需要选择不同的基础分支。**

**以下类型的贡献建议以当前的维护分支为基础分支：**

- 修复稳定版中的 Bug
- 对稳定版中的功能进行小幅度的优化

**以下类型的贡献建议以 `master` 为基础分支：**

- 添加新的功能
- 对代码进行重构
- 其它对 SecRandom 进行较大改动的贡献
- 修改README等文档

### 提交

**请务必在提交前使用`uv run pre-commit run --all-files`检查并自动规范代码格式，否则您的代码将无法通过Checks**

在本代码仓库提交时，请尽量遵守 [约定式提交](https://www.conventionalcommits.org/zh-hans/v1.0.0/) 规范。

### 合并更改

在进行合并之前，请先测试您贡献的代码，确保您贡献的代码能稳定运作。

您可以向本项目发起 [Pull Request](https://github.com/SECTL/SecRandom/pulls) 来合并您的更改。在发起 Pull Request 时，请简要地描述您做的更改，并最好附上您实现的功能的演示截图/视频。

### Actions 构建工作流

SecRandom 项目使用 GitHub Actions 进行自动构建和发布，配置文件位于 `.github/workflows/build-unified.yml`。

#### 触发构建

您可以通过以下方式触发构建：

1. **提交消息触发**：
   - 在 commit 消息中包含 `开始打包` 关键词
   - 示例：`git commit -m "新增功能 开始打包"`

---

感谢您对 SecRandom 项目的支持和贡献！让我们一起打造更好的公平随机抽取系统！ 🚀
