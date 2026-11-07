using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums;

namespace SecRandom.Views.SettingsPages.Picking;

/// <summary>
///     抽取设置页里那一行「默认记录组」：候选组、当前选中项，以及总开关关闭时整行不可选的显隐。
/// </summary>
/// <remarks>
///     <para>
///         点名、抽奖、闪抽三个抽取设置页共用这一个实现，因为三处的行为必须逐字一致：首项固定是
///         「默认记录组」（配置里写空串，表示只按未分组的记录计算），其余是当前档案已登记、或在记录里
///         出现过的组名。三页各写一份曾经让「空串到底代表什么」在三个地方慢慢走样。
///     </para>
///     <para>
///         两条细节是承重的。第一，重建 <see cref="Options" /> 时下拉框会先把选中项清成 <c>null</c>
///         再回写首项，用户并没有做任何选择，所以重建期间的写回一律忽略，否则每次进这个页面都会把
///         存下来的组名抹掉。第二，总开关关闭时不读记录组目录，配置里的组名原样留着，等开关重新
///         打开还能接着用；读取失败同样只降级成「默认记录组」，绝不因为一次读取失败就清掉配置。
///     </para>
///     <para>
///         选中的组可能已被删除或改名：此时把配置清回空串（= 默认记录组），因为删除组只会把记录
///         移回默认组，回落过去仍然是同一批历史，而不是让页面显示一个不存在的组名。
///     </para>
/// </remarks>
public sealed class DrawHistoryGroupSetting : ObservableObject
{
    private readonly IHistoryGroupCatalog _catalog;
    private readonly HistoryGroupKind _kind;
    private readonly Func<bool> _isFeatureEnabled;
    private readonly Func<bool> _isPreviewMode;
    private readonly Func<string> _read;
    private readonly Action<string> _write;
    private bool _rebuilding;

    /// <param name="defaultOption">首项文案（当前语言的「默认记录组」）。</param>
    /// <param name="kind">这一页抽取读写的记录组类别。</param>
    /// <param name="catalog">记录组目录。</param>
    /// <param name="isFeatureEnabled">历史记录组总开关是否打开。</param>
    /// <param name="isPreviewMode">设置预览模式：预览里绝不写配置。</param>
    /// <param name="read">读取本页配置里的组名（空串 = 默认记录组）。</param>
    /// <param name="write">写回本页配置的组名（空串 = 默认记录组）。</param>
    public DrawHistoryGroupSetting(
        string defaultOption,
        HistoryGroupKind kind,
        IHistoryGroupCatalog catalog,
        Func<bool> isFeatureEnabled,
        Func<bool> isPreviewMode,
        Func<string> read,
        Action<string> write)
    {
        DefaultOption = defaultOption;
        _kind = kind;
        _catalog = catalog;
        _isFeatureEnabled = isFeatureEnabled;
        _isPreviewMode = isPreviewMode;
        _read = read;
        _write = write;

        Options = [defaultOption];
    }

    /// <summary>首项固定是「默认记录组」，配置里的空串就是它。</summary>
    public string DefaultOption { get; }

    /// <summary>下拉框的候选值：默认记录组 + 目录里的组名。</summary>
    public ObservableCollection<string> Options { get; }

    /// <summary>总开关关闭时整行只显示说明，不再显示下拉框。</summary>
    public bool IsSelectorVisible => _isFeatureEnabled();

    /// <summary>配置里的组名与下拉框选项之间的映射；空串与首项「默认记录组」互为同一个值。</summary>
    public string Selected
    {
        get
        {
            var configured = _read();
            return string.IsNullOrWhiteSpace(configured) ? DefaultOption : configured;
        }
        set
        {
            if (_rebuilding || value is null)
                return;

            var resolved = string.Equals(value, DefaultOption, StringComparison.Ordinal)
                ? string.Empty
                : value.Trim();
            if (string.Equals(_read(), resolved, StringComparison.Ordinal))
                return;
            if (_isPreviewMode())
                return;

            _write(resolved);
            OnPropertyChanged(nameof(Selected));
        }
    }

    /// <summary>重建候选值；总开关关闭或目录读取失败时只留默认记录组。</summary>
    public void Refresh()
    {
        _rebuilding = true;
        try
        {
            Options.Clear();
            Options.Add(DefaultOption);

            if (_isFeatureEnabled())
                RebuildGroupNames();
        }
        finally
        {
            _rebuilding = false;
        }

        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(IsSelectorVisible));
    }

    /// <summary>配置里的组名被别处改了（另一页、导入、删除组），让下拉框重新取一次值。</summary>
    public void NotifySelectionChanged() => OnPropertyChanged(nameof(Selected));

    private void RebuildGroupNames()
    {
        IReadOnlyList<string> groups;
        try
        {
            groups = _catalog.GetGroupNames(_kind);
        }
        catch (Exception)
        {
            // 读不到目录只降级成「默认记录组」：这一页和抽取本身都还能用。
            return;
        }

        foreach (var group in groups
                     .Select(name => name.Trim())
                     .Where(name => !string.IsNullOrWhiteSpace(name))
                     .Distinct(StringComparer.CurrentCulture)
                     .OrderBy(name => name, StringComparer.CurrentCulture))
        {
            if (!Options.Contains(group))
                Options.Add(group);
        }

        // 组可能已被删除或改名：绝不把一个不存在的组名留在配置里，否则下次抽取会静默落到别的范围。
        var configured = _read();
        if (!string.IsNullOrWhiteSpace(configured)
            && !Options.Contains(configured.Trim())
            && !_isPreviewMode())
            _write(string.Empty);
    }
}
