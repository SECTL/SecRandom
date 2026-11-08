using System.Collections.ObjectModel;
using Avalonia.Controls;
using SecRandom.Core.Services.Profiles;
using SecRandom.Shared;

namespace SecRandom.Views.SettingsPages.Picking;

/// <summary>
///     抽取设置页「默认抽取名单 / 默认奖池」下拉框的数据源。首项恒为「不使用名单」，其余是磁盘上现有的档案文件。
/// </summary>
/// <remarks>
///     <para>
///         下拉框的候选值由这里直接维护，选中项也由这里按 <see cref="Avalonia.Controls.ComboBox.SelectedIndex" />
///         同步，而不是把 <c>SelectedItem</c> 绑定到配置字符串上：候选值重建时下拉框会先清空选中项，绑定会把空值写回配置，
///         默认名单就会被瞬时空掉，这正是「选了之后重启又丢掉」的来源。
///     </para>
///     <para>
///         「不使用名单」在配置里存的是 <see cref="RosterSelection.None" /> 哨兵，界面显示的是页面本地化文案；
///         没有真实档案时这一项只是唯一可选项，不写回配置，等以后建了名单仍会自动落到第一份。
///     </para>
/// </remarks>
public sealed class DefaultRosterSettingSource
{
    private readonly string _listDirectory;
    private readonly Func<string> _readValue;
    private readonly Action<string> _writeValue;
    private readonly Func<bool> _isPreviewMode;
    private bool _syncing;

    public DefaultRosterSettingSource(
        string listDirectory,
        string noListLabel,
        Func<string> readValue,
        Action<string> writeValue,
        Func<bool> isPreviewMode)
    {
        _listDirectory = listDirectory;
        _readValue = readValue;
        _writeValue = writeValue;
        _isPreviewMode = isPreviewMode;
        NoListOption = noListLabel;
        Options.Add(noListLabel);
    }

    /// <summary>下拉框候选值：第 0 项是「不使用名单」，其后是真实档案名。</summary>
    public ObservableCollection<string> Options { get; } = [];

    /// <summary>「不使用名单」的显示文案，用于把下拉框选中项映射回哨兵。</summary>
    public string NoListOption { get; }

    /// <summary>重建候选值，并把选中项落回配置里的默认值（未配置时落到第一份并写回配置）。</summary>
    public void Refresh(ComboBox? selector)
    {
        var names = GetListNames();
        _syncing = true;
        try
        {
            Options.Clear();
            Options.Add(NoListOption);
            foreach (var name in names)
                Options.Add(name);

            ApplySelection(selector, Resolve(names));
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>配置里的默认值在别处被改动（导入、另一页改）后，把下拉框选中项同步过去。</summary>
    public void Sync(ComboBox? selector)
    {
        if (_syncing)
            return;

        _syncing = true;
        try
        {
            var stored = _readValue();
            ApplySelection(selector, RosterSelection.IsNone(stored)
                ? NoListOption
                : Options.Contains(stored) ? stored : NoListOption);
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>用户在下拉框里换了默认值：写回配置（页面收到配置变更后会保存）。</summary>
    public void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncing || sender is not ComboBox { SelectedItem: string selected })
            return;

        var next = string.Equals(selected, NoListOption, StringComparison.Ordinal)
            ? RosterSelection.None
            : selected;

        // 只有存在真实档案时，「不使用名单」才是用户做出的选择；否则它只是唯一可选项。
        if (RosterSelection.IsNone(next) && Options.Count <= 1)
            return;

        if (string.Equals(_readValue(), next, StringComparison.Ordinal))
            return;

        _writeValue(next);
    }

    private void ApplySelection(ComboBox? selector, string value)
    {
        if (selector is null)
            return;

        var index = Options.IndexOf(value);
        selector.SelectedIndex = index >= 0 ? index : 0;
    }

    private string Resolve(IReadOnlyList<string> names)
    {
        var stored = _readValue()?.Trim() ?? string.Empty;
        if (RosterSelection.IsNone(stored))
            return NoListOption;

        if (stored.Length > 0 && names.Contains(stored, StringComparer.Ordinal))
            return stored;

        if (names.Count == 0)
            return NoListOption;

        // 从未配置过（空串）才写第一份；用户明确选过「不使用名单」时那条哨兵保持不变。
        if (stored.Length == 0 && _isPreviewMode() != true)
            _writeValue(names[0]);

        return names[0];
    }

    private List<string> GetListNames()
    {
        var directory = Utils.GetDirectoryPath("list", _listDirectory);
        if (!Directory.Exists(directory))
            return [];

        return Directory.GetFiles(directory, "*.json")
            .OrderBy(Path.GetFileName)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OfType<string>()
            .ToList();
    }
}