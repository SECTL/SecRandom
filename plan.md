# MVE 共享视图迁移 + 移动端补全总计划

依据：4 份侦察报告（MVE 路由 / 抽取栈 / 移动 UI / 共享服务缺口），结论文件见会话工具存档。

**状态：✅ 全部阶段完成（2026-07-20）。**
**最终验证：全量构建 0 错误；Core 测试 310/310 通过；Android Debug 构建通过。**

已完成：Android/iOS 入口拆分（SecRandom.Mobile.Shared + SecRandom.Android + SecRandom.iOS，`BuildMobile=true` 启用头 TFM，`MobileTargetFramework` 已删除，`IMobileUpdateInstaller` seam）。

## Stage 1 — 基础层 ✅ 全部完成
1. ✅ **Worker_DrawCommit**：`IDrawCommitService`/`DrawCommitCoordinator` 落地（单一 DrawRoundId 可外部传入、先临时后历史、失败快照补偿回滚、`_gate` 串行化）；`IProfileService.Record*` 增加可选 `drawRoundId`（`RecordPrizeHistory` 另加 `drawMethod`）；`DrawRepeatPolicy`/`DrawCandidateFilter` 收敛约 15 份重复拷贝；`DrawTemporaryRecordService.SaveState` 与 `FileConfigService.SaveConfig` 原子替换落盘；桌面三 VM、`PluginDrawInvoker`、两个 Session 全部切协调器；插件学生抽取走 prepared 路径消除双重阈值；权重快照从 `VerificationDrawOutcome.FrozenWeights` 带出。遗留 TODO：proof 定序（attestation 延后到 commit 后）。
2. ✅ **Worker_MVEFoundation**：`IViewEngine.ShowExclusiveAsync(hostId, viewId)` 单栈排他导航；ViewEngine 复用分支 host 销毁自愈；`DesktopViewHostProvider.RegisterEmbeddedHost` 重注册自愈 + `Application.Current` null 防护；主/设置窗口 `Closing` 同步清理 embedded host（`e.Cancel` 防护）；MainView/SettingsView 路由判定改查 `IViewRegistry` + `SetEmbeddedMode` 显式互斥；ViewEngineTests 悬挂修复。
3. ✅ **Worker_CoreServices**：`IHistoryQueryService.LoadStudentHistory/LoadPrizeHistory` 快照；`IProfileCatalogManager`（含 `ClearStudentHistory`/`ClearPrizeHistory`）；`RosterImportParser` 下沉；桌面直构造/直文件操作绕过点收敛（OOBE Rename/Delete 与 MusicLibraryService 媒体库本体有意保留）。
4. ✅ **Worker_ArchiveCore**：`SecRandom.Core/Services/Archive/`（`DataArchiveService` + `IArchivePostImportHooks` + `DataTransferModels` + `ArchiveZipWriter`）；桌面 `ImportExportService` 薄壳 + `DesktopArchivePostImportHooks`；`AddCoreRuntimeServices` 注册 DataArchiveService + Null hooks，桌面 App 覆盖 hooks。
5. ✅ **Worker_TelemetrySeam**：`ITelemetryTransaction`（Sentry-free）+ `ITelemetrySdkAdapter.StartTransaction`；`TelemetryRuntimeService` 无 Sentry 引用；`GlobalConstants.SentryDsn`；桌面垫片 `TelemetryTransactionSentryExtensions`（移动端排除）。
6. ✅ **Worker_MobileUIFoundation**：`MobileStyles.axaml`（token + Light/Dark 主题字典 + ControlTheme）；`MobileTheme.BindBrush` DynamicResource 化；控件库 MobileCard/MobileSettingRow/MobileSegmentedControl/MobileEmptyState/MobileSectionHeader + `MobileSettingsPageBase`；`MobileAnimations` 动画原语（PageEnter/PlayResultReveal/StartNameRoll/CrossFade，可打断）；`MobileCapabilities`；`MobileDefaults`；系统字体决策（MiSans 78MB 不打包，DESIGN.md 已记录）；GroupName bug/死代码清理。

## Stage 2 — 桌面页面 MVE 化 + 移动主页/运行时 ✅ 全部完成
7. ✅ **Worker_MVEMainPages**：RollCallPage/LotteryPage → ViewBase + AddView 注册；MainView embedded 路由接入。
8. ✅ **Worker_MVESettingsPages**：全部 30 设置页 → ViewBase + AddView；设置页返回栈/预览冻结（冻 embedded host 父容器）/搜索定位接 MVE；页面关闭清理挂 `ViewBase.Closed` + 幂等 Detach；VM 单例语义不变。
9. ✅ **Worker_MobileSentry**：移动端经 csproj Compile Include 链接六个遥测文件 + `MobileProfilingIntegrationStub` + Sentry 6.6.0 包 + DI/Initialize/Shutdown + 隐私页 Sentry 开关 + Android/iOS 未处理异常钩子（Android `e.Handled=true`）。
10. ✅ **Worker_MobileMainPages**：抽取/历史/概览页组件化重构 + 动画；抽取四态状态机 + 800ms 滚动揭示；Lottery/iOS 更新能力投影。

## Stage 3 — 移动设置页 + 备份落地 ✅ 全部完成
11. ✅ **Worker_MobileSettingsPages**：8 个设置页全面重构（目录/通用/个性化/名单/抽取/备份/更新/关于）；备份页真实实现（Core `DataArchiveService` + StorageProvider 流式事务，v3 校验确认卡，SAF 兼容）；更新页 iOS 降级说明；关于页品牌卡 + 链接；语言选择行。
12. ✅ **Worker_MobileLanguage**：基座 resx 183 键 + en-US/ja-JP 全量变体（不注册 csproj，卫星程序集已验证产出）；硬编码中文清零；启动 `InitializeMobileLanguage`（Host 前直读 settings.json）；语言行免重启切换（`ApplyMobileCulture` + `ReloadRootViewAsync` 重建根视图回 main.rollCall）；`SingleViewHostProvider.Detach` 新增。

## Stage 4 — 收尾 ✅ 全部完成
13. ✅ **Worker_FrameRemoval / 文档同步**：内置页面 keyed DI 已切换为 `AddView<T>(pageId)` 注册；`AddMainPage`/`AddSettingsPage` 保留导航元数据；plugin `plugin.<id>.*` 页的 FAFrame 兜底双轨**有意保留**（两个 axaml 已加注释）；根 / SecRandom / SecRandom.Core AGENTS.md 全量同步。
14. ✅ **Worker_FinalValidation**：全量构建 0 错误；Core 测试 310/310 通过；Android Debug 构建通过；十大缺口验收核对结论见最终交付报告。

其它约定变更：`CsesScheduleException` 改为静态工厂抛 `InvalidDataException`（sealed 无法继承；错误码存 `Exception.Data`，UI 用 `TryGetError` 取回）——“课表错误 = InvalidDataException + Data 错误码”新约定。
