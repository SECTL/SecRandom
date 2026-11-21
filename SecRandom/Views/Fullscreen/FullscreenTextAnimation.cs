using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
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
    /// <summary>逐字循环的最短等待：字已经到期时立刻回到循环顶部补字。</summary>
    private const int MinCharacterDelayMilliseconds = 1;

    /// <summary>
    ///     逐字循环的最长等待：防止设置被写坏后一次睡太久，取消/关窗的响应也更及时。
    ///     每次唤醒只处理"已经到期"的字，所以等待变短不会多吐字。
    /// </summary>
    private const int MaxCharacterDelayMilliseconds = 250;

    /// <summary>
    ///     结果文字列宽未知（调用方没给出舞台宽度）时的兜底：<c>0</c> 表示不做固定列宽，
    ///     文本框按内容自适应，行为与旧实现一致。
    /// </summary>
    private const double NoColumnWidth = 0;

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

    /// <summary>结果之间的分隔符：全角空格，比逗号中性，也不会在结尾留下标点。</summary>
    private const string SeparatorText = "　　";

    /// <summary>
    ///     直接在 <paramref name="host" /> 里铺成**一段连续文字**（Direct），或作为网格的兜底结果。
    /// </summary>
    /// <remarks>
    ///     多人结果不再每人一行（用户 m03587）：整段文字按可用宽度自动换行，字号自适应之后
    ///     30 个人也能在一屏里排开。头像通过 <see cref="InlineUIContainer" /> 内联在该成员名字左侧。
    /// </remarks>
    /// <param name="textColumnWidth">
    ///     结果文字的固定列宽（由窗口按舞台宽度算出）。打字机与直接显示必须用同一个宽度，
    ///     否则换行结果会和动画结束时不一致；传 <c>0</c> 时退回按内容自适应。
    /// </param>
    public static void FillStatic(
        StackPanel host,
        IReadOnlyList<FullscreenResultLine> lines,
        double bodyFontSize,
        Color bodyColor,
        Color accentColor,
        IReadOnlyList<Control?>? avatars = null,
        double textColumnWidth = NoColumnWidth)
    {
        host.Children.Clear();
        var bodyBrush = new SolidColorBrush(bodyColor);
        var accentBrush = new SolidColorBrush(accentColor);
        var block = CreateFlowingBlock(bodyFontSize, bodyBrush, textColumnWidth);

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var prefixRun = CreateRun(bodyFontSize, FontWeight.Normal, bodyBrush);
            prefixRun.Text = line.Prefix;
            block.Inlines?.Add(prefixRun);

            // 头像紧挨在名字左侧：夹在模板前段与结果帧之间，"恭喜 头像 张三" 才读得顺。
            AddAvatarInline(block, avatars, index);

            var resultRun = CreateRun(bodyFontSize * ResultFontScale, FontWeight.SemiBold, accentBrush);
            resultRun.Text = BuildResultFrame(line.Result);
            block.Inlines?.Add(resultRun);

            if (index < lines.Count - 1)
                block.Inlines?.Add(CreateSeparatorRun(bodyFontSize, bodyBrush));

            if (!string.IsNullOrEmpty(line.Suffix))
            {
                var suffixRun = CreateRun(bodyFontSize, FontWeight.Normal, bodyBrush);
                suffixRun.Text = line.Suffix;
                block.Inlines?.Add(suffixRun);
            }
        }

        host.Children.Add(block);
    }

    /// <summary>
    ///     打字机：前段逐字、结果帧整体出现、后段逐字，返回时文本已经吐完。
    /// </summary>
    /// <param name="charactersPerSecond">
    ///     每秒吐出的字符数（来自全屏设置）。每秒字数越低吐得越慢，长文案就播得越久，
    ///     不会为了控制总时长而跳字。
    /// </param>
    /// <param name="textColumnWidth">
    ///     结果文字的固定列宽（由窗口按舞台宽度算出）。传 <c>0</c> 时退回按内容自适应。
    /// </param>
    public static async Task PlayAsync(
        StackPanel host,
        IReadOnlyList<FullscreenResultLine> lines,
        double bodyFontSize,
        Color bodyColor,
        Color accentColor,
        int charactersPerSecond,
        Action? completedCue,
        CancellationToken cancellationToken,
        IReadOnlyList<Control?>? avatars = null,
        double textColumnWidth = NoColumnWidth)
    {
        host.Children.Clear();
        var bodyBrush = new SolidColorBrush(bodyColor);
        var accentBrush = new SolidColorBrush(accentColor);
        var cursors = new List<TypewriterCursor>(lines.Count * 3);
        var block = CreateFlowingBlock(bodyFontSize, bodyBrush, textColumnWidth);

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var prefixText = line.Prefix ?? string.Empty;
            var resultText = BuildResultFrame(line.Result);

            var prefixRun = CreateRun(bodyFontSize, FontWeight.Normal, bodyBrush);
            prefixRun.Text = prefixText;
            block.Inlines?.Add(prefixRun);
            cursors.Add(TypewriterCursor.ForText(prefixRun, prefixText));

            // 头像先占位：整段文字连续排布，不能等吐完再插，否则会整段重排。
            AddAvatarInline(block, avatars, index);

            var resultRun = CreateRun(bodyFontSize * ResultFontScale, FontWeight.SemiBold, accentBrush);
            resultRun.Text = resultText;
            block.Inlines?.Add(resultRun);
            // 结果帧整体一次性出现，所以只占一个"字"的节奏。
            cursors.Add(TypewriterCursor.ForWhole(resultRun, resultText));

            if (index < lines.Count - 1)
            {
                var separatorRun = CreateSeparatorRun(bodyFontSize, bodyBrush);
                block.Inlines?.Add(separatorRun);
                cursors.Add(TypewriterCursor.ForText(separatorRun, SeparatorText));
            }

            if (!string.IsNullOrEmpty(line.Suffix))
            {
                var suffixRun = CreateRun(bodyFontSize, FontWeight.Normal, bodyBrush);
                suffixRun.Text = line.Suffix;
                block.Inlines?.Add(suffixRun);
                cursors.Add(TypewriterCursor.ForText(suffixRun, line.Suffix));
            }
        }

        // 先用"整段文字"把最终高度量出来，再把文字退回空：从第一帧起文本框就是最终尺寸，
        // 吐字过程中就不会重新换行、重新居中，也不会因为多出一行把已经显示的字顶上去。
        AddTypingBlock(host, block, cursors);

        var totalUnits = 0;
        foreach (var cursor in cursors)
            totalUnits += cursor.Steps.Length;

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

                // 睡到下一个字真正到期为止，而不是死循环式地每 16ms 空转：每秒字数低的时候
                // 空转不产出画面，却会反复占用 UI 线程的派发时隙，并把吐字节奏压在帧边界上。
                var nextDueMilliseconds = revealed / (double)perSecond * 1000.0;
                var waitMilliseconds = nextDueMilliseconds - stopwatch.Elapsed.TotalMilliseconds;
                var delayMilliseconds = WaitMilliseconds(waitMilliseconds);
                await Task.Delay(delayMilliseconds, cancellationToken);
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

    /// <summary>把"还有多久到下一个字"换算成可以交给 <see cref="Task.Delay(int, CancellationToken)" /> 的毫秒数。</summary>
    private static int WaitMilliseconds(double waitMilliseconds)
    {
        if (!double.IsFinite(waitMilliseconds) || waitMilliseconds <= 0)
            return MinCharacterDelayMilliseconds;

        return (int)Math.Clamp(
            Math.Ceiling(waitMilliseconds),
            MinCharacterDelayMilliseconds,
            MaxCharacterDelayMilliseconds);
    }

    /// <summary>整段连续文字的文本框：按固定列宽自动换行、居中排版（多人结果不再一人一行）。</summary>
    /// <remarks>
    ///     列宽固定是这套动画的性能前提：文本框尺寸不再随吐字变化，父级不必每帧重新测量/重新居中，
    ///     已经显示的换行也不会因为多一个字而重排。
    /// </remarks>
    private static TextBlock CreateFlowingBlock(
        double bodyFontSize,
        IBrush bodyBrush,
        double textColumnWidth = NoColumnWidth)
    {
        var block = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontSize = bodyFontSize,
            Foreground = bodyBrush
        };

        if (double.IsFinite(textColumnWidth) && textColumnWidth > 0)
            block.Width = textColumnWidth;

        return block;
    }

    /// <summary>
    ///     把打字机文本框放进一个"最终尺寸"的固定画框里，然后清空文字准备逐字显示。
    /// </summary>
    /// <remarks>
    ///     画框高度用整段文字量出来的最终高度：文字从画框顶部开始生长，框本身既不增高也不居中漂移，
    ///     所以已显示的字符在整段动画里一动不动。测量失败（例如字体还没就绪）时退回旧的
    ///     内容自适应高度——画面仍然正确，只是行数变化时会有轻微上移。
    /// </remarks>
    private static void AddTypingBlock(StackPanel host, TextBlock block, List<TypewriterCursor> cursors)
    {
        // 先挂到真实宿主里再量：文本框只有在窗口里才继承到实际字体（MiSans），
        // 脱离窗口用 DesiredSize 量出来的行高和渲染时可能对不上，最后一行就会被啃掉或留白。
        host.Children.Add(block);

        var finalHeight = MeasureAttachedHeight(host, block);
        if (finalHeight > 0)
        {
            host.Children.Remove(block);
            block.VerticalAlignment = VerticalAlignment.Top;
            var frame = new Panel { Height = finalHeight };
            if (double.IsFinite(block.Width) && block.Width > 0)
                frame.Width = block.Width;
            frame.Children.Add(block);
            host.Children.Add(frame);
        }

        foreach (var cursor in cursors)
            cursor.Reset();
    }

    /// <summary>量出文本框在真实宿主里排版后的高度；列宽未知或量不出来时返回 <c>0</c>。</summary>
    /// <remarks>测量失败只意味着退回"内容自适应高度"，画面仍然正确，只是行数变化时会有轻微上移。</remarks>
    private static double MeasureAttachedHeight(StackPanel host, TextBlock block)
    {
        if (!double.IsFinite(block.Width) || block.Width <= 0)
            return 0;

        try
        {
            // 跑一次布局把文本框真正排出来（含继承字体/换行），读它的实际高度。
            host.UpdateLayout();
            var height = block.Bounds.Height;
            if (double.IsFinite(height) && height > 0)
                return height;

            // 布局还没跑起来（例如预览窗口尚未显示）时退回直接测量。
            block.Measure(new Size(block.Width, double.PositiveInfinity));
            height = block.DesiredSize.Height;
            return double.IsFinite(height) && height > 0 ? height : 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>每个成员的分隔符：小一号的正文色，只负责把相邻结果拉开。</summary>
    private static Run CreateSeparatorRun(double bodyFontSize, IBrush bodyBrush)
    {
        var run = CreateRun(bodyFontSize * 0.9, FontWeight.Normal, bodyBrush);
        run.Text = SeparatorText;
        return run;
    }

    /// <summary>把头像内联到该成员名字左侧（没有头像的成员直接跳过）。</summary>
    private static void AddAvatarInline(
        TextBlock block,
        IReadOnlyList<Control?>? avatars,
        int index)
    {
        if (avatars is null || index >= avatars.Count)
            return;

        var avatar = avatars[index];
        if (avatar is null)
            return;

        block.Inlines?.Add(new InlineUIContainer
        {
            Child = avatar,
            BaselineAlignment = BaselineAlignment.Center
        });
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

        /// <summary>把文字退回空、进度归零：量完最终高度、开始逐字显示之前调用。</summary>
        public void Reset()
        {
            Position = 0;
            if (_run is not null)
                _run.Text = string.Empty;
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
