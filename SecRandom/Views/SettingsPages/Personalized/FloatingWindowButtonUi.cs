using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services;
using SecRandom.Core.Services.FloatingWindow;
using SecRandom.Services.FloatingWindow;
using LR = SecRandom.Langs.SettingsPages.Personalized.FloatingWindowButtons.Resources;

namespace SecRandom.Views.SettingsPages.Personalized;

/// <summary>动作下拉框的一项。</summary>
public sealed record ButtonActionOption(FloatingWindowButtonActionKind Kind, string Label)
{
    public override string ToString() => Label;
}

/// <summary>目标下拉框的一项；<see cref="Value" /> 是页面 id 或路径。</summary>
public sealed record ButtonTargetOption(string Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>图标选择器里的一项。</summary>
public sealed record IconChoice(string Name, string Glyph);

/// <summary>
///     自定义按钮编辑页和抽屉共用的文案、选项与图标索引。
/// </summary>
public static class FloatingWindowButtonUi
{
    public const string PageId = "settings.personalized.floatingWindowButtons";

    private static readonly Lazy<IReadOnlyDictionary<string, string>> IconTable = new(() =>
    {
        var table = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in typeof(FluentIcons).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.FieldType != typeof(string))
                continue;
            if (field.GetValue(null) is string glyph)
                table[field.Name] = glyph;
        }

        return table;
    });

    private static readonly string[] CuratedIconNames =
    [
        nameof(FluentIcons.PeopleFilled),
        nameof(FluentIcons.FlashFilled),
        nameof(FluentIcons.TimerFilled),
        nameof(FluentIcons.SettingsFilled),
        nameof(FluentIcons.LinkFilled),
        nameof(FluentIcons.FolderFilled),
        nameof(FluentIcons.DocumentFilled),
        nameof(FluentIcons.WindowAppsFilled),
        nameof(FluentIcons.HomeFilled),
        nameof(FluentIcons.ListFilled),
        nameof(FluentIcons.CalendarFilled),
        nameof(FluentIcons.ClockFilled),
        nameof(FluentIcons.StarFilled),
        nameof(FluentIcons.HeartFilled),
        nameof(FluentIcons.FlagFilled),
        nameof(FluentIcons.BookmarkFilled),
        nameof(FluentIcons.BookFilled),
        nameof(FluentIcons.EditFilled),
        nameof(FluentIcons.PlayFilled),
        nameof(FluentIcons.PauseFilled),
        nameof(FluentIcons.MusicNote2Filled),
        nameof(FluentIcons.VideoFilled),
        nameof(FluentIcons.ImageFilled),
        nameof(FluentIcons.CameraFilled),
        nameof(FluentIcons.MailFilled),
        nameof(FluentIcons.ChatFilled),
        nameof(FluentIcons.GlobeFilled),
        nameof(FluentIcons.RocketFilled),
        nameof(FluentIcons.LightbulbFilled),
        nameof(FluentIcons.WrenchFilled),
        nameof(FluentIcons.DrawShapeFilled),
        nameof(FluentIcons.GamesFilled),
        nameof(FluentIcons.CodeFilled),
        nameof(FluentIcons.BugFilled),
        nameof(FluentIcons.ShieldFilled),
        nameof(FluentIcons.KeyFilled),
        nameof(FluentIcons.LockClosedFilled),
        nameof(FluentIcons.LockOpenFilled),
        nameof(FluentIcons.PersonFilled),
        nameof(FluentIcons.GroupFilled),
        nameof(FluentIcons.PhoneFilled),
        nameof(FluentIcons.DesktopFilled),
        nameof(FluentIcons.LaptopFilled),
        nameof(FluentIcons.PrintFilled),
        nameof(FluentIcons.SaveFilled),
        nameof(FluentIcons.ArrowDownloadFilled),
        nameof(FluentIcons.ArrowUploadFilled),
        nameof(FluentIcons.CloudFilled),
        nameof(FluentIcons.Wifi1Filled),
        nameof(FluentIcons.Speaker2Filled),
        nameof(FluentIcons.MicFilled),
        nameof(FluentIcons.HeadphonesFilled),
        nameof(FluentIcons.EmojiFilled),
        nameof(FluentIcons.GiftFilled),
        nameof(FluentIcons.TrophyFilled),
        nameof(FluentIcons.TargetFilled),
        nameof(FluentIcons.CheckmarkFilled),
        nameof(FluentIcons.AlertFilled)
    ];

    public static string Text(string key) => LR.ResourceManager.GetString(key, LR.Culture) ?? key;

    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Text(key), args);

    public static IReadOnlyList<ButtonActionOption> BuildActionOptions()
    {
        return
        [
            new ButtonActionOption(FloatingWindowButtonActionKind.MainPage, Text("A_MainPage")),
            new ButtonActionOption(FloatingWindowButtonActionKind.QuickDraw, Text("A_QuickDraw")),
            new ButtonActionOption(FloatingWindowButtonActionKind.Timer, Text("A_Timer")),
            new ButtonActionOption(FloatingWindowButtonActionKind.SettingsPage, Text("A_SettingsPage")),
            new ButtonActionOption(FloatingWindowButtonActionKind.Uri, Text("A_Uri")),
            new ButtonActionOption(FloatingWindowButtonActionKind.Path, Text("A_Path"))
        ];
    }

    public static IReadOnlyList<ButtonTargetOption> BuildTargetOptions(FloatingWindowButtonActionKind kind,
        string? current)
    {
        IEnumerable<PageInfo> pages = kind switch
        {
            FloatingWindowButtonActionKind.MainPage => PagesRegistryService.MainItems,
            FloatingWindowButtonActionKind.SettingsPage => PagesRegistryService.SettingsItems
                .Where(item => !item.IsHide && item.Id != PageId),
            _ => []
        };

        // 目标下拉框只列出真正有名字的页面：注册表里存在没有本地化名称的条目，
        // 列出来是一条纯空白项，用户无法辨认也无法选择。
        var options = pages
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .Select(item => new ButtonTargetOption(item.Id, item.Name!))
            .ToList();

        if (!string.IsNullOrWhiteSpace(current) && options.All(option => option.Value != current))
            options.Insert(0, new ButtonTargetOption(current, Text("M_InvalidTargetOption")));

        return options;
    }

    public static string ActionLabel(FloatingWindowButtonActionKind kind) => kind switch
    {
        FloatingWindowButtonActionKind.MainPage => Text("A_MainPage"),
        FloatingWindowButtonActionKind.QuickDraw => Text("A_QuickDraw"),
        FloatingWindowButtonActionKind.Timer => Text("A_Timer"),
        FloatingWindowButtonActionKind.SettingsPage => Text("A_SettingsPage"),
        FloatingWindowButtonActionKind.Uri => Text("A_Uri"),
        FloatingWindowButtonActionKind.Path => Text("A_Path"),
        _ => Text("V_UnsupportedAction")
    };

    public static string TargetLabel(FloatingWindowButtonActionKind kind, string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return "—";

        if (kind is FloatingWindowButtonActionKind.MainPage or FloatingWindowButtonActionKind.SettingsPage)
        {
            IEnumerable<PageInfo> pages = kind == FloatingWindowButtonActionKind.MainPage
                ? PagesRegistryService.MainItems
                : PagesRegistryService.SettingsItems;
            var page = pages.FirstOrDefault(item => item.Id == target);
            if (page is not null)
                return page.Name ?? page.Id;
        }

        return target;
    }

    public static string? DescribeError(FloatingWindowButtonConfig config)
    {
        return FloatingWindowButtonCatalog.Validate(config) switch
        {
            FloatingWindowButtonValidationError.None => null,
            FloatingWindowButtonValidationError.LabelRequired => Text("V_LabelRequired"),
            FloatingWindowButtonValidationError.LabelTooLong =>
                Format("V_LabelTooLong", FloatingWindowButtonCatalog.MaxLabelLength),
            FloatingWindowButtonValidationError.IconMissing => Text("V_IconMissing"),
            FloatingWindowButtonValidationError.IconImageInvalid => Text("V_IconImageInvalid"),
            FloatingWindowButtonValidationError.TargetRequired => Text("V_TargetRequired"),
            FloatingWindowButtonValidationError.TargetTooLong => Text("V_TargetTooLong"),
            FloatingWindowButtonValidationError.TargetInvalidUri => Text("V_TargetInvalidUri"),
            FloatingWindowButtonValidationError.TargetInvalidPath => Text("V_TargetInvalidPath"),
            FloatingWindowButtonValidationError.UnsupportedAction => Text("V_UnsupportedAction"),
            _ => Text("V_UnsupportedAction")
        };
    }

    public static string DescribeImportError(FloatingWindowIconImportError error) => error switch
    {
        FloatingWindowIconImportError.Unreadable => Text("E_Unreadable"),
        FloatingWindowIconImportError.TooLarge => Text("E_TooLarge"),
        FloatingWindowIconImportError.UnsupportedFormat => Text("E_UnsupportedFormat"),
        FloatingWindowIconImportError.NotAnApplication => Text("E_NotAnApplication"),
        FloatingWindowIconImportError.NoIconFound => Text("E_NoIconFound"),
        FloatingWindowIconImportError.ExtractionFailed => Text("E_ExtractionFailed"),
        _ => Text("M_IconImportFailed")
    };

    public static IReadOnlyList<IconChoice> FilterIcons(string? query)
    {
        var table = IconTable.Value;
        var text = query?.Trim();

        if (string.IsNullOrEmpty(text))
            return CuratedIconNames
                .Where(table.ContainsKey)
                .Select(name => new IconChoice(name, table[name]))
                .ToList();

        return table.Keys
            .Where(name => name.Contains(text, StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Take(240)
            .Select(name => new IconChoice(name, table[name]))
            .ToList();
    }
}
