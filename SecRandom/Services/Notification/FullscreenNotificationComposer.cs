using System;
using Microsoft.Extensions.Logging;
using SecRandom.Core;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Enums;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.AttachedSettings;
using SecRandom.Core.Models.Notification;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Notification;
using SecRandom.Shared.Extensions;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Services.Notification;

/// <summary>
///     把抽取页面的领域对象翻译成全屏渠道只读快照。
/// </summary>
/// <remarks>
///     窗口与 Presenter 都不允许回读 ViewModel / 重新查候选池，所以"展示什么"必须在
///     提交完成的那一刻被冻结成不可变记录。文案也在这一刻选定并计数：这样屏幕上的那条
///     文案和"使用次数 +1"永远是同一条，不会出现显示 A 却给 B 计数。
/// </remarks>
public static class FullscreenNotificationComposer
{
    /// <summary>文案库不可用时的兜底模板。展示时 <c>{result}</c> 会被替换成纯结果文本。</summary>
    public const string FallbackTemplate = "恭喜 {result}";

    /// <summary>把一组学生映射成展示项。学号作为副标题，方便课堂上分辨同名；配了图片的成员带上头像路径。</summary>
    public static IReadOnlyList<FullscreenResultItem> MapStudents(IEnumerable<Student> students)
        => students.Select(static student => new FullscreenResultItem(
                DisplayValue(student.Name, student.Id),
                string.IsNullOrWhiteSpace(student.Name) ? null : student.Id,
                ResolveStudentImagePath(student)))
            .ToList();

    /// <summary>
    ///     按"显示内容"勾选拼出结果正文：序号与名称可各自开关；两个都不显示时退回原标题，
    ///     绝不让屏幕出现一行空白。
    /// </summary>
    public static string ComposeResultText(FullscreenResultItem item, FullscreenNotificationSettings settings)
    {
        var number = item.Subtitle;
        var name = item.DisplayText;

        var parts = new System.Collections.Generic.List<string>(2);
        if (settings.ShowResultNumber && !string.IsNullOrWhiteSpace(number))
            parts.Add(number);

        if (settings.ShowResultName && !string.IsNullOrWhiteSpace(name))
            parts.Add(name);

        return parts.Count == 0 ? name : string.Join("  ", parts);
    }

    /// <summary>
    ///     抽取开始前冻结展示素材（候选池 + 计数）。冻结池用于学号网格的排布。
    /// </summary>
    public static FullscreenPresentationSeed CreateSeed(
        NotificationSettingsType kind,
        IReadOnlyList<FullscreenResultItem> frozenPool,
        int totalCount,
        int remainingCount,
        string? scopeName)
        => new(kind, frozenPool, totalCount, remainingCount, scopeName);

    /// <summary>
    ///     提交成功后构造结果快照。只有成功提交的抽取才应该调用它——失败/取消的抽取
    ///     不能消耗文案的使用次数。
    /// </summary>
    public static FullscreenResultSnapshot CreateSnapshot(
        NotificationSettingsType kind,
        IReadOnlyList<FullscreenResultItem> results,
        IReadOnlyList<FullscreenResultItem> frozenPool,
        int totalCount,
        int remainingCount,
        string? courseName,
        string? historyGroup,
        string? scopeName)
    {
        var settings = TryGetSettings();
        var template = PickTemplate();

        // 显示内容（序号/名称/头像）在快照构造时定下来：窗口只吃快照，不能再回头查设置，
        // 否则一次抽取的标题和头像会跟着设置页的随手改动来回跳。
        var composedResults = results
            .Select(item => item with { DisplayText = ComposeResultText(item, settings) })
            .ToList();
        var voiceText = FullscreenTextTemplate.RenderPlain(template, JoinResults(composedResults));

        // 文案（前段 + 结果 + 后段）只属于打字机样式：学号网格落定后停在自己的画面上，
        // 不会再显示任何模板文字，所以也不该在这时候突然把文案念出来。
        var captionActive = settings.ShowText && settings.AnimationStyle == FullscreenAnimationStyle.Typewriter;

        return new FullscreenResultSnapshot(
            kind,
            // 必须用拼好的结果：这里曾经传了未拼装的 results，于是"显示内容"勾选在画面上
            // 完全无效（只有语音用到了拼装结果），是用户 m03130 反馈的直接成因。
            composedResults,
            frozenPool,
            totalCount,
            remainingCount,
            courseName,
            historyGroup,
            scopeName,
            DateTimeOffset.Now,
            template,
            captionActive,
            settings.ShowVoiceAnnouncement && captionActive ? voiceText : null,
            settings.ShowResultAvatar);
    }

    /// <summary>预览等不产生真实抽取的场景使用：不选文案、不计数，但外观与显示内容必须跟真实结果一致。</summary>
    public static FullscreenResultSnapshot CreatePreviewSnapshot(
        NotificationSettingsType kind,
        IReadOnlyList<FullscreenResultItem> results,
        IReadOnlyList<FullscreenResultItem> frozenPool,
        int? totalCount = null,
        int? remainingCount = null)
    {
        var settings = TryGetSettings();

        // 预览也必须走"显示内容"这一步：否则用户在设置里勾掉序号、勾上头像，点预览却毫无变化，
        // 就会以为开关是坏的（用户 m03130 的反馈正是这条）。
        var composedResults = results
            .Select(item => item with { DisplayText = ComposeResultText(item, settings) })
            .ToList();
        var captionActive = settings.ShowText && settings.AnimationStyle == FullscreenAnimationStyle.Typewriter;

        return new FullscreenResultSnapshot(
            kind,
            composedResults,
            frozenPool,
            results.Count,
            results.Count,
            null,
            null,
            null,
            DateTimeOffset.Now,
            FallbackTemplate,
            captionActive,
            null,
            settings.ShowResultAvatar);
    }

    private static string PickTemplate()
    {
        try
        {
            var library = IAppHost.TryGetService<TextLibraryService>();
            return library?.PickTemplate() ?? FallbackTemplate;
        }
        catch (Exception)
        {
            // 文案库只是装饰：它坏掉不能拖累已经提交成功的抽取。
            return FallbackTemplate;
        }
    }

    private static FullscreenNotificationSettings TryGetSettings()
    {
        try
        {
            return IAppHost.TryGetService<MainConfigHandler>()?.Data.NotificationSettings.Fullscreen
                   ?? new FullscreenNotificationSettings();
        }
        catch (Exception)
        {
            return new FullscreenNotificationSettings();
        }
    }

    private static string JoinResults(IReadOnlyList<FullscreenResultItem> results)
        => string.Join("、", results.Select(static item => item.DisplayText));

    private static string DisplayValue(string primary, string fallback)
        => string.IsNullOrWhiteSpace(primary) ? fallback : primary;

    /// <summary>读取成员自己配置的展示图片路径（没有配置就安静返回 null，不抛异常）。</summary>
    private static string? ResolveStudentImagePath(Student student)
    {
        try
        {
            var settings = student.GetAttachedObject<DrawImageAttachedSettings>(
                Guid.Parse(GlobalConstants.DrawImageAttachedSettings));
            if (settings is not { IsAttachSettingsEnabled: true } || string.IsNullOrWhiteSpace(settings.ImagePath))
                return null;

            return settings.ImagePath;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
