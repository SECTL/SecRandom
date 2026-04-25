using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;

namespace SecRandom.Core.Abstraction.Controls;

public abstract class AttachedSettingsControlBase : UserControl
{
    public static readonly StyledProperty<AttachedSettingsTargets> TargetProperty = AvaloniaProperty.Register<AttachedSettingsControlBase, AttachedSettingsTargets>(
        nameof(Target));

    public AttachedSettingsTargets Target
    {
        get => GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }
    
    [NotNull] internal object? SettingsInternal { get; set; }

    public static AttachedSettingsControlBase? GetInstance(AttachedSettingsControlInfo info, ref object? settings)
    {
        var control = IAppHost.Host?.Services.GetKeyedService<AttachedSettingsControlBase>(info.Guid);
        if (control == null)
        {
            return null;
        }

        var baseType = info.AttachedSettingsControlType.BaseType;
        if (baseType?.GetGenericArguments().Length > 0)
        {
            var settingsType = baseType.GetGenericArguments().First();
            var settingsReal = settings ?? Activator.CreateInstance(settingsType);
            if (settingsReal is JsonElement json)
            {
                settingsReal = json.Deserialize(settingsType);
            }
            settings = settingsReal;

            control.SettingsInternal = settingsReal;
        }
        return control;
    }
}

public abstract class AttachedSettingsControlBase<T> : AttachedSettingsControlBase where T : class
{
    public T Settings => (SettingsInternal as T)!;
}