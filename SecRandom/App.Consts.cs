using System;
using System.Collections.Generic;
using Avalonia.Media;
using SecRandom.Helpers;

namespace SecRandom;

public partial class App
{
    private static readonly string[] LegacyUiFontFamilies = ["MiSans", "HarmonyOS Sans SC", "Segoe UI", "Microsoft YaHei UI"];
    private static readonly FontWeight[] UiFontWeights = [FontWeight.Thin, FontWeight.ExtraLight, FontWeight.Light, FontWeight.Normal, FontWeight.Medium, FontWeight.SemiBold, FontWeight.Bold, FontWeight.ExtraBold, FontWeight.Black, FontWeight.UltraBlack];
    private static readonly Dictionary<string, Func<string>> PageNameProviders = new()
    {
        // 主页面
        ["main.rollCall"] = () => Langs.Common.Resources.RollCall,

        // 基础设置
        ["settings.basic"] = () => Langs.Common.Resources.BasicSettings,
        ["settings.voiceSettings"] = () => Langs.Common.Resources.VoiceSettings,
        ["settings.securitySettings"] = () => Langs.Common.Resources.SecuritySettings,
        ["settings.linkageSettings"] = () => Langs.Common.Resources.LinkageSettings,
        ["settings.themeManagement"] = () => Langs.Common.Resources.ThemeManagement,
        ["settings.floatingWindow"] = () => Langs.Common.Resources.FloatingWindowManagement,

        // 更新与关于
        ["settings.update"] = () => Langs.Common.Resources.UpdateSettings,
        ["settings.about"] = () => Langs.Common.Resources.About,

        // 列表管理
        ["settings.listManagement"] = () => Langs.SettingsPages.ListManagementPage.Resources.ListManagement,
        
        // 列表管理 - 点名
        ["settings.listManagement.rollCall"] = () => Langs.SettingsPages.ListManagementPage.Resources.RollCallListSettings,
        ["settings.listManagement.rollCall.preview"] = () => Langs.SettingsPages.ListManagementSubPages.TablePreview.Resources.RollCallTableTitle,
        ["settings.listManagement.rollCall.setClassName"] = () => Langs.SettingsPages.ListManagementSubPages.RollCallSubPages.Resources.SetClassNameTitle,
        ["settings.listManagement.rollCall.importStudent"] = () => Langs.SettingsPages.ListManagementSubPages.RollCallSubPages.Resources.ImportStudentTitle,
        ["settings.listManagement.rollCall.nameSettings"] = () => Langs.SettingsPages.ListManagementSubPages.RollCallSubPages.Resources.NameSettingsTitle,
        ["settings.listManagement.rollCall.genderSettings"] = () => Langs.SettingsPages.ListManagementSubPages.RollCallSubPages.Resources.Settings_GenderSettingsTitle,
        ["settings.listManagement.rollCall.groupSettings"] = () => Langs.SettingsPages.ListManagementSubPages.RollCallSubPages.Resources.Settings_GroupSettingsTitle,
        ["settings.listManagement.rollCall.tagSettings"] = () => Langs.SettingsPages.ListManagementSubPages.RollCallSubPages.Resources.TagSettingsTitle,
        ["settings.listManagement.rollCall.exportStudent"] = () => Langs.SettingsPages.ListManagementSubPages.RollCallSubPages.Resources.ExportStudentTitle,

        // 列表管理 - 抽奖
        ["settings.listManagement.lottery"] = () => Langs.SettingsPages.ListManagementPage.Resources.LotteryListSettings,
        ["settings.listManagement.lottery.preview"] = () => Langs.SettingsPages.ListManagementSubPages.TablePreview.Resources.LotteryTableTitle,
        ["settings.listManagement.lottery.setPoolName"] = () => Langs.SettingsPages.ListManagementSubPages.LotterySubPages.Resources.SetPoolNameTitle,
        ["settings.listManagement.lottery.importPrize"] = () => Langs.SettingsPages.ListManagementSubPages.LotterySubPages.Resources.ImportPrizeTitle,
        ["settings.listManagement.lottery.prizeSettings"] = () => Langs.SettingsPages.ListManagementSubPages.LotterySubPages.Resources.PrizeSettingsTitle,
        ["settings.listManagement.lottery.weightSettings"] = () => Langs.SettingsPages.ListManagementSubPages.LotterySubPages.Resources.WeightSettingsTitle,
        ["settings.listManagement.lottery.countSettings"] = () => Langs.SettingsPages.ListManagementSubPages.LotterySubPages.Resources.CountSettingsTitle,
        ["settings.listManagement.lottery.nameSettings"] = () => Langs.SettingsPages.ListManagementSubPages.LotterySubPages.Resources.NameSettingsTitle,
        ["settings.listManagement.lottery.genderSettings"] = () => Langs.SettingsPages.ListManagementSubPages.LotterySubPages.Resources.Settings_GenderSettingsTitle,
        ["settings.listManagement.lottery.groupSettings"] = () => Langs.SettingsPages.ListManagementSubPages.LotterySubPages.Resources.Settings_GroupSettingsTitle,
        ["settings.listManagement.lottery.tagSettings"] = () => Langs.SettingsPages.ListManagementSubPages.LotterySubPages.Resources.TagSettingsTitle,
        ["settings.listManagement.lottery.exportPrize"] = () => Langs.SettingsPages.ListManagementSubPages.LotterySubPages.Resources.ExportPrizeTitle,

        // 抽签设置
        ["settings.draw"] = () => Langs.Common.Resources.DrawSettings,
        ["settings.draw.rollCall"] = () => Langs.SettingsPages.DrawSettingsPage.Resources.RollCallSettings,
        ["settings.draw.rollCall.listSpecific"] = () => Langs.SettingsPages.DrawSettingsPage.Resources.Settings_ListSpecificSettings,
        ["settings.draw.quickDraw"] = () => Langs.SettingsPages.DrawSettingsPage.Resources.QuickDrawSettings,
        ["settings.draw.quickDraw.listSpecific"] = () => Langs.SettingsPages.DrawSettingsPage.Resources.Settings_ListSpecificSettings,
        ["settings.draw.lottery"] = () => Langs.SettingsPages.DrawSettingsPage.Resources.LotterySettings,
        ["settings.draw.lottery.listSpecific"] = () => Langs.SettingsPages.DrawSettingsPage.Resources.Settings_ListSpecificSettings,
        ["settings.draw.faceDetector"] = () => Langs.SettingsPages.DrawSettingsPage.Resources.FaceDetectorSettings,

        // 通知设置
        ["settings.notification"] = () => Langs.Common.Resources.NotificationSettings,
        ["settings.notification.rollCall"] = () => Langs.SettingsPages.NotificationSettingsPage.Resources.RollCallNotificationSettings,
        ["settings.notification.quickDraw"] = () => Langs.SettingsPages.NotificationSettingsPage.Resources.QuickDrawNotificationSettings,
        ["settings.notification.lottery"] = () => Langs.SettingsPages.NotificationSettingsPage.Resources.LotteryNotificationSettings,

        // 历史记录
        ["settings.history"] = () => Langs.Common.Resources.History,
        ["settings.history.rollCall"] = () => Langs.SettingsPages.HistoryPage.Resources.Settings_RollCallHistory,
        ["settings.history.rollCallTable"] = () => Langs.SettingsPages.HistoryPage.Resources.RollCallHistoryTable,
        ["settings.history.lottery"] = () => Langs.SettingsPages.HistoryPage.Resources.Settings_LotteryHistory,
        ["settings.history.lotteryTable"] = () => Langs.SettingsPages.HistoryPage.Resources.LotteryHistoryTable,
    };
    
    public static bool IsAcrylicBlurSupported { get; } =
        OperatingSystem.IsWindows() 
        && Environment.OSVersion.Version >= new Version(10, 0, 18362, 0)
        && AvaloniaUnsafeAccessorHelpers.GetActiveWin32CompositionMode() == AvaloniaUnsafeAccessorHelpers.Win32CompositionMode.WinUiComposition;
    
    public static bool IsMicaSupported { get; } = 
        OperatingSystem.IsWindows() 
        && Environment.OSVersion.Version >= new Version(10, 0, 22000, 0)
        && AvaloniaUnsafeAccessorHelpers.GetActiveWin32CompositionMode() == AvaloniaUnsafeAccessorHelpers.Win32CompositionMode.WinUiComposition;
}