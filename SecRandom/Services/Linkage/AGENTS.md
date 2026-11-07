# SecRandom/Services/Linkage/ AGENTS.md

<!--
Supplement to ../../AGENTS.md. Update this file when a source, the course-state
runtime, the draw gate, the restriction notice, or the pre-class reset changes.
-->

## OVERVIEW

课程联动运行层：CSES 课表解析/存储、ClassIsland IPC 适配、课程边界调度、
抽取授权闸门、课前重置。**课间与课下的抽取禁用就在这一层判定**，页面只问
`LinkageDrawCoordinator`，不自己读课表状态。

## STRUCTURE

```
SecRandom/Services/Linkage/
|-- ICourseScheduleSource.cs        # 课表源契约：快照 + StateChanged
|-- CourseScheduleMath.cs           # 纯函数：按课表算当前/上节/下节与倒计时
|-- CsesScheduleParser.cs           # CSES YAML -> 课程列表（纯解析）
|-- CsesScheduleStore.cs            # data/CSES/cses_schedule.yml 的读写与校验
|-- CsesScheduleSource.cs           # CSES 源：存储变更 -> 快照
|-- ICsesScheduleStore.cs           # CSES 存储契约（页面只认这个）
|-- ClassIslandIpcConnection.cs     # 唯一 IPC 管道：课程查询 + 上下课通知 + 重连退避
|-- ClassIslandScheduleSource.cs    # ClassIsland -> CourseScheduleSnapshot
|-- CourseLinkageService.cs         # 单例状态：快照、闸门判定、刷新调度、科目过滤
|-- CourseLinkageHostedService.cs   # 唯一课程边界调度器 + 课前重置执行
|-- LinkageDrawCoordinator.cs       # 抽取授权闸门（本地/协议/快捷键共用）
|-- ClassTimeDrawRestrictionNotice.cs # 课间被拦时的本地化 toast（5 秒节流）
```

## WHERE TO LOOK

| Task | Location | Notes |
|------|----------|-------|
| 课间/课下禁令是否在生效 | `CourseLinkageService.IsConfirmedNonClassTime` | `InstantDrawDisable` 开 **且** `IsConfirmedBreakTime`（快照确认 `CourseTimeState.Breaking`、且不在课前解禁/课后延迟窗口内）。 |
| 数据源是否给不出判定 | `CourseScheduleSnapshot.IsAvailable` / `Unavailable(...)` | ClassIsland 断连、计时器停、课表未加载、CSES 课表缺失都会返回 `IsAvailable == false` 的快照；`IsConfirmedBreakTime` 因此恒为假，禁令自动放行。 |
| 为什么点不动 | `LinkageDrawCoordinator.AuthorizeAsync` | `VerificationRequired == false` 时课间是硬拒绝，拒绝前调 `ClassTimeDrawRestrictionNotice.Show(snapshot)` 发提示。 |
| 远程命令的判定 | `LinkageDrawCoordinator.EvaluateGate` | 无交互判定，**刻意不弹提示**：教室机可能无人，弹窗会让远程命令挂死。`BlockedByClassTime` 由 `ControlDrawTriggerHandler` 转成 `denied(blocked_by_class_time)`。 |
| 上下课事件 | `ClassIslandIpcConnection` | 订阅 `OnClassNotifyId` / `OnBreakingTimeNotifyId` / `OnAfterSchoolNotifyId`，`StateChanged` → 两个源 → `CourseLinkageService.RefreshAsync`。 |
| 课前重置 | `CourseLinkageHostedService.TryPerformPreClassReset` | `IsPreClassResetDue` 给出的 key 去重，清临时记录并让三个页面 `ResetForCourseLinkage()`。 |
| 提示节流 | `ClassTimeDrawRestrictionNotice.ThrottleWindow` | 5 秒一次，连点按钮不刷屏；`MainView.Current` 为空时静默跳过。 |

## CONVENTIONS

- 上课/下课/课间/放学的时间判定只在 `CourseScheduleMath`（CSES）与
  `ClassIslandScheduleSource`（IPC 状态映射）里做，别在调用点重算时间窗。
- 课间禁令的开关是 `LinkageSettingsConfig.InstantDrawDisable`（默认 `false`），
  与之配套的 `VerificationRequired`（默认 `true`）决定课间是"验证后放行"还是"直接阻止"。
  新增开关一律默认关闭，并在 `LinkageSettingsPage` 与三语资源同步暴露。
- 数据源不可用（`CourseScheduleSnapshot.Unavailable`）必须放行。断连不是下课，
  把不可用当禁课会让人在教室外白等一整天——这条在 issue 里被反复踩过。
- 用户可见的拦截文案放 `SecRandom/Langs/SettingsPages/Linkage/Resources*.resx`：
  `M_ClassTimeDrawBlocked`（带下一节课与分钟数）与 `M_ClassTimeDrawBlockedGeneric`。
  取值走 `Resources.ResourceManager.GetString(key, Resources.Culture)`，不要手改
  `Resources.Designer.cs`。
- 快照比较只看语义字段（`SnapshotsEqual`），排除倒计时，否则每次刷新都会自激（issue #274）。
- ClassIsland 类型只允许出现在 `ClassIslandScheduleSource` 与 `ClassIslandIpcConnection`；
  其他文件接触它就是把 IPC 依赖漏进业务层。

## ANTI-PATTERNS

- 不要在页面 ViewModel 里写 `if (在课间) return;`：三个抽取入口（点名/即时抽取/抽奖）
  已经全部经过 `LinkageDrawCoordinator`，散写判断迟早漏掉协议与快捷键路径。
- 不要用 `EvaluateGate` 代替 `AuthorizeAsync` 执行抽取：它只回答"会不会弹验证框"。
- 不要因为 `PeerConnectionBroken` 就把状态钉成 `Breaking` 或永久禁用；重连退避是
  5 秒到 5 分钟，期间的快照必须是 `Unavailable`。
- 不要在这里直接调平台窗口 API（浮窗隐藏归浮窗服务，见 ../../AGENTS.md）。
