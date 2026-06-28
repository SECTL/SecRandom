### 🚀 主要更新

> v1.1.2.0 更新日志

- 新增 历史记录中增加“全班同学”/“奖品记录”可按照时间顺序查看
- 新增 自动保存窗口大小,将在下次启动应用自动恢复(无法保存最大化)
- 新增 可设置播报语音的语速、音量
- 新增 可设置播报结果前可自动调节系统音量大小
- 新增 可设置是否在抽取小组组号时显示随机成员
- 新增 在应用启动时检查更新并提醒(可以设置是否提醒)
- 新增 可在关于界面进行手动检查更新
- 新增 更新提醒可直接打开对应的下载方式进行下载
- 新增 3位贡献者(主要是想法、测试、网站、文档这方面)
- 新增 可以配置过期历史记录过期天数,超过过期天数的历史记录将被删除
- 新增 可选择更新通道
- 新增 导入名单功能（支持从Excel CSV文件导入名单；适配NamePicker的名单文件-可自动转化性别为中文）
- 新增 可设置浮窗样式(经典版/简洁版)
- 新增 可设置抽取时/结果出现时播放音乐/音量大小/渐入渐出(支持格式)
- 新增 可修改抽人的学生显示格式(姓名/学号/学号+姓名)
- 新增 可修改抽奖的奖品显示格式(奖品/序号/序号+奖品)
- 新增 可设置抽选时的字体颜色(随机颜色/固定颜色)
- 新增 可在编辑名单(抽人/抽奖)时,直接修改性别/小组(抽奖:可直接修改抽奖的奖品/权重)
- 新增 关于界面增加官网链接
- 新增 可切换语音引擎(系统TTS/Edge_TTS)
- 新增 历史记录加载中的提示

> v1.1.3.0-beta 更新日志

- 新增 抽单人时使用双行格式显示 [Issue #4](https://github.com/SECTL/SecRandom/issues/4)
- 新增 插件管理(预览版)
- 新增 引导界面(仅在没有设置文件夹会出现)
- 新增 捐献支持(关于界面)
- 新增 图片显示(没图片打开该功能是显示第一个字)(抽取时显示) [Issue #5](https://github.com/SECTL/SecRandom/issues/5)
- 新增 插件广场(预览版)
- 新增 插件设置(预览版)
- 新增 主窗口置顶(需重新打开主窗口生效-不是重启软件)
- 新增 重启功能(仅支持目录模式)

> v1.1.4.0-beta 更新日志

- 新增 更新日志界面，方便用户了解版本更新内容
- 新增 MD5校验功能，检验捐献支持二维码是否被篡改
- 新增 导出诊断数据功能，方便用户导出软件运行数据
- 新增 导出抽人抽奖名单功能，方便用户导出抽人抽奖名单
- 新增 导入导出设置功能，不能导出密码等安全性设置
- 新增 URL功能，方便其它软件调用SecRadnom的功能

> v1.2.0.0 更新日志

- 新增 可设置主界面是否显示设置图标(默认打开)
- 新增 可设置主界面的控制面板的位置(默认在左侧)
- 新增 浮窗中的闪抽功能
- 新增 可设置浮窗的布局(矩形/竖向/横向)
- 新增 根据主题自动切换浮窗的背景颜色
- 新增 可设置闪抽窗口的关闭时间(默认3秒)
- 新增 可设置前台应用感知浮窗控制(支持类名、标题、进程)(默认不检测)
- 新增 给闪抽界面增加URL参数(可以直接打开闪抽界面进行抽取-详情请看文档)

### 💡 功能优化

> v1.1.2.0-beta 更新日志

- 优化 抽取界面的按钮内容与实际理解的偏差
- 优化 部分弹窗在深色模式下界面背景为白色的Bug
- 优化 输入密码或2FA后可以回车确认
- 优化 更新弹窗的配色和动画
- 优化 抽人/抽奖字体设置的操作方式
- 优化 贡献者界面(支持自动排列)
- 优化 部分二级界面的主题下的配色以及标题栏
- 优化 焦点的性能问题(导致cpu占用过高)
- 优化 滑动块的库使用(从QScrollArea替换为SingleDirectionScrollArea)
- 优化 将重置抽取名单会刷新名单功能去除
- 优化 将抽人及其抽奖的语音相关设置移动至语音设置界面
- 优化 播报队列的大小进行适当的调整

> v1.1.3.0-beta 更新日志

- 优化 历史记录加载方式

> v1.1.4.0-beta 更新日志

- 优化 引导流程，区分首次使用和版本更新情况
- 优化 引导窗口、更新日志窗口中加入滚动区域

> v1.2.0.0 更新日志

- 优化 抽人、抽奖名单的按钮文字内容问题
- 优化 当更新日志界面中某个部分的更新日志为空时，不显示该部分的标题
- 优化 软件全部跟路径有关的代码，改为使用绝对路径

### 🐛 修复问题

> v1.1.2.0-beta 更新日志

- 修复 右键点击会导致软件卡退的Bug
- 修复 字体设置成功后重开应用导致误弹错误提示
- 修复 部分情况下关闭窗口导致的应用退出 
- 修复 浮窗长时间不点击后无法置顶的问题 
- 修复 多次点击检查更新会导致软件卡退的问题
- 修复 鼠标拖动浮窗不在原位置的问题
- 修复 贡献者卡片高度不一致的问题
- 修复 浮窗右键或长按导致程序异常退出
- 修复 在抽取界面抽取动画中学生姓名与学号不匹配的显示问题
- 修复 在刷新后,性别下拉框中的内容变动而导致的软件卡退问题 [Issue #3](https://github.com/SECTL/SecRandom/issues/3)
- 修复 无法删除带有【】的学生姓名的问题
- 修复 人数显示无法修改格式问题
- 修复 奖品量显示无法修改问题
- 修复 不重复抽取抽完一轮后再抽一次(不管抽多少人)的都不会纳入已抽取名单
- 修复 Edge_tts的语音播报重复和漏报问题
- 修复 历史记录中公平抽取是否开启都显示权重/概率的问题

> v1.1.4.0-beta 更新日志

- 修复 不开图片模式，字体显示异常的问题
- 修复 不开图片模式，控件不居中的问题
- 修复 插件管理界面自启动按钮问题
- 修复 插件广场界面卸载插件时定位错误导致误卸载其他插件的问题
- 修复 引导窗口关闭时,主窗口不启动的问题
- 修复 引导窗口字体太大,导致内容看不全的问题
- 修复 缩减插件广场的插件信息
- 修复 历史记录界面，加载数据时，界面通知飞了一下的问题
- 修复 退出验证密码开关取消后状态异常
- 修复 2FA设置未验证即写入密钥
- 修复 因为SSL的原因无法正确下载捐献支持二维码的问题

> v1.2.0.0 更新日志

- 修复 点击刷新按钮后，导致软件卡退
- 修复 历史记录没有做完的问题
- 修复 当修改主窗口检测焦点的相关设置之后导致程序无法打开的问题
- 修复 重置抽人之后剩余人数没有改变的问题
- 修复 其它应用启动URL遇到应用启动目录改变的问题
- 修复 插件获取超时时间问题(改为默认为关闭状态)

### 🔧 其它变更

> v1.1.3.0-beta 更新日志

- 去除 删除了抽取界面的部分功能按钮的显示隐藏功能

> v1.1.4.0-beta 更新日志

- 去除 数字人民币捐赠功能
- 修改 更新弹窗链接换为SecRandom官网

### 🙏 贡献者 (排名不分先后)

<div align="left">

| 贡献者 | 贡献内容 | 贡献者 | 贡献内容 |
|:------:|:----------|:------:|:----------|
| <img src="app/resource/icon/contributor1.png" width="50px;" alt="lzy98276"/> <br> [**lzy98276**](https://github.com/lzy98276) | 🎨 设计 & 💡 创意 & 📋 策划 <br> 🔧 维护 & 📝 文档 & 🧪 测试 | <img src="app/resource/icon/contributor4.png" width="50px;" alt="yuanbenxin"/> <br> [**yuanbenxin**](https://github.com/yuanbenxin) | 🌐 响应式前端页面设计及维护 & 📝 文档 |
| <img src="app/resource/icon/contributor2.png" width="50px;" alt="QiKeZhiCao"/> <br> [**QiKeZhiCao**](https://github.com/QiKeZhiCao) | 💡 创意 & 🔧 维护 | <img src="app/resource/icon/contributor5.png" width="50px;" alt="zhangjianjian7"/> <br> [**zhangjianjian7**](https://github.com/zhangjianjian7) | 📝 文档 |
| <img src="app/resource/icon/contributor3.png" width="50px;" alt="Fox-block-offcial"/> <br> [**Fox-block-offcial**](https://github.com/Fox-block-offcial) | 🧪 应用测试 | <img src="app/resource/icon/contributor6.png" width="50px;" alt="Jursin"/> <br> [**Jursin**](https://github.com/Jursin) | 🌐 响应式前端页面设计及维护 & 📝 文档 |

</div>

---

💝 **感谢所有贡献者为 SecRandom 项目付出的努力！**

## 下载SecRandom怎么选择文件
- 可前往[SecRandom README 查看](https://github.com/SECTL/SecRandom?tab=readme-ov-file#%E4%B8%8B%E8%BD%BDsecrandom%E6%80%8E%E4%B9%88%E9%80%89%E6%8B%A9%E6%96%87%E4%BB%B6)
Full Changelog: [v1.1.4.0-beta...v1.2.0.0](https://github.com/SECTL/SecRandom/compare/v1.1.4.0-beta...v1.2.0.0)

**国内 下载链接**
| 平台/打包方式 | 支持架构 | 完整版 |
| --- | --- | --- |
| Windows | x86, x64 | [下载](https://www.123684.com/s/9529jv-U4Fxh) |

**Github 镜像 下载链接**
| 镜像源 | 平台/打包方式 | 支持架构 | 完整版 |
| --- | --- | --- | --- |
| ghfast.top | Windows 目录模式 | x86 | [下载 v1.2.0.0](https://ghfast.top/https://github.com/SECTL/SecRandom/releases/download/v1.2.0.0/SecRandom-Windows-v1.2.0.0-x86-dir.zip) |
| ghfast.top | Windows 目录模式 | x64 | [下载 v1.2.0.0](https://ghfast.top/https://github.com/SECTL/SecRandom/releases/download/v1.2.0.0/SecRandom-Windows-v1.2.0.0-x64-dir.zip) |
| gh-proxy.com | Windows 目录模式 | x86 | [下载 v1.2.0.0](https://gh-proxy.com/https://github.com/SECTL/SecRandom/releases/download/v1.2.0.0/SecRandom-Windows-v1.2.0.0-x86-dir.zip) |
| gh-proxy.com | Windows 目录模式 | x64 | [下载 v1.2.0.0](https://gh-proxy.com/https://github.com/SECTL/SecRandom/releases/download/v1.2.0.0/SecRandom-Windows-v1.2.0.0-x64-dir.zip) |

**SHA256 校验值-请核对下载的文件的SHA256值是否正确**
| 文件名 | SHA256 值 |
| --- | --- |
|  |  |
| SHA256SUMS.txt | 01ba4719c80b6fe911b091a7c05124b64eeece964e09c058ef8f9805daca546b |
| SecRandom-Windows-v1.2.0.0-x64-dir.zip | 1a7e1efdd2f60423ad9bb20fd7dcc0f7df5c59d784d26bea6abb3677f858b45b |
| SecRandom-Windows-v1.2.0.0-x86-dir.zip | 6a8a6ee6e89a68580e0aab945dd5370ec50f51be9f840acfb502bdea86283d0b |
