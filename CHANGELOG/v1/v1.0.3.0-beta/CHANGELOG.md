> [!note]
> 
> **SecRandom v1.0.3.0-beta 版本起存储名单采用json格式**
>
> **旧版名单存储采用ini格式，更新至此版本需要重新添加名单(建议不替换文件进行安装-删了旧版重新弄)**
>
> **不了解公平抽取可看[SecRandom-README](https://github.com/SECTL/SecRandom)**

> [!warning]
> 注意：
> 更新至此版本您需要使用以下步骤将旧版本的名单迁移到新版本：
> 1. 重新在表格中复制后添加名单
> 2. 确保可用后，删除旧版本的SecRandom程序文件

### 🚀 新增功能与优化
 - 【图标】更换图标为 [fluentui-system-icons](https://github.com/microsoft/fluentui-system-icons)
 - 【抽人】新增性别功能
 - 【抽人】将所有与抽人(抽学生)相关功能集成在一个页面中
 - 【抽人】增加新的3种-共4种(原1种不可更改的：Random方法-可预测)可选择的抽取方式： 
 -  1.可预测抽取(Random方法-可预测)
 -  2.不可预测抽取(SystemRandom方法-小于1000人无影响性能，不可预测)
 -  3.公平可预测抽取(Random方法-可预测)
 -  4.公平不可预测抽取(SystemRandom方法-小于1000人无影响性能，不可预测)
 - 【抽人】优化了代码部分内容(就是将三个抽取模式合并为一块了...)
 - 【抽人-设置】可单独设置抽人界面的所有功能卡的显示与隐藏
 - 【抽人】调整抽人页面布局
 - 【历史记录】在查看个人的历史纪录时，新增可查看抽取时设置的人数、抽取时选择的小组以及性别
 - 【历史记录】在查看全部学生历史记录时，抽取次数仅显示总次数(去除了抽单人、多人、小组次数的单独显示)
 - 【历史记录】在查看全部学生历史记录且抽取方式:公平可预测抽取 或 公平不可预测抽取，将显示下次抽取概率(将权重转化为概率显示)
 - 【重启】新增重启功能
 - 【名单导入】新版名单存储采用json,更新至此版本需要重新添加名单
### 🐛 修复
 - 修复一些抽人时的小bug
 - 修复抽人时，若选取小组/性别名单为空，会卡退的bug
 - 修复点击托盘关于后会导致程序退出的情况
### ❌ 移除
 - 【主题】暂时移除了软件变更主题颜色的功能

### 🎉 软件仓库日志-仅显示最近5条
 - SecRandom v1.0.3.0-beta 发布 - 2025-05-25 - 处于 1.1-dev测试版本
 - SecRandom 将在 1.2-dev 测试版本迁移至 RinUI --励志的目标? - 2025-05-05
 - SecRandom v1.0.2.3-beta 发布 - 2025-05-05 - 处于 1.1-dev测试版本
 - SecRandom v1.0.2.2-beta 发布 - 2025-05-03 - 处于 1.1-dev测试版本

Full Changelog: [v1.0.2.3-beta...v1.0.3.0-beta](https://github.com/SECTL/SecRandom/compare/v1.0.2.3-beta...v1.0.3.0-beta)

**国内 下载链接**
| 平台/打包方式 | 支持架构 | 完整版 |
| --- | --- | --- |
| Windows | x86, x64 | [下载](https://www.123684.com/s/9529jv-U4Fxh) |

**Github 镜像 下载链接**
| 镜像源 | 平台/打包方式 | 支持架构 | 完整版 |
| --- | --- | --- | --- |
| ghfast.top | Windows | x86 | [下载](https://ghfast.top/https://github.com/SECTL/SecRandom/releases/download/v1.0.3.0-beta/SecRandom-Windows-x86.zip) |
| ghfast.top | Windows | x64 | [下载](https://ghfast.top/https://github.com/SECTL/SecRandom/releases/download/v1.0.3.0-beta/SecRandom-Windows-x64.zip) |
| gh-proxy.com | Windows | x86 | [下载](https://gh-proxy.com/https://github.com/SECTL/SecRandom/releases/download/v1.0.3.0-beta/SecRandom-Windows-x86.zip) |
| gh-proxy.com | Windows | x64 | [下载](https://gh-proxy.com/https://github.com/SECTL/SecRandom/releases/download/v1.0.3.0-beta/SecRandom-Windows-x64.zip) |

SHA256 校验值-**请核对下载的文件的SHA256值是否正确**
| 文件名 | SHA256 值 |
| --- | --- |
| SecRandom-Windows-x64.zip | 6a25e028537dcb0836e8d77411903d18b5105416a442798c05d25a625c480175 |
| SecRandom-Windows-x86.zip | 4f869c03ac9792cb00674eaca992252ae2b4d88358085b65f90976d00f7e2505 |
