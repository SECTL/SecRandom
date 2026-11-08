using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using SecRandom.Core.Models.Notification;
using FullscreenResources = SecRandom.Langs.Fullscreen.Resources;

namespace SecRandom.Views.Fullscreen;

/// <summary>
///     "学号排布随机抽取"网格动画。
/// </summary>
/// <remarks>
///     <para>
///         <see cref="ItemsControl" /> + <see cref="WrapPanel" /> 按冻结池顺序铺满窗口，
///         字体随人数自适应；卡片带 <c>result-animation-item</c> 样式类，与主页面结果区同一视觉语言。
///     </para>
///     <para>
///         随机高亮 1.5–2.5 秒后，末帧把真实结果换成放大 + 发光的定格卡片；多人结果逐个定格，
///         每次间隔 250ms。取消时保留当前高亮画面，不切换到结果。
///     </para>
/// </remarks>
internal static class FullscreenGridAnimation
{
    /// <summary>卡片数量上限：候选人过多时只铺前 N 个，避免整屏文字无法阅读。</summary>
    public const int MaxCards = 96;

    private const int HighlightIntervalMilliseconds = 70;
    private const int RevealGapMilliseconds = 250;
    private const int SettleMilliseconds = 320;

    /// <summary>
    ///     在 <paramref name="host" /> 里铺开候选人并播放随机高亮，返回时结果已定格（或已取消）。
    /// </summary>
    /// <param name="bodyColor">正文颜色：深色主题近白，浅色主题近黑（跟随主题的文字必须换色，否则浅底白字看不清）。</param>
    /// <param name="lightTheme">当前是否浅色主题，用来选卡片的半透明底/描边。</param>
    public static async Task PlayAsync(
        Panel host,
        IReadOnlyList<FullscreenResultItem> pool,
        IReadOnlyList<FullscreenResultItem> results,
        double availableWidth,
        double availableHeight,
        Color accentColor,
        Color bodyColor,
        bool lightTheme,
        int durationMs,
        Action? voiceCue,
        CancellationToken cancellationToken)
    {
        host.Children.Clear();
        // 卡片中奖时会放大 1.08 并带一圈 Blur 36 / Spread 4 的发光，光晕会画到卡片外。
        // 只要有任何一层裁掉超出范围的内容，光晕就被切出一条直边，看着像"卡片被削了"。
        host.ClipToBounds = false;

        var displayed = (pool ?? Array.Empty<FullscreenResultItem>())
            .Where(item => item is not null)
            .Take(MaxCards)
            .ToList();
        if (displayed.Count == 0 || availableWidth <= 0 || availableHeight <= 0)
            return;

        var wrap = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ClipToBounds = false
        };

        // 按可用面积估算每个格子的边长，再反推字号，保证人越多字越小但仍可读。
        // 系数从 0.72 降到 0.62：网格铺得太满时最外圈卡片贴住可视区边缘，
        // 中奖卡的发光（Blur 36 + Spread 4 + 放大 1.08）会被边缘切掉，留白比塞满更重要。
        var area = Math.Max(1, availableWidth * availableHeight * 0.62);
        var perCard = area / displayed.Count;
        var fontSize = Math.Clamp(Math.Sqrt(perCard) * 0.3, 11, 64);
        var cardPadding = Math.Clamp(fontSize * 0.42, 4, 22);
        var textMaxWidth = Math.Clamp(availableWidth * 0.22, 80, 320);

        // 卡片配色跟随主题：深色主题用"白底低透明度"，浅色主题换成"黑底低透明度"。
        var defaultBackground = new SolidColorBrush(lightTheme
            ? Color.FromArgb(0x1A, 0x00, 0x00, 0x00)
            : Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
        var defaultBorder = new SolidColorBrush(lightTheme
            ? Color.FromArgb(0x38, 0x00, 0x00, 0x00)
            : Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
        var defaultText = new SolidColorBrush(bodyColor);
        var highlightBackground = new SolidColorBrush(Color.FromArgb(0xB3, accentColor.R, accentColor.G, accentColor.B));
        var highlightBorder = new SolidColorBrush(accentColor);
        var highlightText = new SolidColorBrush(Color.FromArgb(0xFF, 0x14, 0x18, 0x22));
        var winnerBackground = new SolidColorBrush(accentColor);
        var winnerBorder = new SolidColorBrush(lightTheme
            ? Color.FromArgb(0xFF, 0x14, 0x18, 0x22)
            : Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
        var winnerText = new SolidColorBrush(Color.FromArgb(0xFF, 0x14, 0x18, 0x22));
        var glow = new BoxShadows(new BoxShadow
        {
            Blur = 36,
            Spread = 4,
            Color = Color.FromArgb(0xCC, accentColor.R, accentColor.G, accentColor.B)
        });

        var cards = new List<GridCard>(displayed.Count);
        foreach (var item in displayed)
        {
            var scale = new ScaleTransform(1, 1);
            var label = new TextBlock
            {
                Text = item.DisplayText ?? string.Empty,
                FontSize = fontSize,
                FontWeight = FontWeight.SemiBold,
                Foreground = defaultText,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = textMaxWidth,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var card = new Border
            {
                Background = defaultBackground,
                BorderBrush = defaultBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(Math.Clamp(fontSize * 0.3, 6, 18)),
                Padding = new Thickness(cardPadding * 1.3, cardPadding * 0.6, cardPadding * 1.3, cardPadding * 0.6),
                Margin = new Thickness(8),
                Child = label,
                RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                RenderTransform = scale
            };
            card.Classes.Add("result-animation-item");
            cards.Add(new GridCard
            {
                Card = card,
                Text = label,
                Scale = scale,
                DisplayText = item.DisplayText ?? string.Empty
            });
        }

        var itemsControl = new ItemsControl
        {
            ItemsPanel = new FuncTemplate<Panel?>(() => wrap),
            ItemsSource = cards.Select(card => card.Card).ToList(),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            // 光晕要画到卡片外，这层和它下面的 WrapPanel 都不能裁；
            // 40px 的留白刚好容下 Blur 36 + Spread 4，中奖卡的发光不会被切成直边。
            ClipToBounds = false,
            Margin = new Thickness(40)
        };
        host.Children.Add(itemsControl);

        if ((pool?.Count ?? 0) > MaxCards)
        {
            host.Children.Add(new TextBlock
            {
                // 卡片上限由 MaxCards 决定，文案里用 {0} 占位，改上限不必同步改资源。
                Text = string.Format(CultureInfo.CurrentCulture, FullscreenResources.Fullscreen_GridOverflow,
                    MaxCards),
                FontSize = 15,
                Foreground = new SolidColorBrush(Color.FromArgb(0x99, bodyColor.R, bodyColor.G, bodyColor.B)),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 10, 0, 0)
            });
        }

        var duration = Math.Clamp(durationMs, 900, 2500);
        var stopwatch = Stopwatch.StartNew();
        var previous = -1;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (stopwatch.Elapsed.TotalMilliseconds >= duration)
                    break;

                var index = PickRandomIndex(cards.Count, previous);
                if (index >= 0)
                {
                    if (previous >= 0 && previous < cards.Count)
                        ApplyDefault(cards[previous], defaultBackground, defaultBorder, defaultText);
                    ApplyHighlight(cards[index], highlightBackground, highlightBorder, highlightText);
                    previous = index;
                }

                await Task.Delay(HighlightIntervalMilliseconds, cancellationToken);
            }

            if (previous >= 0 && previous < cards.Count)
                ApplyDefault(cards[previous], defaultBackground, defaultBorder, defaultText);

            var winnerIndices = new List<int>();
            foreach (var result in results ?? Array.Empty<FullscreenResultItem>())
            {
                var index = FindCardIndex(cards, result?.DisplayText);
                if (index < 0 || winnerIndices.Contains(index))
                    continue;

                winnerIndices.Add(index);
                ApplyWinner(cards[index], winnerBackground, winnerBorder, winnerText, glow);
                await Task.Delay(RevealGapMilliseconds, cancellationToken);
            }

            // 有确定赢家时压暗其他卡片，让定格的赢家成为唯一焦点。
            if (winnerIndices.Count > 0)
            {
                for (var index = 0; index < cards.Count; index++)
                {
                    if (!winnerIndices.Contains(index))
                        cards[index].Card.Opacity = 0.4;
                }
            }

            InvokeCue(voiceCue);
            await Task.Delay(SettleMilliseconds, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // 取消时保留当前高亮画面，不补全、不压暗。
        }
    }

    private static int PickRandomIndex(int count, int exclude)
    {
        if (count <= 0)
            return -1;
        if (count == 1)
            return exclude == 0 ? -1 : 0;

        var index = Random.Shared.Next(count - 1);
        if (index >= exclude)
            index++;
        return index;
    }

    private static int FindCardIndex(IReadOnlyList<GridCard> cards, string? displayText)
    {
        if (string.IsNullOrEmpty(displayText))
            return -1;

        for (var index = 0; index < cards.Count; index++)
        {
            if (string.Equals(cards[index].DisplayText, displayText, StringComparison.Ordinal))
                return index;
        }

        return -1;
    }

    private static void ApplyDefault(GridCard card, IBrush background, IBrush border, IBrush text)
    {
        card.Card.Background = background;
        card.Card.BorderBrush = border;
        card.Card.BorderThickness = new Thickness(1);
        card.Card.BoxShadow = default;
        card.Card.Opacity = 1;
        card.Text.Foreground = text;
        card.Text.FontWeight = FontWeight.SemiBold;
        card.Scale.ScaleX = 1;
        card.Scale.ScaleY = 1;
    }

    private static void ApplyHighlight(GridCard card, IBrush background, IBrush border, IBrush text)
    {
        card.Card.Background = background;
        card.Card.BorderBrush = border;
        card.Card.BorderThickness = new Thickness(2);
        card.Card.BoxShadow = default;
        card.Text.Foreground = text;
    }

    private static void ApplyWinner(
        GridCard card,
        IBrush background,
        IBrush border,
        IBrush text,
        BoxShadows glow)
    {
        card.Card.Background = background;
        card.Card.BorderBrush = border;
        card.Card.BorderThickness = new Thickness(3);
        card.Card.BoxShadow = glow;
        card.Card.Opacity = 1;
        card.Text.Foreground = text;
        card.Text.FontWeight = FontWeight.Bold;
        card.Scale.ScaleX = 1.08;
        card.Scale.ScaleY = 1.08;
    }

    private static void InvokeCue(Action? cue)
    {
        try
        {
            cue?.Invoke();
        }
        catch
        {
            // 语音回调失败绝不能影响动画。
        }
    }

    /// <summary>一张结果卡片及其可动画部件。</summary>
    private sealed class GridCard
    {
        public required Border Card { get; init; }
        public required TextBlock Text { get; init; }
        public required ScaleTransform Scale { get; init; }
        public required string DisplayText { get; init; }
    }
}
