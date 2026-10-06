using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services.Presentation;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.SubConfigs.Picking;

namespace SecRandom.Controls;

/// <summary>
///     "动画样式"下拉框：宿主内置的三种动画，加上插件通过
///     <see cref="IDrawAnimationContribution" /> 贡献的动画项。
///     <para>
///         用法：把本控件的 <see cref="SettingsProperty" /> 绑到设置页的抽签配置对象上，
///         选中项会写回 <see cref="DrawSettingsConfigBase.PluginAnimationId" />（内置项同时写回
///         <see cref="DrawSettingsConfigBase.AnimationStyle" />）。
///     </para>
/// </summary>
public partial class DrawAnimationStyleSelector : UserControl
{
    public static readonly StyledProperty<DrawSettingsConfigBase?> SettingsProperty =
        AvaloniaProperty.Register<DrawAnimationStyleSelector, DrawSettingsConfigBase?>(nameof(Settings));

    public DrawAnimationStyleSelector()
    {
        InitializeComponent();
        Model = new DrawAnimationSelectorModel();

        // 只把下拉框自己的 DataContext 指向模型，控件本身继续继承设置页的 DataContext。
        // 之前在这里写 DataContext = Model，会让设置页里 Settings="{Binding Settings}" 这条
        // 编译期绑定拿模型去转成设置页类型而失败（日志：Unable to cast object of type
        // 'SecRandom.Controls.DrawAnimationSelectorModel' to type '...SettingsPage'），
        // 结果是 Settings 永远为 null、选中项永远写不回配置。
        PART_Combo.DataContext = Model;

        // 设置页可能先于插件注册被构造，挂到视觉树时再取一次插件贡献项。
        AttachedToVisualTree += (_, _) => Model.Reload();
    }

    public DrawAnimationSelectorModel Model { get; }

    /// <summary>要读写的抽签配置对象。</summary>
    public DrawSettingsConfigBase? Settings
    {
        get => GetValue(SettingsProperty);
        set => SetValue(SettingsProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SettingsProperty)
            Model.Attach(change.GetNewValue<DrawSettingsConfigBase?>());
    }
}

/// <summary>
///     <see cref="DrawAnimationStyleSelector" /> 的绑定模型：维护下拉项列表，并把选中项写回配置。
/// </summary>
public sealed partial class DrawAnimationSelectorModel : ObservableObject
{
    private readonly List<DrawAnimationOption> _builtIn =
    [
        new(DrawAnimationSelection.Host, Langs.SettingsPages.Picking.Resources.O_AnimationDirectRotate,
            DrawAnimationStyleMode.DirectRotate),
        new(DrawAnimationSelection.Host, Langs.SettingsPages.Picking.Resources.O_AnimationFadeFloat,
            DrawAnimationStyleMode.FadeFloat),
        new(DrawAnimationSelection.Host, Langs.SettingsPages.Picking.Resources.O_AnimationHorizontalShake,
            DrawAnimationStyleMode.HorizontalShake)
    ];

    private DrawSettingsConfigBase? _settings;
    private bool _syncing;

    /// <summary>下拉项：内置三项在前，插件项按 Priority 降序排在其后。</summary>
    public ObservableCollection<DrawAnimationOption> Options { get; } = [];

    [ObservableProperty] private DrawAnimationOption? _selectedOption;

    /// <summary>把控件绑定到某个配置对象（切换时退订旧对象）。</summary>
    public void Attach(DrawSettingsConfigBase? settings)
    {
        if (ReferenceEquals(_settings, settings))
            return;

        if (_settings is not null)
            _settings.PropertyChanged -= OnSettingsPropertyChanged;

        _settings = settings;

        if (_settings is not null)
            _settings.PropertyChanged += OnSettingsPropertyChanged;

        Reload();
    }

    /// <summary>
    ///     重建下拉项：宿主内置三项 + 当前已注册的插件动画贡献项。插件在宿主启动时才注册，
    ///     所以设置页每次挂到视觉树都会再刷一次。
    /// </summary>
    public void Reload()
    {
        var selectedId = _settings?.PluginAnimationId;
        var selectedStyle = _settings?.AnimationStyle;

        Options.Clear();
        foreach (var option in _builtIn)
            Options.Add(option);

        foreach (var contribution in ResolveContributions().OrderByDescending(c => c.Priority))
        {
            if (string.IsNullOrWhiteSpace(contribution.Id))
                continue;

            if (Options.Any(o => string.Equals(o.Id, contribution.Id, StringComparison.OrdinalIgnoreCase)))
                continue;

            Options.Add(new DrawAnimationOption(contribution.Id, contribution.DisplayName, null, true));
        }

        // 列表重建后按原选择恢复，尽量保持用户看到的那一项不变。
        // 只有配置里本来就明确写着内置动画（host）时，才允许把下拉结果同步回配置。
        ApplySelection(
            ResolveOption(selectedId, selectedStyle),
            allowHostWriteBack: string.Equals(selectedId, DrawAnimationSelection.Host, StringComparison.Ordinal));
    }

    private DrawAnimationOption? ResolveOption(string? selectedId, DrawAnimationStyleMode? style)
    {
        if (!string.IsNullOrWhiteSpace(selectedId) &&
            !string.Equals(selectedId, DrawAnimationSelection.Host, StringComparison.Ordinal))
        {
            // 插件被卸载/禁用后这个 id 就不在列表里了：必须回落到内置动画，
            // 否则下拉会空着、配置也会永远卡在这个死值上。
            var contributed = Options.FirstOrDefault(o =>
                string.Equals(o.Id, selectedId, StringComparison.OrdinalIgnoreCase));
            if (contributed is not null)
                return contributed;
        }

        // 配置里没写过 id（旧配置 / 从没选过）：宿主的结果呈现服务本来就会让优先级最高的插件接管，
        // 所以这里也把那一项显示成选中，别让下拉看起来选的是内置动画。
        if (string.IsNullOrWhiteSpace(selectedId))
        {
            var firstContribution = Options.FirstOrDefault(o => o.IsPlugin);
            if (firstContribution is not null)
                return firstContribution;
        }

        return Options.FirstOrDefault(o => o.BuiltInStyle == style) ?? _builtIn.First();
    }

    private void ApplySelection(DrawAnimationOption? option, bool allowHostWriteBack)
    {
        _syncing = true;
        try
        {
            SelectedOption = option;

            if (_settings is null || option is null)
                return;

            if (option.IsPlugin)
            {
                // 插件项：把选择记进配置，重复写同一值没有副作用。
                if (!string.Equals(_settings.PluginAnimationId, option.Id, StringComparison.Ordinal))
                    _settings.PluginAnimationId = option.Id;
            }
            else if (allowHostWriteBack &&
                     !string.Equals(_settings.PluginAnimationId, DrawAnimationSelection.Host, StringComparison.Ordinal))
            {
                // 只有配置本来就选中内置动画时才回写 host。
                _settings.PluginAnimationId = DrawAnimationSelection.Host;
            }

            // 保存下来的插件动画已经不在了（插件被卸载/禁用）时这里只是"显示"回落到内置动画，
            // 绝不把配置静默改写成 host：那会直接关掉插件接管且不留任何日志。
            // 死 id 的清理由启动时的 AnimationSelectionFallback 负责（它会记日志）。
            if (option.BuiltInStyle is { } builtIn && _settings.AnimationStyle != builtIn)
                _settings.AnimationStyle = builtIn;
        }
        finally
        {
            _syncing = false;
        }
    }

    partial void OnSelectedOptionChanged(DrawAnimationOption? value)
    {
        if (_syncing || _settings is null || value is null)
            return;

        if (value.BuiltInStyle is { } style)
        {
            _settings.AnimationStyle = style;
            _settings.PluginAnimationId = DrawAnimationSelection.Host;
        }
        else
        {
            _settings.PluginAnimationId = value.Id;
        }
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_syncing)
            return;

        if (e.PropertyName is nameof(DrawSettingsConfigBase.PluginAnimationId)
            or nameof(DrawSettingsConfigBase.AnimationStyle))
            ApplySelection(
                ResolveOption(_settings?.PluginAnimationId, _settings?.AnimationStyle),
                allowHostWriteBack: false);
    }

    private static IEnumerable<IDrawAnimationContribution> ResolveContributions()
    {
        try
        {
            return IAppHost.Host?.Services.GetServices<IDrawAnimationContribution>()
                   ?? Enumerable.Empty<IDrawAnimationContribution>();
        }
        catch
        {
            return Enumerable.Empty<IDrawAnimationContribution>();
        }
    }
}
