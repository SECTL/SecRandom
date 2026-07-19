# MVE 共享视图迁移 + 移动端补全总计划

依据：4 份侦察报告（MVE 路由 / 抽取栈 / 移动 UI / 共享服务缺口），结论文件见会话工具存档。
已完成：Android/iOS 入口拆分（SecRandom.Mobile.Shared + SecRandom.Android + SecRandom.iOS）。

## Stage 1 — 基础层（6 并行，无页面改动）
1. **Worker_DrawCommit**：统一 DrawRoundId + IDrawCommitService 事务式提交 + 原子落盘 + 重复阈值/候选过滤收敛 + 6 处调用点切换 + 测试。
2. **Worker_MVEFoundation**：ViewEngine host 销毁复用防护、桌面关窗清理竞争修复、Frame↔MVE 命中测试互斥 + 测试。
3. **Worker_CoreServices**：IHistoryQueryService/IProfileCatalog 扩容、RosterImportParser 下沉、桌面历史页/名单页绕过点收敛 + 测试。
4. **Worker_ArchiveCore**：Core 共享 DataArchiveService + IArchivePostImportHooks，桌面 ImportExportService 重构为薄壳 + 测试。
5. **Worker_TelemetrySeam**：ISpan 泄漏抽象化（ITelemetryTransaction），桌面行为不变；产出移动端接入片段（不接线）。
6. **Worker_MobileUIFoundation**：MobileStyles.axaml 资源字典、主题 DynamicResource 化、5+ 可复用控件、设置页基类、**动画原语（页面过渡/结果揭示/状态切换）**、GroupName bug、死代码、字体决策、能力投影 helper。

> 用户补充：移动端需完成**全部页面与相关逻辑**（多人抽取、历史管理、名单导入、备份、Sentry、语言、能力投影等）以及**动画**（FluentAvalonia 风格的轻量过渡与结果动效，非桌面重型滚动动画）。

## Stage 2 — 桌面页面 MVE 化 + 移动主页/运行时（4 并行）
7. **Worker_MVEMainPages**：RollCall/Lottery → ViewBase + AddView + MainView embedded 路由集合。
8. **Worker_MVESettingsPages**：全部 settings 页 → ViewBase + desktop.settings host 路由 + 返回栈/预览冻结/搜索定位联动。
9. **Worker_MobileSentry**：移动 Sentry 包 + adapter（用 Stage1 seam）+ DI + Android 未处理异常钩子 + 隐私页开关。
10. **Worker_MobileMainPages**：抽取/历史/概览页用新组件库重构 + Lottery/iOS 更新能力投影 + 结果过渡动效。

## Stage 3 — 移动设置页 + 备份落地（2 串行→并行）
11. **Worker_MobileSettingsPages**：8 个设置页全面重构（目录/通用/个性化/名单/抽取/更新/关于）+ 备份页真实实现（Core archive + StorageProvider 导出导入）+ 语言选择行。
12. **Worker_MobileLanguage**：en-US/ja-JP resx 翻译 + MobileApp culture 初始化 + 硬编码中文清扫 + 切换免重启重建。

## Stage 4 — 收尾
13. **Worker_FrameRemoval**：拆除旧 FAFrame 双轨与 keyed 页面 DI；AGENTS.md 全量同步。
14. **Worker_FinalValidation**：全量构建 + Core 测试 + Android 头构建 + 验收清单核对。

依赖：Stage 2 依赖 Stage 1 全部完成；Stage 3 的 12 依赖 11；Stage 4 依赖前面全部。
