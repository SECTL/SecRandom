using System.Globalization;
using System.Reflection;
using System.Resources;

namespace SecRandom.Core.Services.ControlNode;

/// <summary>
///     设置目录的类目标签、字段标签与说明：这台机器自己的设置页怎么说，控制台就怎么说。
/// </summary>
/// <remarks>
///     <para>
///         <b>为什么文案必须由客户端下发</b>：控制台渲染的是**这台机器**上的设置。文案如果由控制台自己维护，
///         同一条设置就会有两个说法，客户端改了措辞、或者哪天新增一项设置，控制台都不会跟着变。
///         因此文案的唯一来源是客户端自己的设置页资源
///         （<c>SecRandom/Langs/SettingsPages/**</c> 与 <c>SecRandom/Langs/Common</c>），
///         这里只负责"哪条设置对应哪个资源键"这一张对照表。
///     </para>
///     <para>
///         <b>为什么用反射读资源而不是编译期引用</b>：本地化资源住在应用层 <c>SecRandom</c> 程序集里，
///         而 Core 不能引用它——Core 同时被插件引用，插件运行在同一个进程，但没有、也不该有应用层的编译依赖。
///         所以这里按类型名解析应用程序集；解析不到时整体退化成下面的回退链，Core 单独使用时功能不会崩，
///         只是文案退化成英文属性名。
///     </para>
///     <para>
///         <b>回退链</b>（按顺序取第一个非空值）：
///         <list type="number">
///             <item><description><see cref="CultureInfo.CurrentUICulture" /> 对应的资源（日语设备 → 日语）。</description></item>
///             <item><description>英语资源。</description></item>
///             <item><description>中文基资源（<c>Resources.resx</c> 本体；没有对应语言卫星程序集时也落到这里）。</description></item>
///             <item><description>属性名/路径段转成的短英文标签（<c>voice_enable</c> → <c>Voice enable</c>）。</description></item>
///         </list>
///         <b>标签永远不会为空</b>：控制台拿到空串只会渲染出一行没有名字的设置，那比给一个能猜的英文名更糟。
///     </para>
///     <para>
///         说明（<c>description</c>）**没有**最后一级回退：说明宁可为空，也不能变成属性名的复述，
///         所以查不到就返回 <c>null</c>。资源标签的说明按设置页自己的约定取"标签键 + <c>_D</c>"。
///     </para>
///     <para>
///         每一条文案会下发**两份**：<c>label</c>/<c>description</c> 是按设备当前界面语言解析的那一份
///         （旧控制台只认它），<c>labels</c>/<c>descriptions</c> 是中英日三语各一份——控制台的界面语言
///         可以和这台设备不一样，一台中文设备面对英文管理员时，把设备语言的中文当成"这条设置的标签"是错的。
///         三语那份的键固定是 <c>zh-CN</c>/<c>en-US</c>/<c>ja-JP</c>，取不到的那一语不出现这个键，
///         一个键都取不到时整份映射是 <c>null</c>，控制台据此回落到设备语言的那一份。
///     </para>
///     <para>
///         这张表按**字段令牌**（路径最后一段，如 <c>half_repeat</c>）建索引，因为四个抽取设置页共用同一批
///         设置项：一条 <c>half_repeat</c> 同时覆盖默认/点名/闪抽/抽奖四个类目。路径级条目用于少数
///         **同名不同义**的字段（<c>algorithm_id</c>、<c>mode</c>、通知渠道的 <c>enabled</c>/<c>display_duration</c>），
///         它优先于令牌级条目。
///     </para>
///     <para>
///         对照的书写口径与设置页一致：中文与说明都不使用句号分隔的完整句子，避免把设置页那种
///         "短标签 + 一句话" 的节奏带成说明书。
///     </para>
/// </remarks>
public static class ControlSettingsLabels
{
    // ---------------------------------------------------------------- 资源页（应用层资源类的短名）

    private const string Picking = "SettingsPages.Picking.Resources";
    private const string Voice = "SettingsPages.Voice.Resources";
    private const string MiMo = "SettingsPages.Voice.MiMoResources";
    private const string FloatingWindow = "SettingsPages.FloatingWindow.Resources";
    private const string Notification = "SettingsPages.Notification.Resources";
    private const string Linkage = "SettingsPages.Linkage.Resources";
    private const string History = "SettingsPages.HistoryManagement.Resources";
    private const string Update = "SettingsPages.Update.Resources";
    private const string More = "SettingsPages.More.Resources";
    private const string Appearance = "SettingsPages.Personalized.Appearance.Resources";
    private const string Basic = "SettingsPages.General.Basic.Resources";
    private const string Backup = "SettingsPages.General.Backup.Resources";
    private const string Privacy = "SettingsPages.General.Privacy.Resources";
    private const string Verification = "SettingsPages.General.Verification.Resources";
    private const string Common = "Common.Resources";

    /// <summary>用来反查应用程序集的锚点类型：任意一个设置页资源类都行，取一个不会改名的。</summary>
    private const string AppAssemblyAnchorType = "SecRandom.Langs.SettingsPages.Voice.Resources, SecRandom";

    // ---------------------------------------------------------------- 文化（回退链）

    private static readonly CultureInfo Japanese = CultureInfo.GetCultureInfo("ja-JP");
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    /// <summary>中文基资源：没有 zh 卫星程序集，请求 zh-CN 会直接落到 <c>Resources.resx</c> 本体。</summary>
    private static readonly CultureInfo Chinese = CultureInfo.GetCultureInfo("zh-CN");

    private static readonly CultureInfo[] JapaneseFallback = [Japanese, English, Chinese];
    private static readonly CultureInfo[] EnglishFallback = [English, Chinese];
    private static readonly CultureInfo[] ChineseFallback = [Chinese, English];

    /// <summary>
    ///     随目录一起下发的三语，以及**每一语自己的回退链**：控制台的界面语言可能和这台设备的不一样，
    ///     所以三种语言要一次给全，让它自己挑。键名就是语言标签，顺序固定（中 → 英 → 日），
    ///     与下面 <c>Lit</c>/<c>Text</c> 的书写口径一致。
    /// </summary>
    private static readonly (string Key, CultureInfo[] Fallback)[] PublishedLanguages =
    [
        ("zh-CN", ChineseFallback),
        ("en-US", EnglishFallback),
        ("ja-JP", JapaneseFallback)
    ];

    private static readonly Lazy<Assembly?> AppAssembly = new(ResolveAppAssembly);

    private static readonly Dictionary<string, ResourceManager?> ResourceManagers = new(StringComparer.Ordinal);

    private static readonly Lock ResourceGate = new();

    // ---------------------------------------------------------------- 类目标签

    /// <summary>
    ///     类目标签刻意写在这里而不是取页面标题：一个类目往往横跨多个设置页（<c>general</c> 就有
    ///     基本/隐私/备份/验证/性能/崩溃恢复六块），页面标题是那一页自己的名字，不是这一类设置的分类名。
    ///     这里取的是客户端分组导航里的说法。
    /// </summary>
    private static readonly Dictionary<string, LabelSpec> CategoryLabels = new(StringComparer.Ordinal)
    {
        ["float_position"] = Lit("悬浮窗位置", "Floating window position", "フローティングウィンドウ位置"),
        ["general"] = Lit("通用设置", "General settings", "一般設定"),
        ["appearance"] = Lit("外观", "Appearance", "外観"),
        ["fair_draw"] = Lit("公平抽取", "Fair draw", "公平抽選"),
        ["default_draw"] = Lit("默认抽取设置", "Default draw settings", "既定の抽選設定"),
        ["roll_call"] = Lit("点名设置", "Roll call settings", "点呼設定"),
        ["quick_draw"] = Lit("闪抽设置", "Quick draw settings", "クイック抽選設定"),
        ["lottery"] = Lit("抽奖设置", "Lottery settings", "抽選設定"),
        ["floating_window"] = Lit("悬浮窗", "Floating window", "フローティングウィンドウ"),
        ["notification"] = Lit("通知", "Notifications", "通知"),
        ["linkage"] = Lit("联动", "Linkage", "連携"),
        ["voice"] = Lit("语音", "Voice", "音声"),
        ["history"] = Lit("历史记录", "History", "履歴"),
        ["update"] = Lit("更新", "Updates", "更新"),
        ["more"] = Lit("更多设置", "More settings", "その他の設定")
    };

    // ---------------------------------------------------------------- 路径级条目（同名不同义）

    private static readonly Dictionary<string, FieldSpec> FieldsByPath = new(StringComparer.Ordinal)
    {
        // 三个抽取页各有自己的算法选择器，标签也必须跟着页面走。
        ["roll_call.algorithm_id"] = R(Picking, "S_RollCallAlgorithm"),
        ["quick_draw.algorithm_id"] = R(Picking, "S_QuickDrawAlgorithm"),
        ["lottery.algorithm_id"] = R(Picking, "S_LotteryAlgorithm"),

        // 抽奖的"抽取方式"是按奖盘还是按剩余数量，与点名页的随机/公平不是同一件事。
        ["lottery.draw_type"] = R(Picking, "S_LotteryDrawType"),

        // 通知类目只暴露渠道开关与显示时长，四个渠道各有一套文案。
        ["notification.default.enabled"] = R(Notification, "S_Default_Enabled"),
        ["notification.roll_call.enabled"] = R(Notification, "S_RollCall_Enabled"),
        ["notification.quick_draw.enabled"] = R(Notification, "S_QuickDraw_Enabled"),
        ["notification.lottery.enabled"] = R(Notification, "S_Lottery_Enabled"),
        ["notification.default.display_duration"] = R(Notification, "S_Default_DisplayDuration"),
        ["notification.roll_call.display_duration"] = R(Notification, "S_RollCall_DisplayDuration"),
        ["notification.quick_draw.display_duration"] = R(Notification, "S_QuickDraw_DisplayDuration"),
        ["notification.lottery.display_duration"] = R(Notification, "S_Lottery_DisplayDuration"),

        // 通用设置下面有两个 "mode"：崩溃恢复方式与可验证抽取模式，同名字不同事。
        ["general.crash_recovery.mode"] = R(Basic, "S_Behavior_CrashRecovery"),
        ["general.verification.mode"] = R(Verification, "S_VerificationMode"),

        // 证明留存属于验证页的"本地证明保留期限"，路径挂在 general.proof_retention 下。
        ["general.proof_retention.retention_days"] = R(Verification, "S_LocalProofRetention"),

        // 悬浮窗位置就是浮窗左上角的屏幕坐标。
        ["float_position.x"] = L(
            "横坐标", "Horizontal position", "横位置",
            "悬浮窗左上角相对屏幕左边缘的像素位置", "Pixel offset of the floating window's left edge", "フローティングウィンドウ左上の画面左端からのピクセル位置"),
        ["float_position.y"] = L(
            "纵坐标", "Vertical position", "縦位置",
            "悬浮窗左上角相对屏幕顶部的像素位置", "Pixel offset of the floating window's top edge", "フローティングウィンドウ左上の画面上端からのピクセル位置")
    };

    // ---------------------------------------------------------------- 令牌级条目（路径最后一段）

    private static readonly Dictionary<string, FieldSpec> FieldsByToken = new(StringComparer.Ordinal)
    {
        // ---------------- 通用：行为与性能（基本设置页）
        ["language"] = R(Basic, "S_Behavior_Language"),
        ["autostart"] = R(Basic, "S_Behavior_Autostart"),
        ["show_startup_window"] = R(Basic, "S_Behavior_ShowStartupWindow"),
        ["auto_save_window_size"] = R(Basic, "S_Behavior_AutoSaveWindowSize"),
        ["main_window_topmost_mode"] = R(Basic, "S_Behavior_MainWindowTopmostMode"),
        ["background_resident"] = R(Basic, "S_Behavior_BackgroundResident"),
        ["url_protocol"] = R(Basic, "S_Behavior_UrlProtocol"),
        ["disable_crashed_plugin"] = R(Basic, "S_Behavior_DisableCrashedPlugin"),
        ["low_spec_mode"] = R(Basic, "S_Performance_LowSpecMode"),

        // 窗口几何没有对应的设置行：它由"自动保存窗口大小"顺手记下来，控制台能看到的只是这块持久化状态。
        ["main_window_width"] = L(
            "主窗口宽度", "Main window width", "メインウィンドウの幅",
            "上次退出时主窗口的宽度（像素）", "Width of the main window when the app last closed, in pixels", "前回終了時のメインウィンドウの幅（ピクセル）"),
        ["main_window_height"] = L(
            "主窗口高度", "Main window height", "メインウィンドウの高さ",
            "上次退出时主窗口的高度（像素）", "Height of the main window when the app last closed, in pixels", "前回終了時のメインウィンドウの高さ（ピクセル）"),
        ["main_window_maximized"] = L(
            "主窗口最大化", "Main window maximized", "メインウィンドウの最大化",
            "上次退出时主窗口是否处于最大化状态", "Whether the main window was maximized when the app last closed", "前回終了時にメインウィンドウが最大化されていたか"),
        ["settings_window_width"] = L(
            "设置窗口宽度", "Settings window width", "設定ウィンドウの幅",
            "上次退出时设置窗口的宽度（像素）", "Width of the settings window when the app last closed, in pixels", "前回終了時の設定ウィンドウの幅（ピクセル）"),
        ["settings_window_height"] = L(
            "设置窗口高度", "Settings window height", "設定ウィンドウの高さ",
            "上次退出时设置窗口的高度（像素）", "Height of the settings window when the app last closed, in pixels", "前回終了時の設定ウィンドウの高さ（ピクセル）"),
        ["settings_window_maximized"] = L(
            "设置窗口最大化", "Settings window maximized", "設定ウィンドウの最大化",
            "上次退出时设置窗口是否处于最大化状态", "Whether the settings window was maximized when the app last closed", "前回終了時に設定ウィンドウが最大化されていたか"),

        // 引导完成标记与四份"已同意"版本：远程改它们等于替这台机器的主人按下"我已阅读并同意"。
        ["guide_completed"] = L(
            "已完成首次引导", "First-run guide completed", "初回ガイド完了",
            "首次运行引导是否已经走完；控制面不允许替用户勾选", "Whether the first-run guide has been completed; the control plane never answers it for the user", "初回ガイドが完了しているか。管理側から代理で完了にはできません"),
        ["accepted_eula_version"] = L(
            "已同意的用户协议版本", "Accepted terms version", "同意済み利用規約バージョン",
            "这台机器已确认过的用户协议版本号", "Version of the terms this device has acknowledged", "この端末が確認済みの利用規約バージョン"),
        ["accepted_privacy_policy_version"] = L(
            "已同意的隐私政策版本", "Accepted privacy policy version", "同意済みプライバシーポリシーバージョン",
            "这台机器已确认过的隐私政策版本号", "Version of the privacy policy this device has acknowledged", "この端末が確認済みのプライバシーポリシーバージョン"),
        ["accepted_gpl_version"] = L(
            "已同意的 GPL 版本", "Accepted GPL version", "同意済み GPL バージョン",
            "这台机器已确认过的 GPLv3 许可版本号", "Version of the GPLv3 notice this device has acknowledged", "この端末が確認済みの GPLv3 ライセンスバージョン"),
        ["accepted_verification_notice_version"] = L(
            "已同意的可验证抽取声明版本", "Accepted verification notice version", "同意済み検証案内バージョン",
            "这台机器已确认过的可验证抽取声明版本号", "Version of the verifiable-draw notice this device has acknowledged", "この端末が確認済みの検証可能抽選に関する案内のバージョン"),

        // ---------------- 通用：隐私
        ["sentry_telemetry_enabled"] = R(Privacy, "S_SentryTelemetry_Enabled"),
        ["online_status_mode"] = R(Privacy, "S_OnlineStatus_Mode"),

        // ---------------- 通用：备份（本地与云端两套内容选择共用同一批文案）
        ["auto_backup_enabled"] = R(Backup, "S_AutoBackup"),
        ["auto_backup_interval_days"] = R(Backup, "S_AutoBackup_IntervalDays"),
        ["auto_backup_max_count"] = R(Backup, "S_AutoBackup_MaxCount"),
        ["cloud_device_alias"] = R(Backup, "S_CloudDeviceAlias"),
        ["cloud_auto_backup_enabled"] = R(Backup, "S_CloudAutoBackup"),
        ["cloud_auto_backup_interval_days"] = R(Backup, "S_CloudAutoBackup_IntervalDays"),
        ["cloud_auto_backup_max_count"] = R(Backup, "S_CloudAutoBackup_MaxCount"),
        ["include_config"] = R(Backup, "S_Includes_Config"),
        ["include_list"] = R(Backup, "S_Includes_List"),
        ["include_history"] = R(Backup, "S_Includes_History"),
        ["include_proofs"] = R(Backup, "S_Includes_Proofs"),
        ["include_audio"] = R(Backup, "S_Includes_Audio"),
        ["include_cses"] = R(Backup, "S_Includes_Cses"),
        ["include_images"] = R(Backup, "S_Includes_Images"),
        ["include_logs"] = R(Backup, "S_Includes_Logs"),
        ["cloud_include_config"] = R(Backup, "S_Includes_Config"),
        ["cloud_include_list"] = R(Backup, "S_Includes_List"),
        ["cloud_include_history"] = R(Backup, "S_Includes_History"),
        ["cloud_include_proofs"] = R(Backup, "S_Includes_Proofs"),
        ["cloud_include_audio"] = R(Backup, "S_Includes_Audio"),
        ["cloud_include_cses"] = R(Backup, "S_Includes_Cses"),
        ["cloud_include_images"] = R(Backup, "S_Includes_Images"),

        // ---------------- 外观
        ["theme"] = R(Appearance, "S_Theme_Theme"),
        ["theme_color_mode"] = R(Appearance, "S_Theme_ThemeColor"),
        ["font"] = R(Appearance, "S_Font_FontFamily"),
        ["font_weight"] = R(Appearance, "S_Font_FontWeight"),

        // ---------------- 公平抽取（旧版公平抽取设置页已下线，页面资源仍留在 Langs/Common）
        ["fair_draw"] = L(
            "公平抽取", "Fair draw", "公平抽選",
            "开启后按历史抽取次数调整候选项权重；关闭后所有候选项等权", "Adjusts candidate weights by past draw counts; when off every candidate has the same weight", "過去の抽選回数に応じて候補の重みを調整します。無効にすると全候補が等しい重みになります"),
        ["fair_draw_group"] = R(Common, "Settings_FairPick_ByGroup") with
        {
            Description = Lit(
                "按分组平衡各分组的抽取次数", "Balances draw counts across groups", "グループごとの抽選回数を平準化します")
        },
        ["fair_draw_gender"] = R(Common, "Settings_FairPick_ByGender") with
        {
            Description = Lit(
                "按性别平衡各性别的抽取次数", "Balances draw counts across genders", "性別ごとの抽選回数を平準化します")
        },
        ["fair_draw_time"] = R(Common, "Settings_FairPick_ByTime") with
        {
            Description = Lit(
                "为兼容旧配置保留，当前公平抽取算法不使用该项", "Retained for older configurations; the current fair-draw algorithm does not use it", "旧設定との互換のために保持されています。現在の公平抽選アルゴリズムは使用しません")
        },
        ["frequency_function"] = L(
            "频次函数", "Frequency function", "頻度関数",
            "为兼容旧配置保留，当前公平抽取算法不使用该项", "Retained for older configurations; the current fair-draw algorithm does not use it", "旧設定との互換のために保持されています。現在の公平抽選アルゴリズムは使用しません"),
        ["frequency_weight"] = L(
            "频次权重", "Frequency weight", "頻度の重み",
            "为兼容旧配置保留，当前公平抽取算法不使用该项", "Retained for older configurations; the current fair-draw algorithm does not use it", "旧設定との互換のために保持されています。現在の公平抽選アルゴリズムは使用しません"),
        ["base_weight"] = L(
            "基础权重", "Base weight", "基本の重み",
            "不启用公平加权时每个候选项的权重", "Weight given to every candidate while fair weighting is off", "公平な重み付けが無効なときの各候補の重み"),
        ["min_weight"] = L(
            "最小权重", "Minimum weight", "最小の重み",
            "为兼容旧配置保留，当前公平抽取算法不使用该项", "Retained for older configurations; the current fair-draw algorithm does not use it", "旧設定との互換のために保持されています。現在の公平抽選アルゴリズムは使用しません"),
        ["max_weight"] = L(
            "最大权重", "Maximum weight", "最大の重み",
            "为兼容旧配置保留，当前公平抽取算法不使用该项", "Retained for older configurations; the current fair-draw algorithm does not use it", "旧設定との互換のために保持されています。現在の公平抽選アルゴリズムは使用しません"),
        ["group_weight"] = L(
            "分组权重", "Group weight", "グループの重み",
            "为兼容旧配置保留，当前公平抽取算法不使用该项", "Retained for older configurations; the current fair-draw algorithm does not use it", "旧設定との互換のために保持されています。現在の公平抽選アルゴリズムは使用しません"),
        ["gender_weight"] = L(
            "性别权重", "Gender weight", "性別の重み",
            "为兼容旧配置保留，当前公平抽取算法不使用该项", "Retained for older configurations; the current fair-draw algorithm does not use it", "旧設定との互換のために保持されています。現在の公平抽選アルゴリズムは使用しません"),
        ["time_weight"] = L(
            "时间权重", "Time weight", "時間の重み",
            "为兼容旧配置保留，当前公平抽取算法不使用该项", "Retained for older configurations; the current fair-draw algorithm does not use it", "旧設定との互換のために保持されています。現在の公平抽選アルゴリズムは使用しません"),
        ["min_pool_size"] = L(
            "最小候选池", "Minimum pool size", "最小候補数",
            "为兼容旧配置保留，当前公平抽取算法不使用该项", "Retained for older configurations; the current fair-draw algorithm does not use it", "旧設定との互換のために保持されています。現在の公平抽選アルゴリズムは使用しません"),
        ["shield_enabled"] = R(Common, "Settings_FairPick_Shield") with
        {
            Description = Lit(
                "抽中后的一段时间内不再进入候选池", "Keeps a drawn member out of the candidate pool for a while", "当選後しばらく候補プールに戻さないようにします")
        },
        ["shield_time"] = L(
            "屏蔽时长", "Shield duration", "ブロック時間",
            "抽中之后多久之内不再进入候选池", "How long a drawn member stays out of the candidate pool", "当選してから候補に戻らない時間の長さ"),
        ["shield_time_unit"] = L(
            "屏蔽时长单位", "Shield time unit", "ブロック時間の単位",
            "屏蔽时长按秒、分还是小时计", "Whether the shield duration is measured in seconds, minutes, or hours", "ブロック時間を秒・分・時間のどれで数えるか"),
        ["cold_start_enabled"] = R(Common, "Settings_FairPick_ColdStart") with
        {
            Description = Lit(
                "为兼容旧配置保留，当前公平抽取算法不使用该项", "Retained for older configurations; the current fair-draw algorithm does not use it", "旧設定との互換のために保持されています。現在の公平抽選アルゴリズムは使用しません")
        },
        ["cold_start_rounds"] = L(
            "冷启动轮数", "Cold-start rounds", "コールドスタート回数",
            "为兼容旧配置保留，当前公平抽取算法不使用该项", "Retained for older configurations; the current fair-draw algorithm does not use it", "旧設定との互換のために保持されています。現在の公平抽選アルゴリズムは使用しません"),

        // ---------------- 抽取设置（默认/点名/闪抽/抽奖四页共用）
        ["draw_mode"] = R(Picking, "S_DrawMode"),
        ["half_repeat"] = R(Picking, "S_HalfRepeat"),
        ["clear_record"] = R(Picking, "S_ClearRecord"),
        ["draw_type"] = R(Picking, "S_DrawType"),
        ["default_class"] = R(Picking, "S_DefaultClass"),
        ["default_pool"] = R(Picking, "S_DefaultPool"),
        ["disable_after_click"] = R(Picking, "S_DisableAfterClick"),
        ["use_global_font"] = R(Picking, "S_FontSource"),
        ["custom_font"] = R(Picking, "S_CustomFont"),
        ["font_size"] = R(Picking, "S_FontSize"),
        ["display_format"] = R(Picking, "S_DisplayFormat"),
        ["display_style"] = R(Picking, "S_DisplayStyle"),
        ["lottery_show_random"] = R(Picking, "S_LotteryShowRandom"),
        ["custom_lottery_show_random_format"] = R(Picking, "S_LotteryShowRandomFormat"),
        ["show_tags"] = R(Picking, "S_ShowTags"),
        ["show_weight_transparency"] = R(Picking, "S_ShowWeightTransparency"),
        ["reminder_text"] = R(Picking, "S_ReminderText"),
        ["reminder_font_size"] = R(Picking, "S_ReminderFontSize"),
        ["reminder_text_opacity"] = R(Picking, "S_ReminderTextOpacity"),
        ["animation"] = R(Picking, "S_Animation"),
        ["animation_interval"] = R(Picking, "S_AnimationInterval"),
        ["autoplay_count"] = R(Picking, "S_AutoplayCount"),
        ["animation_style"] = R(Picking, "S_AnimationStyle"),
        ["animation_color_theme"] = R(Picking, "S_ColorTheme"),
        ["student_image"] = R(Picking, "S_StudentImage"),
        ["student_image_position"] = R(Picking, "S_StudentImagePosition"),
        ["lottery_image"] = R(Picking, "S_LotteryImage"),
        ["lottery_image_position"] = R(Picking, "S_LotteryImagePosition"),
        ["animation_music"] = R(Picking, "S_AnimationMusic"),
        ["animation_music_loop"] = R(Picking, "S_AnimationMusicLoop"),
        ["animation_music_volume"] = R(Picking, "S_AnimationMusicVolume"),
        ["result_music"] = R(Picking, "S_ResultMusic"),
        ["result_music_volume"] = R(Picking, "S_ResultMusicVolume"),
        ["voice_announcement_enabled"] = R(Picking, "S_VoiceAnnouncementEnabled"),

        // 音乐淡入淡出在页面上一行两个滑块，协议里是两个独立的毫秒数，因此各给一句。
        ["animation_music_fade_in"] = L(
            "动画音乐淡入", "Animation music fade-in", "アニメーション音楽のフェードイン",
            "动画音乐开始播放时的渐入时间（毫秒）", "Fade-in time of the animation music, in milliseconds", "アニメーション音楽の再生開始時のフェードイン時間（ミリ秒）"),
        ["animation_music_fade_out"] = L(
            "动画音乐淡出", "Animation music fade-out", "アニメーション音楽のフェードアウト",
            "动画音乐停止播放时的渐出时间（毫秒）", "Fade-out time of the animation music, in milliseconds", "アニメーション音楽の停止時のフェードアウト時間（ミリ秒）"),
        ["result_music_fade_in"] = L(
            "结果音乐淡入", "Result music fade-in", "結果音楽のフェードイン",
            "结果音乐开始播放时的渐入时间（毫秒）", "Fade-in time of the result music, in milliseconds", "結果音楽の再生開始時のフェードイン時間（ミリ秒）"),
        ["result_music_fade_out"] = L(
            "结果音乐淡出", "Result music fade-out", "結果音楽のフェードアウト",
            "结果音乐停止播放时的渐出时间（毫秒）", "Fade-out time of the result music, in milliseconds", "結果音楽の停止時のフェードアウト時間（ミリ秒）"),

        // 七个覆盖开关在页面上一律写作"启用覆盖"，控制台里它们是七条独立设置，必须各自说清覆盖的是哪一块。
        ["override_animation_settings"] = L(
            "覆盖动画设置", "Override animation settings", "アニメーション設定を上書き",
            "本页使用自己的动画设置，而不是默认抽取设置", "This page uses its own animation settings instead of the default draw settings", "このページ独自のアニメーション設定を使い、既定の抽選設定は使いません"),
        ["override_color_settings"] = L(
            "覆盖颜色设置", "Override color settings", "色設定を上書き",
            "本页使用自己的颜色设置，而不是默认抽取设置", "This page uses its own color settings instead of the default draw settings", "このページ独自の色設定を使い、既定の抽選設定は使いません"),
        ["override_display_settings"] = L(
            "覆盖显示设置", "Override display settings", "表示設定を上書き",
            "本页使用自己的显示设置，而不是默认抽取设置", "This page uses its own display settings instead of the default draw settings", "このページ独自の表示設定を使い、既定の抽選設定は使いません"),
        ["override_reminder_settings"] = L(
            "覆盖提示语设置", "Override reminder settings", "ヒント表示設定を上書き",
            "本页使用自己的提示语设置，而不是默认抽取设置", "This page uses its own reminder settings instead of the default draw settings", "このページ独自のヒント表示設定を使い、既定の抽選設定は使いません"),
        ["override_student_image_settings"] = L(
            "覆盖头像设置", "Override member image settings", "メンバー画像設定を上書き",
            "本页使用自己的头像设置，而不是默认抽取设置", "This page uses its own member image settings instead of the default draw settings", "このページ独自のメンバー画像設定を使い、既定の抽選設定は使いません"),
        ["override_music_settings"] = L(
            "覆盖音乐设置", "Override music settings", "音楽設定を上書き",
            "本页使用自己的音乐设置，而不是默认抽取设置", "This page uses its own music settings instead of the default draw settings", "このページ独自の音楽設定を使い、既定の抽選設定は使いません"),
        ["override_voice_announcement_settings"] = L(
            "覆盖语音播报设置", "Override voice announcement settings", "音声読み上げ設定を上書き",
            "本页使用自己的语音播报设置，而不是默认抽取设置", "This page uses its own voice announcement settings instead of the default draw settings", "このページ独自の読み上げ設定を使い、既定の抽選設定は使いません"),

        // ---------------- 悬浮窗
        ["startup_display_floating_window"] = R(FloatingWindow, "S_Display_StartupDisplay"),
        ["floating_window_opacity"] = R(FloatingWindow, "S_Display_Opacity"),
        ["floating_window_topmost_mode"] = R(FloatingWindow, "S_Display_TopmostMode"),
        ["show_roll_call_button"] = R(FloatingWindow, "S_Buttons_RollCall"),
        ["show_quick_draw_button"] = R(FloatingWindow, "S_Buttons_QuickDraw"),
        ["show_lottery_button"] = R(FloatingWindow, "S_Buttons_Lottery"),
        // 计时器按钮在页面资源里只有标题没有说明，而"点名入口"那句说明属于点名按钮，不能借用。
        ["show_timer_button"] = R(FloatingWindow, "S_Buttons_Timer") with
        {
            Description = Lit(
                "在悬浮窗中显示计时器入口", "Shows the timer entry in the floating window", "フローティングウィンドウにタイマーの入口を表示します")
        },
        ["floating_window_placement"] = R(FloatingWindow, "S_Buttons_Placement"),
        ["floating_window_display_style"] = R(FloatingWindow, "S_Buttons_DisplayStyle"),
        ["floating_window_size"] = R(FloatingWindow, "S_Buttons_Size"),
        ["floating_window_theme"] = L(
            "悬浮窗主题", "Floating window theme", "フローティングウィンドウのテーマ",
            "悬浮窗控件使用的配色方案", "Color scheme used by the floating window controls", "フローティングウィンドウのコントロールが使う配色"),
        ["stick_to_edge"] = R(FloatingWindow, "S_Interaction_StickToEdge"),
        ["stick_to_edge_recover_seconds"] = R(FloatingWindow, "S_Interaction_StickToEdgeRecoverSeconds"),
        ["stick_to_edge_display_style"] = R(FloatingWindow, "S_Interaction_StickToEdgeDisplayStyle"),
        ["docked_window_size"] = R(FloatingWindow, "S_Dock_Size"),
        ["draggable"] = R(FloatingWindow, "S_Interaction_Draggable"),
        ["long_press_duration"] = R(FloatingWindow, "S_Interaction_LongPressDuration"),
        ["do_not_steal_focus"] = R(FloatingWindow, "S_Interaction_DoNotStealFocus"),
        ["hide_on_foreground"] = R(FloatingWindow, "S_Interaction_HideOnForeground"),
        ["hide_on_foreground_window_titles"] = R(FloatingWindow, "S_Interaction_HideOnForegroundWindowTitles"),
        ["hide_on_foreground_process_names"] = R(FloatingWindow, "S_Interaction_HideOnForegroundProcessNames"),

        // ---------------- 联动
        ["data_source"] = R(Linkage, "S_External_DataSource"),
        ["instant_draw_disable"] = R(Linkage, "S_External_InstantDrawDisable"),
        ["verification_required"] = R(Linkage, "S_External_VerificationRequired"),
        ["hide_floating_window_on_class_end"] = R(Linkage, "S_ClassTime_HideFloatingWindow"),
        ["pre_class_reset_enabled"] = R(Linkage, "S_ClassTime_PreClassReset"),
        ["pre_class_reset_time"] = R(Linkage, "S_ClassTime_PreClassResetTime"),
        ["post_class_disable_delay"] = R(Linkage, "S_ClassTime_PostClassDisableDelay"),
        ["pre_class_enable_time"] = R(Linkage, "S_ClassTime_PreClassEnableTime"),
        ["subject_history_break_assignment"] = R(Linkage, "S_SubjectHistory_BreakAssignment"),
        ["subject_history_filter_enabled"] = R(Linkage, "S_SubjectHistory_Filter"),

        // ---------------- 语音
        ["enable"] = R(Voice, "S_Playback_Enable"),
        ["voice_engine"] = R(Voice, "S_Playback_Engine"),
        ["volume"] = R(Voice, "S_Playback_Volume"),
        ["speech_rate"] = R(Voice, "S_Playback_SpeechRate"),
        ["voice_wait_complete"] = R(Voice, "S_Playback_WaitComplete"),
        ["announce_id"] = R(Voice, "S_Content_AnnounceId"),
        ["announce_name"] = R(Voice, "S_Content_AnnounceName"),
        ["edge_tts_voice_name"] = R(Voice, "S_Playback_EdgeTtsVoice"),
        ["system_tts_voice_name"] = R(Voice, "S_Playback_Voice"),
        ["system_volume_control"] = R(Voice, "S_SystemVolume_Control"),
        ["system_volume_size"] = R(Voice, "S_SystemVolume_Size"),
        ["omni_tts_provider"] = R(Voice, "S_OmniTts_Provider"),
        ["omni_tts_api_base_url"] = R(Voice, "S_OmniTts_ApiBaseUrl"),
        ["omni_tts_model"] = R(Voice, "S_OmniTts_Model"),
        ["omni_tts_voice_id"] = R(Voice, "S_OmniTts_Voice"),
        ["omni_tts_instructions"] = R(Voice, "S_OmniTts_Instructions"),
        ["mi_mo_voice_design_prompt"] = R(MiMo, "S_MiMoVoiceDesignPrompt"),
        ["mi_mo_voice_clone_reference_hash"] = L(
            "音色克隆参考音频", "Voice clone reference", "音色クローンの参照音声",
            "已配置参考音频的哈希；音频本身只保存在本机私有目录，不写入设置也不备份", "Hash of the configured reference audio; the audio itself stays in a private local directory and is never written to settings or backups", "設定済み参照音声のハッシュ。音声ファイル自体は端末内の非公開ディレクトリにのみ保存され、設定やバックアップには含まれません"),

        // ---------------- 历史记录
        ["show_roll_call_history"] = R(History, "S_History_ShowRollCall"),
        ["show_lottery_history"] = R(History, "S_History_ShowLottery"),
        ["select_weight"] = R(History, "S_History_SelectWeight"),
        ["selected_class_name"] = R(History, "S_Filter_SelectedClassName"),
        ["selected_pool_name"] = R(History, "S_Filter_SelectedPoolName"),

        // ---------------- 更新
        ["auto_update_mode"] = R(Update, "S_Strategy_AutoUpdateMode"),
        ["update_channel"] = R(Update, "S_Strategy_Channel"),
        ["update_mode_version"] = L(
            "更新模式格式版本", "Update mode format version", "更新モード形式のバージョン",
            "记录旧配置使用过的更新模式格式，仅用于升级时的兼容判断", "Records which update-mode format an older configuration used, only for upgrade compatibility", "旧設定が使っていた更新モード形式の記録で、アップグレード時の互換判定にのみ使われます"),

        // ---------------- 更多设置：快捷键（标签就是页面上的行标题）
        ["enable_shortcut"] = R(More, "S_Shortcut_Enable"),
        ["open_roll_call_page_shortcut"] = R(More, "S_Shortcut_OpenRollCallPage"),
        ["quick_draw_shortcut"] = R(More, "S_Shortcut_QuickDraw"),
        ["open_lottery_page_shortcut"] = R(More, "S_Shortcut_OpenLotteryPage"),
        ["increase_roll_call_count_shortcut"] = R(More, "S_Shortcut_IncreaseRollCallCount") with
        {
            Description = Lit(
                "增加抽取人数的快捷键", "Shortcut that increases the number of members drawn", "抽選人数を増やすショートカット")
        },
        ["decrease_roll_call_count_shortcut"] = R(More, "S_Shortcut_DecreaseRollCallCount") with
        {
            Description = Lit(
                "减少抽取人数的快捷键", "Shortcut that decreases the number of members drawn", "抽選人数を減らすショートカット")
        },
        ["increase_lottery_count_shortcut"] = R(More, "S_Shortcut_IncreaseLotteryCount") with
        {
            Description = Lit(
                "增加抽奖数量的快捷键", "Shortcut that increases the number of prizes drawn", "抽選数量を増やすショートカット")
        },
        ["decrease_lottery_count_shortcut"] = R(More, "S_Shortcut_DecreaseLotteryCount") with
        {
            Description = Lit(
                "减少抽奖数量的快捷键", "Shortcut that decreases the number of prizes drawn", "抽選数量を減らすショートカット")
        },
        ["start_roll_call_shortcut"] = R(More, "S_Shortcut_StartRollCall") with
        {
            Description = Lit(
                "开始或停止点名的快捷键", "Shortcut that starts or stops a roll call", "点呼を開始または停止するショートカット")
        },
        ["start_lottery_shortcut"] = R(More, "S_Shortcut_StartLottery") with
        {
            Description = Lit(
                "开始或停止抽奖的快捷键", "Shortcut that starts or stops a lottery draw", "抽選を開始または停止するショートカット")
        },
        ["lottery_enabled"] = R(More, "S_LotteryEnabled"),
        ["roll_call_control_panel_position"] = R(More, "S_RollCallPanelPosition"),
        ["lottery_control_panel_position"] = R(More, "S_LotteryPanelPosition"),

        // ---------------- 更多设置：页面控件开关（页面把整组开关放进一个多选框，没有逐项文案）
        ["roll_call_start_button"] = L(
            "开始按钮", "Start button", "開始ボタン",
            "在点名页控制面板中显示开始按钮", "Shows the start button on the roll-call control panel", "点呼ページのコントロールパネルに開始ボタンを表示します"),
        ["roll_call_reset_button"] = L(
            "重置按钮", "Reset button", "リセットボタン",
            "在点名页控制面板中显示重置按钮", "Shows the reset button on the roll-call control panel", "点呼ページのコントロールパネルにリセットボタンを表示します"),
        ["roll_call_quantity_control"] = L(
            "人数控件", "Member count control", "人数コントロール",
            "在点名页控制面板中显示抽取人数的加减控件", "Shows the member-count stepper on the roll-call control panel", "点呼ページのコントロールパネルに抽選人数の増減コントロールを表示します"),
        ["roll_call_quantity_label"] = L(
            "人数标签", "Member count label", "人数ラベル",
            "在点名页控制面板中显示当前抽取人数", "Shows the current member count on the roll-call control panel", "点呼ページのコントロールパネルに現在の抽選人数を表示します"),
        ["roll_call_list_selector"] = L(
            "名单选择", "List selector", "リスト選択",
            "在点名页控制面板中显示点名名单下拉框", "Shows the roll-call list dropdown on the control panel", "点呼ページのコントロールパネルにリスト選択を表示します"),
        ["roll_call_range_selector"] = L(
            "范围选择", "Range selector", "範囲選択",
            "在点名页控制面板中显示抽取范围选择", "Shows the draw-range selector on the roll-call control panel", "点呼ページのコントロールパネルに抽選範囲の選択を表示します"),
        ["roll_call_gender_selector"] = L(
            "性别选择", "Gender selector", "性別選択",
            "在点名页控制面板中显示性别筛选", "Shows the gender filter on the roll-call control panel", "点呼ページのコントロールパネルに性別フィルターを表示します"),
        ["roll_call_remaining_button"] = L(
            "剩余名单按钮", "Remaining list button", "残りリストボタン",
            "在点名页控制面板中显示查看剩余名单的入口", "Shows the remaining-list entry on the roll-call control panel", "点呼ページのコントロールパネルに残りリストの入口を表示します"),
        ["lottery_start_button"] = L(
            "开始按钮", "Start button", "開始ボタン",
            "在抽奖页控制面板中显示开始按钮", "Shows the start button on the lottery control panel", "抽選ページのコントロールパネルに開始ボタンを表示します"),
        ["lottery_reset_button"] = L(
            "重置按钮", "Reset button", "リセットボタン",
            "在抽奖页控制面板中显示重置按钮", "Shows the reset button on the lottery control panel", "抽選ページのコントロールパネルにリセットボタンを表示します"),
        ["lottery_quantity_control"] = L(
            "数量控件", "Prize count control", "数量コントロール",
            "在抽奖页控制面板中显示抽取数量的加减控件", "Shows the prize-count stepper on the lottery control panel", "抽選ページのコントロールパネルに抽選数量の増減コントロールを表示します"),
        ["lottery_quantity_label"] = L(
            "数量标签", "Prize count label", "数量ラベル",
            "在抽奖页控制面板中显示当前抽取数量", "Shows the current prize count on the lottery control panel", "抽選ページのコントロールパネルに現在の抽選数量を表示します"),
        ["lottery_list_selector"] = L(
            "奖池选择", "Pool selector", "賞品プール選択",
            "在抽奖页控制面板中显示奖池下拉框", "Shows the prize-pool dropdown on the lottery control panel", "抽選ページのコントロールパネルに賞品プールの選択を表示します"),
        ["lottery_student_list_selector"] = L(
            "成员名单选择", "Member list selector", "メンバーリスト選択",
            "在抽奖页控制面板中显示参与抽奖的成员名单", "Shows the member list that also takes part in the lottery", "抽選ページのコントロールパネルに抽選に参加するメンバーリストを表示します"),
        ["lottery_range_selector"] = L(
            "范围选择", "Range selector", "範囲選択",
            "在抽奖页控制面板中显示抽取范围选择", "Shows the draw-range selector on the lottery control panel", "抽選ページのコントロールパネルに抽選範囲の選択を表示します"),
        ["lottery_gender_selector"] = L(
            "性别选择", "Gender selector", "性別選択",
            "在抽奖页控制面板中显示性别筛选", "Shows the gender filter on the lottery control panel", "抽選ページのコントロールパネルに性別フィルターを表示します"),
        ["lottery_remaining_button"] = L(
            "剩余奖项按钮", "Remaining prize button", "残り賞品ボタン",
            "在抽奖页控制面板中显示查看剩余奖项的入口", "Shows the remaining-prizes entry on the lottery control panel", "抽選ページのコントロールパネルに残り賞品の入口を表示します")
    };

    // ---------------------------------------------------------------- 对外接口

    /// <summary>取类目标签；任何情况下都不会返回空串。</summary>
    public static string GetCategoryLabel(string categoryId)
    {
        ArgumentException.ThrowIfNullOrEmpty(categoryId);

        if (CategoryLabels.TryGetValue(categoryId, out var spec))
        {
            foreach (var culture in CultureChain())
            {
                if (Resolve(spec, culture) is { Length: > 0 } text)
                    return text;
            }
        }

        return Humanize(categoryId);
    }

    /// <summary>
    ///     取字段标签；任何情况下都不会返回空串（查不到就退化成属性名的英文短语）。
    /// </summary>
    /// <param name="path">协议路径，如 <c>voice.volume</c>。</param>
    public static string GetFieldLabel(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (ResolveSpec(path) is { } spec)
        {
            foreach (var culture in CultureChain())
            {
                if (Resolve(spec.Label, culture) is { Length: > 0 } text)
                    return text;
            }
        }

        return Humanize(Token(path));
    }

    /// <summary>取字段说明；查不到时返回 <c>null</c>（说明宁可为空，也不复述属性名）。</summary>
    /// <param name="path">协议路径，如 <c>voice.volume</c>。</param>
    public static string? GetFieldDescription(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (ResolveSpec(path) is not { } spec)
            return null;

        foreach (var culture in CultureChain())
        {
            if (ResolveDescription(spec, culture) is { Length: > 0 } text)
                return text;
        }

        return null;
    }

    // ---------------------------------------------------------------- 对外接口：三语映射

    /// <summary>
    ///     取类目标签的三语映射，键固定是 <c>zh-CN</c> / <c>en-US</c> / <c>ja-JP</c>；
    ///     这个类目一条文案都取不到时返回 <c>null</c>。
    /// </summary>
    /// <remarks>
    ///     单语言的 <see cref="GetCategoryLabel" /> 是"这台设备当前的界面语言"，只有设备语言的控制台能用。
    ///     控制台管理员完全可以用英文界面去看一台中文设备，那时他需要的是英文原文，而不是设备语言的中文。
    /// </remarks>
    public static IReadOnlyDictionary<string, string>? GetCategoryLabels(string categoryId)
    {
        ArgumentException.ThrowIfNullOrEmpty(categoryId);

        return CategoryLabels.TryGetValue(categoryId, out var spec)
            ? ResolveAll(culture => Resolve(spec, culture))
            : null;
    }

    /// <summary>取字段标签的三语映射；这个字段一条文案都取不到时返回 <c>null</c>。</summary>
    /// <param name="path">协议路径，如 <c>voice.volume</c>。</param>
    public static IReadOnlyDictionary<string, string>? GetFieldLabels(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        return ResolveSpec(path) is { } spec
            ? ResolveAll(culture => Resolve(spec.Label, culture))
            : null;
    }

    /// <summary>
    ///     取类目说明的三语映射；**永远是 <c>null</c>**，因为客户端从来没有写过类目级的说明。
    /// </summary>
    /// <remarks>
    ///     类目只是控制台表单里的分组标题，设置页把每一句说明都写在具体某一条设置上
    ///     （见 <see cref="ControlSettingCategory.Description" />）。保留这个入口是为了让控制台对类目和
    ///     字段走同一条取词路径：今天它一定为空，将来真的写了类目说明也不必再改协议。
    /// </remarks>
    public static IReadOnlyDictionary<string, string>? GetCategoryDescriptions(string categoryId)
    {
        ArgumentException.ThrowIfNullOrEmpty(categoryId);

        return null;
    }

    /// <summary>取字段说明的三语映射；查不到说明时返回 <c>null</c>（与单语言接口一致，说明不复述属性名）。</summary>
    /// <param name="path">协议路径，如 <c>voice.volume</c>。</param>
    public static IReadOnlyDictionary<string, string>? GetFieldDescriptions(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        return ResolveSpec(path) is { } spec
            ? ResolveAll(culture => ResolveDescription(spec, culture))
            : null;
    }

    // ---------------------------------------------------------------- 对外接口：按控制台请求的语言取词

    /// <summary>
    ///     把请求里的 <c>locale</c> 归一到发布的三语键；认不出来返回 <c>null</c>（当作"没给"）。
    /// </summary>
    /// <remarks>
    ///     只比语言前缀：<c>zh</c> / <c>zh-Hans</c> / <c>zh-CN</c> 都算中文。控制台与设备装的语言包
    ///     版本不一定一致，为一个区域子标签把整次读取判失败没有任何好处；认不出来就退回设备语言。
    /// </remarks>
    public static string? NormalizePublishedLocale(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
            return null;

        var tag = locale.Trim().ToLowerInvariant();
        if (tag.StartsWith("zh", StringComparison.Ordinal)) return "zh-CN";
        if (tag.StartsWith("ja", StringComparison.Ordinal)) return "ja-JP";
        if (tag.StartsWith("en", StringComparison.Ordinal)) return "en-US";
        return null;
    }

    /// <summary>
    ///     按控制台请求的语言取字段标签；那一语取不到词时回落到设备语言的标签。
    /// </summary>
    /// <remarks>
    ///     为什么不让控制台自己从三语映射里挑：一张三语映射等于每个字段多带 6 段文案，
    ///     五类设置一起读就顶穿了单帧上限（真发生过）。控制台一次只能显示一种语言，
    ///     所以**语言放在请求里**，响应里只回那一份。
    /// </remarks>
    public static string GetFieldLabel(string path, string? locale)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        return PickPublished(GetFieldLabels(path), locale) ?? GetFieldLabel(path);
    }

    /// <summary>按控制台请求的语言取字段说明；没有说明时返回 <c>null</c>。</summary>
    public static string? GetFieldDescription(string path, string? locale)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        return PickPublished(GetFieldDescriptions(path), locale) ?? GetFieldDescription(path);
    }

    /// <summary>按控制台请求的语言取类目标签；那一语取不到词时回落到设备语言的标签。</summary>
    public static string GetCategoryLabel(string categoryId, string? locale)
    {
        ArgumentException.ThrowIfNullOrEmpty(categoryId);

        return PickPublished(GetCategoryLabels(categoryId), locale) ?? GetCategoryLabel(categoryId);
    }

    /// <summary>从三语映射里取出请求的那一语；没请求或那一语缺词时返回 <c>null</c>。</summary>
    private static string? PickPublished(IReadOnlyDictionary<string, string>? published, string? locale)
    {
        if (published is null || NormalizePublishedLocale(locale) is not { } key)
            return null;

        return published.TryGetValue(key, out var text) && text.Length > 0 ? text : null;
    }

    /// <summary>
    ///     按发布的三语各取一次词，得到 <c>{"zh-CN": …, "en-US": …, "ja-JP": …}</c>；一条都取不到时返回 <c>null</c>。
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         每一语走的是**它自己的**回退链，与这台设备当前的界面语言无关：日语缺词退英语再退中文基资源，
    ///         英语缺词退中文，中文缺词退英语。这正是同一个词在三种语言下的既有取词口径，
    ///         只是把设备语言那一次换成了固定的一语。
    ///     </para>
    ///     <para>
    ///         取不到的那一语**不出现这个键**，而不是给一个空串：空串到了控制台就是"有设置名、没说明"的假文案，
    ///         与"这条设置本来就没有说明"分不开。三语都取不到时整个映射是 <c>null</c>，
    ///         控制台据此回到设备语言的 <c>label</c>/<c>description</c>。
    ///     </para>
    ///     <para>
    ///         这里刻意**不**套用单语言接口最后那级"属性名短语"兜底：那串英文不是任何一语的翻译，
    ///         把它塞进 <c>ja-JP</c> 只会让日语控制台把英文当成日语。缺词时让控制台回落到 <c>label</c>，
    ///         它拿到的正是同一个兜底串。
    ///     </para>
    /// </remarks>
    private static IReadOnlyDictionary<string, string>? ResolveAll(Func<CultureInfo, string?> resolve)
    {
        Dictionary<string, string>? published = null;

        foreach (var (key, fallback) in PublishedLanguages)
        {
            foreach (var culture in fallback)
            {
                if (resolve(culture) is not { Length: > 0 } text)
                    continue;

                published ??= new Dictionary<string, string>(PublishedLanguages.Length, StringComparer.Ordinal);
                published[key] = text;
                break;
            }
        }

        return published;
    }

    // ---------------------------------------------------------------- 解析

    /// <summary>说明的出处：条目自己写了就用它，否则沿用设置页"标签键 + <c>_D</c>"的约定。</summary>
    /// <remarks>字面量条目没有"<c>_D</c>"这条约定（它没有资源键），说明必须显式写出来，否则就是没有说明。</remarks>
    private static LabelSpec? DescriptionSpec(FieldSpec spec) =>
        spec.Description ?? (spec.Label.Key is { } key ? spec.Label with { Key = key + "_D" } : null);

    private static string? ResolveDescription(FieldSpec spec, CultureInfo culture) =>
        Resolve(DescriptionSpec(spec), culture);

    private static FieldSpec? ResolveSpec(string path)
    {
        if (FieldsByPath.TryGetValue(path, out var exact))
            return exact;

        return FieldsByToken.TryGetValue(Token(path), out var token) ? token : null;
    }

    private static string Token(string path)
    {
        var separator = path.LastIndexOf('.');
        return separator < 0 ? path : path[(separator + 1)..];
    }

    /// <summary>当前界面语言下的取词顺序：界面语言 → 英语 → 中文基资源。</summary>
    private static CultureInfo[] CultureChain() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch
        {
            "ja" => JapaneseFallback,
            "en" => EnglishFallback,
            "zh" => ChineseFallback,
            // 应用只发布了中日英三种资源：别的界面语言直接查它自己的文化会立刻落到中文基资源，
            // 那是"没翻译"而不是"翻译成英语"，所以这里显式从英语开始退。
            _ => EnglishFallback
        };

    private static string? Resolve(LabelSpec? spec, CultureInfo culture)
    {
        if (spec is null)
            return null;

        if (spec.Text is { } text)
        {
            return culture.TwoLetterISOLanguageName switch
            {
                "ja" => text.Ja,
                "en" => text.En,
                _ => text.Zh
            };
        }

        var manager = ManagerFor(spec.Page!);
        if (manager is null || spec.Key is not { } key)
            return null;

        try
        {
            return manager.GetString(key, culture);
        }
        catch (MissingManifestResourceException)
        {
            // 表里的资源页名字写错时不能让整次 settings.read 挂掉：当作查不到，退回下一级文案。
            return null;
        }
    }

    private static ResourceManager? ManagerFor(string page)
    {
        lock (ResourceGate)
        {
            if (ResourceManagers.TryGetValue(page, out var cached))
                return cached;

            var manager = AppAssembly.Value is { } assembly
                ? new ResourceManager($"SecRandom.Langs.{page}", assembly)
                : null;

            ResourceManagers[page] = manager;
            return manager;
        }
    }

    /// <summary>
    ///     反查应用程序集：Core 不能编译期引用应用层，所以按一个稳定的资源类名解析。
    ///     解析不到（例如 Core 被非 SecRandom 宿主单独加载）时整条资源链失效，
    ///     标签退化为英文属性名，而不是抛异常。
    /// </summary>
    private static Assembly? ResolveAppAssembly() =>
        Type.GetType(AppAssemblyAnchorType, throwOnError: false)?.Assembly;

    /// <summary>
    ///     兜底标签：<c>half_repeat</c> → <c>Half repeat</c>。
    /// </summary>
    /// <remarks>
    ///     刻意做成"首字母大写、其余保持小写"的短语而不是 PascalCase 还原：<c>VoiceEnable</c> 拆成
    ///     <c>Voice Enable</c> 只是把属性名念了一遍，控制台管理员读到的是代码，不是设置。
    /// </remarks>
    private static string Humanize(string token)
    {
        var text = token.Replace('_', ' ').Trim();
        return text.Length == 0 ? token : char.ToUpperInvariant(text[0]) + text[1..];
    }

    // ---------------------------------------------------------------- 表构造小工具

    /// <summary>资源条目：标签/说明直接取客户端设置页自己的文案（说明默认取 <c>键_D</c>）。</summary>
    private static FieldSpec R(string page, string key) => new(new LabelSpec(page, key, null), null);

    /// <summary>资源条目，但说明取另一个键（页面把多条设置写在同一句说明下时用）。</summary>
    private static FieldSpec R(string page, string key, string descriptionPage, string descriptionKey) =>
        new(new LabelSpec(page, key, null), new LabelSpec(descriptionPage, descriptionKey, null));

    /// <summary>字面量条目：客户端没有任何一处写过这条设置的文案，只能自己写。</summary>
    private static FieldSpec L(string zh, string en, string ja) =>
        new(new LabelSpec(null, null, new Text(zh, en, ja)), null);

    /// <summary>字面量条目（带说明）。</summary>
    private static FieldSpec L(string zh, string en, string ja, string descriptionZh, string descriptionEn, string descriptionJa) =>
        new(
            new LabelSpec(null, null, new Text(zh, en, ja)),
            new LabelSpec(null, null, new Text(descriptionZh, descriptionEn, descriptionJa)));

    private static LabelSpec Lit(string zh, string en, string ja) => new(null, null, new Text(zh, en, ja));

    /// <summary>一条标签的出处：要么是某个资源页里的键，要么是写在这里的三语字面量。</summary>
    private sealed record LabelSpec(string? Page, string? Key, Text? Text);

    /// <summary>一个字段的标签与（可选）说明。</summary>
    private sealed record FieldSpec(LabelSpec Label, LabelSpec? Description);

    private readonly record struct Text(string Zh, string En, string Ja);
}
