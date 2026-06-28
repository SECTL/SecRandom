> **⚠️ 重要提示：自v1.2.5.0 到 v1.3.5.0版本起，SecRandom 已停止对 Windows 7 和 x86 系统的官方支持。建议使用 Windows 10 x64 或更高版本系统以获得最佳体验。**
> 
> **注意：在 v1.3.5.0 版本之后，SecRandom 将会重新支持 Windows 7 及以上系统的 x86 和 x64 架构。**
> 
> **如急需在 Windows 7 或 x86 系统上使用本工具，可使用仓库根目录下的 requirements-windows-win7_x64_x86.txt 文件自行打包适配。**

### 🚀 主要更新

- 新增 可在上课前预设时间自动清除点名、抽奖页面内的学生名称标签和奖品名称标签
- 新增 可设置隔离点名、闪抽/即抽 已抽取记录
- 新增 可配置窗口背景（主窗口、闪抽/即抽窗口、设置窗口）颜色
- 新增 可配置 闪抽 默认选择名单
- 新增 可配置托盘选项显隐

### 💡 功能优化

- 无

### 🐛 修复问题

- 修复 调整最大不重复抽取次数后，剩余人数显示错误问题
- 修复 无法在上课前预设时间清除已抽取记录的问题
- 修复 设置 “抽取管理设置” 中，修改最后的设置组名称为 “闪抽/即抽窗口管理”
- 修复 闪抽/即抽设置中动画模式同步逻辑问题：将 “手动停止动画”（原值 0）和 “自动播放完整动画”（原值 1）统一调整为 “自动播放完整动画”（新值 0）；将 “直接显示结果”（原值 2）调整为 “直接显示结果”（新值 1）
- 修复 闪抽/即抽窗口 不会自动关闭的问题
- 修复 结果音乐不播放的问题

### 🔧 其它变更

- 无

💝 **感谢所有贡献者为 SecRandom 项目付出的努力！**
Full Changelog: [v1.2.8.7-beta...v1.2.9.9999-beta](https://github.com/SECTL/SecRandom/compare/v1.2.8.7-beta...v1.2.9.9999-beta)

**国内 下载链接**
| 平台/打包方式 | 支持架构 | 完整版 |
| --- | --- | --- |
| Windows | x64 | [下载](https://www.123684.com/s/9529jv-U4Fxh) |

**Github 镜像 下载链接**
| 镜像源 | 平台/打包方式 | 支持架构 | 完整版 |
| --- | --- | --- | --- |
| ghfast.top | Windows 目录模式 | x64 | [下载 v1.2.9.9999-beta](https://ghfast.top/https://github.com/SECTL/SecRandom/releases/download/v1.2.9.9999-beta/SecRandom-Windows-v1.2.9.9999-beta-x64-dir.zip) |
| gh-proxy.com | Windows 目录模式 | x64 | [下载 v1.2.9.9999-beta](https://gh-proxy.com/https://github.com/SECTL/SecRandom/releases/download/v1.2.9.9999-beta/SecRandom-Windows-v1.2.9.9999-beta-x64-dir.zip) |

**SHA256 校验值-请核对下载的文件的SHA256值是否正确**
| 文件名 | SHA256 值 |
| --- | --- |
|  |  |
| SHA256SUMS.txt | 01ba4719c80b6fe911b091a7c05124b64eeece964e09c058ef8f9805daca546b |
| SecRandom-Windows-v1.2.9.9999-beta-x64-dir.zip | a68b78446fcf3b87ef0054be666351f9359ab25f59f02411443deeb84fe5782f |
