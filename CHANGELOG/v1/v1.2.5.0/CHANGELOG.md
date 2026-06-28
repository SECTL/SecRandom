> **⚠️ 重要提示：自本版本起，SecRandom 已停止对 Windows 7 和 x86 系统的官方支持。为获得最佳使用体验，我们强烈建议使用 Windows 10 x64 或更高版本系统。**
> **对于仍需在 Windows 7 系统上使用本工具的用户，可通过手动调整依赖库版本后自行打包的方式适配该系统环境，从而继续使用本工具。**
> **对于仍需在 x86 系统上使用本工具的用户，我们建议使用 Python 3.8 进行打包，以确保最佳的兼容性和稳定性。**
> **如需自行打包，请使用仓库根目录下的 requirements-windows-win7_x64_x86.txt 文件，其中包含了适用于这些系统的依赖库版本信息。**

### 🚀 主要更新

- 新增 浮窗可选显示“图标+文字”“图标”“文字”
- 新增 浮窗可选透明度(0-100%)
- 新增 悬浮窗靠近边缘自动贴边隐藏功能

### 💡 功能优化

- 优化 抽取多个学生/奖品会导致之间存在间距过小的问题
- 优化 给闪抽新增图标
- 优化 将单词PK功能默认关闭

### 🐛 修复问题

- 修复 无法使用密钥文件进行解锁
- 修复 更新窗口点击的下载链接没有直接调整到下载页面的问题
- 修复 主界面控制面板（抽奖 抽人）的位置开关文字内容问题
- 修复 主窗口莫名其妙的嵌套问题
- 修复 重启后不能使用密码打开设置界面的问题
- 修复 无法使用exFAT格式的U盘进行解锁
- 修复 浮窗初始显示功能默认应为"拖动+主界面"，却错误的默认设置为"拖动+闪抽"的问题

### 🔧 其它变更

- 已将 Github Actions 从 Windows 2022 迁移到 Windows 2025
- 已将 Python 版本从 3.8.10 迁移到 3.13.1
- 已将 Linux 相关打包代码 GitHub Action 中删除
- 已将 签名相关代码 GitHub Action 中删除
- 已将 制作安装包的代码 GitHub Action 中删除

### 🙏 贡献者 (排名不分先后)

<div align="left">

| 贡献者 | 贡献内容 | 贡献者 | 贡献内容 |
|:------:|:----------|:------:|:----------|
| <img src="https://github.com/SECTL/SecRandom/raw/main/app/resource/icon/contributor1.png" width="50px;" alt="lzy98276"/> <br> [**lzy98276**](https://github.com/lzy98276) | 🎨 设计 & 💡 创意 & 📋 策划 <br> 🔧 维护 & 📝 文档 & 🧪 测试 | <img src="https://github.com/SECTL/SecRandom/raw/main/app/resource/icon/contributor4.png" width="50px;" alt="yuanbenxin"/> <br> [**yuanbenxin**](https://github.com/yuanbenxin) | 🌐 响应式前端页面设计及维护 & 📝 文档 |
| <img src="https://github.com/SECTL/SecRandom/raw/main/app/resource/icon/contributor2.png" width="50px;" alt="QiKeZhiCao"/> <br> [**QiKeZhiCao**](https://github.com/QiKeZhiCao) | 💡 创意 & 🔧 维护 | <img src="https://github.com/SECTL/SecRandom/raw/main/app/resource/icon/contributor5.png" width="50px;" alt="zhangjianjian7"/> <br> [**zhangjianjian7**](https://github.com/zhangjianjian7) | 📝 文档 |
| <img src="https://github.com/SECTL/SecRandom/raw/main/app/resource/icon/contributor3.png" width="50px;" alt="Fox-block-offcial"/> <br> [**Fox-block-offcial**](https://github.com/Fox-block-offcial) | 🧪 应用测试 & 📝 文档 & 📦 安装包制作 | <img src="https://github.com/SECTL/SecRandom/raw/main/app/resource/icon/contributor6.png" width="50px;" alt="Jursin"/> <br> [**Jursin**](https://github.com/Jursin) | 🌐 响应式前端页面设计及维护 & 📝 文档 |
</div>

---

💝 **感谢所有贡献者为 SecRandom 项目付出的努力！**
Full Changelog: [v1.2.2.3...v1.2.5.0](https://github.com/SECTL/SecRandom/compare/v1.2.2.3...v1.2.5.0)

**国内 下载链接**
| 平台/打包方式 | 支持架构 | 完整版 |
| --- | --- | --- |
| Windows | x64 | [下载](https://www.123684.com/s/9529jv-U4Fxh) |

**Github 镜像 下载链接**
| 镜像源 | 平台/打包方式 | 支持架构 | 完整版 |
| --- | --- | --- |
| ghfast.top | Windows 目录模式 | x64 | [下载 v1.2.5.0](https://ghfast.top/https://github.com/SECTL/SecRandom/releases/download/v1.2.5.0/SecRandom-Windows-v1.2.5.0-x64-dir.zip) |
| gh-proxy.com | Windows 目录模式 | x64 | [下载 v1.2.5.0](https://gh-proxy.com/https://github.com/SECTL/SecRandom/releases/download/v1.2.5.0/SecRandom-Windows-v1.2.5.0-x64-dir.zip) |

**SHA256 校验值-请核对下载的文件的SHA256值是否正确**
| 文件名 | SHA256 值 |
| --- | --- |
|  |  |
| SHA256SUMS.txt | 01ba4719c80b6fe911b091a7c05124b64eeece964e09c058ef8f9805daca546b |
| SecRandom-Windows-v1.2.5.0-x64-dir.zip | 183a97c8ff620f1494dee77cdb30b6f15920289897e071bc9ce0e399d2ae4854 |
