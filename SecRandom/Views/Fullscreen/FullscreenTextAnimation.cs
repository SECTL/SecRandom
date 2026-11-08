using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using SecRandom.Core.Models.Notification;

namespace SecRandom.Views.Fullscreen;

/// <summary>
///     全屏结果的一行文案：占位符之前的前段、结果帧、占位符之后的后段。
/// </summary>
/// <remarks>
///     结果帧就是结果文本本身（例如 <c>张三</c>），用强调色、1.6 倍字号、SemiBold 高亮，
///     不再额外包一层大括号：<c>{result}</c> 只是模板占位符语法，不是要展示给用户的内容。
/// </remarks>
internal readonly record struct FullscreenResultLine(string Prefix, string Result, string Suffix);

/// <summary>
///     全屏结果文案的排版与打字机动画。
/// </summary>
/// <remarks>
///     <para>
///         逐字动画完全手写在 UI 线程的帧循环里（与 <c>DrawAnimationHelper.AnimateAsync</c> 同一范式），
///         不使用 Avalonia 声明式 <c>Animation</c>：字符数是运行期才知道的，声明式动画无法表达。
///     </para>
///     <para>
///         文本按 **grapheme**（文本元素）切分，避免拆坏 emoji、代理对与组合字符。
///         <see cref="FullscreenTextTemplate.ResultPlaceholder" /> 整体一次性出现，绝不逐字拆开。
///     </para>
///     <para>
///         吐字速度完全由设置里的"每秒字数"决定（每秒字数 → 每字间隔 = 1000 / 字数），
///         长文案就是慢慢吐完，**绝不**为了压时长而跳字或提前收尾。
///     </para>
/// </remarks>
internal static class FullscreenTextAnimation
{
    /// <summary>逐字循环的帧间隔：只决定"检查进度"的频率，不决定吐字速度。</summary>
    private const int FrameMilliseconds = 16;

    /// <summary>每秒字数下限，防止设置被写坏后间隔变成无穷大。</summary>
    private const int MinCharactersPerSecond = 1;

    /// <summary>每秒字数上限，防止设置被写坏后一帧吐完整句。</summary>
    private const int MaxCharactersPerSecond = 120;

    /// <summary>结果帧相对正文字号的倍率。</summary>
    private const double ResultFontScale = 1.6;

    /// <summary>
    ///     把快照渲染成一行行文案。多人结果时前段只出现在第一行、后段只出现在最后一行。
    /// </summary>
    /// <param name="snapshot">结果快照。</param>
    /// <param name="includeTemplate">
    ///     是否带上模板的前段/后段。只有打字机动画需要它；
    ///     直接显示、网格都只看自己的画面，传入 <c>false</c> 只留结果帧。
    /// </param>
    public static IReadOnlyList<FullscreenResultLine> BuildLines(
        FullscreenResultSnapshot snapshot,
        bool includeTemplate = true)
    {
        var texts = (snapshot.Results ?? Array.Empty<FullscreenResultItem>())
            .Select(item => item.DisplayText ?? string.Empty)
            .ToList();
        if (texts.Count == 0)
            texts.Add(string.Empty);

        var template = snapshot.TemplateText ?? string.Empty;
        var prefix = string.Empty;
        var suffix = string.Empty;
        if (includeTemplate && snapshot.ShowText)
        {
            if (FullscreenTextTemplate.HasSinglePlaceholder(template))
            {
                (prefix, suffix) = FullscreenTextTemplate.Split(template);
            }
            else if (!string.IsNullOrEmpty(template)
                     && !template.Contains(FullscreenTextTemplate.ResultPlaceholder, StringComparison.Ordinal))
            {
                // 模板里没有占位符时退化成"整句前缀 + 结果帧"，绝不显示 {result} 字面量。
                prefix = template;
            }
        }

        var lines = new List<FullscreenResultLine>(texts.Count);
        if (texts.Count == 1)
        {
            lines.Add(new FullscreenResultLine(prefix, texts[0], suffix));
            return lines;
        }

        for (var index = 0; index < texts.Count; index++)
        {
            lines.Add(new FullscreenResultLine(
                index == 0 ? prefix : string.Empty,
                texts[index],
                index == texts.Count - 1 ? suffix : string.Empty));
        }

        return lines;
    }

    /// <summary>结果帧文本：就是结果本身（高亮由 Run 的字号/字重/颜色负责）。</summary>
    public static string BuildResultFrame(string? result) => result ?? string.Empty;

    /// <summary>
    ///     直接在 <paramref name="host" /> 里铺满整句（Direct），或作为网格的兜底结果。
    /// </summary>
    public static void FillStatic(
        StackPanel host,
        IReadOnlyList<FullscreenResultLine> lines,
        double bodyFontSize,
        Color bodyColor,
        Color accentColor)
    {
        host.Children.Clear();
        var bodyBrush = new SolidColorBrush(bodyColor);
        var accentBrush = new SolidColorBrush(accentColor);

        foreach (var line in lines)
        {
            var block = BuildLineBlock(line, bodyFontSize, bodyBrush, accentBrush,
                out var prefixRun, out var resultRun, out var suffixRun);
            prefixRun.Text = line.Prefix;
            resultRun.Text = BuildResultFrame(line.Result);
            suffixRun.Text = line.Suffix;
            host.Children.Add(block);
        }
    }

    /// <summary>
    ///     打字机：前段逐字、结果帧整体出现、后段逐字，返回时文本已经吐完。
    /// </summary>
    /// <param name="charactersPerSecond">
    ///     每秒吐出的字符数（来自全屏设置）。每秒字数越低吐得越慢，长文案就播得越久，
    ///     不会为了控制总时长而跳字。
    /// </param>
    public static async Task PlayAsync(
        StackPanel host,
        IReadOnlyList<FullscreenResultLine> lines,
        double bodyFontSize,
        Color bodyColor,
        Color accentColor,
        int charactersPerSecond,
        Action? completedCue,
        CancellationToken cancellationToken)
    {
        host.Children.Clear();
        var bodyBrush = new SolidColorBrush(bodyColor);
        var accentBrush = new SolidColorBrush(accentColor);
        var cursors = new List<TypewriterCursor>(lines.Count * 3);

        foreach (var line in lines)
        {
            var block = BuildLineBlock(line, bodyFontSize, bodyBrush, accentBrush,
                out var prefixRun, out var resultRun, out var suffixRun);
            host.Children.Add(block);

            cursors.Add(TypewriterCursor.ForText(prefixRun, line.Prefix));
            // 结果帧整体一次性出现，所以只占一个"字"的节奏。
            cursors.Add(TypewriterCursor.ForWhole(resultRun, BuildResultFrame(line.Result)));
            cursors.Add(TypewriterCursor.ForText(suffixRun, line.Suffix));
        }

        var totalUnits = cursors.Sum(cursor => cursor.Steps.Length);
        if (totalUnits == 0)
        {
            RevealAll(cursors);
            InvokeCue(completedCue);
            return;
        }

        var perSecond = Math.Clamp(charactersPerSecond, MinCharactersPerSecond, MaxCharactersPerSecond);
        var stopwatch = Stopwatch.StartNew();
        var revealed = 0;
        var cursorIndex = 0;
        try
        {
            while (revealed < totalUnits)
            {
                // 用真实经过的时间换算"应该已经吐了多少字"：Task.Delay 的抖动不会累积成速度偏差。
                var due = Math.Min(totalUnits, (int)(stopwatch.Elapsed.TotalSeconds * perSecond) + 1);
                while (revealed < due)
                {
                    while (cursorIndex < cursors.Count && cursors[cursorIndex].Done)
                        cursorIndex++;
                    if (cursorIndex >= cursors.Count)
                        break;

                    cursors[cursorIndex].RevealNext();
                    revealed++;
                }

                if (revealed >= totalUnits)
                    break;

                await Task.Delay(FrameMilliseconds, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // 取消时立刻停帧并保留当前逐字画面，不补全文本。
            return;
        }

        if (cancellationToken.IsCancellationRequested)
            return;

        RevealAll(cursors);
        InvokeCue(completedCue);
    }

    private static void InvokeCue(Action? cue)
    {
        try
        {
            cue?.Invoke();
        }
        catch
        {
            // 语音回调失败绝不能影响结果呈现。
        }
    }

    private static void RevealAll(List<TypewriterCursor> cursors)
    {
        foreach (var cursor in cursors)
            cursor.RevealAll();
    }

    private static TextBlock BuildLineBlock(
        FullscreenResultLine line,
        double bodyFontSize,
        IBrush bodyBrush,
        IBrush accentBrush,
        out Run prefixRun,
        out Run resultRun,
        out Run suffixRun)
    {
        var block = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            FontSize = bodyFontSize,
            Foreground = bodyBrush
        };

        prefixRun = CreateRun(bodyFontSize, FontWeight.Normal, bodyBrush);
        resultRun = CreateRun(bodyFontSize * ResultFontScale, FontWeight.SemiBold, accentBrush);
        suffixRun = CreateRun(bodyFontSize, FontWeight.Normal, bodyBrush);
        block.Inlines?.Add(prefixRun);
        block.Inlines?.Add(resultRun);
        block.Inlines?.Add(suffixRun);
        return block;
    }

    private static Run CreateRun(double fontSize, FontWeight weight, IBrush foreground)
        => new()
        {
            Text = string.Empty,
            FontSize = fontSize,
            FontWeight = weight,
            Foreground = foreground
        };

    /// <summary>按文本元素切分，返回每一步累加后的完整文本（逐帧直接赋值，避免每帧重拼字符串）。</summary>
    private static string[] BuildCumulativeSteps(string text)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        var starts = StringInfo.ParseCombiningCharacters(text);
        var steps = new string[starts.Length];
        var builder = new StringBuilder(text.Length);
        for (var index = 0; index < starts.Length; index++)
        {
            var start = starts[index];
            var end = index + 1 < starts.Length ? starts[index + 1] : text.Length;
            builder.Append(text, start, end - start);
            steps[index] = builder.ToString();
        }

        return steps;
    }

    /// <summary>一行里的一段文本的逐字进度。</summary>
    private sealed class TypewriterCursor
    {
        private Run? _run;

        public string[] Steps { get; private init; } = [];
        public int Position { get; private set; }
        public bool Done => Position >= Steps.Length;

        public static TypewriterCursor ForText(Run run, string text) => new()
        {
            _run = run,
            Steps = BuildCumulativeSteps(text)
        };

        public static TypewriterCursor ForWhole(Run run, string text) => new()
        {
            _run = run,
            Steps = string.IsNullOrEmpty(text) ? [] : [text]
        };

        public void RevealNext()
        {
            if (_run is null || Done)
                return;

            _run.Text = Steps[Position];
            Position++;
        }

        public void RevealAll()
        {
            if (_run is null || Steps.Length == 0)
                return;

            _run.Text = Steps[^1];
            Position = Steps.Length;
        }
    }
}
