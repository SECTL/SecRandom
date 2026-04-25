using Microsoft.Extensions.DependencyInjection;
using SecRandom.Core.Abstraction.Controls;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Core.Services;

namespace SecRandom.Core.Extensions.Registry;

public static class AttachedSettingsRegistryExtensions
{
    public static IServiceCollection AddAttachedSettingsControl<T>(this IServiceCollection services)
        where T : AttachedSettingsControlBase
    {
        var type = typeof(T);
        
        if (type.GetCustomAttributes(false).FirstOrDefault(x => x is AttachedSettingsControlInfo) is not AttachedSettingsControlInfo info)
        {
            throw new InvalidOperationException($"无法注册附加设置控件 {type.FullName}，因为此控件有注册信息。");
        }
        
        if (type.GetCustomAttributes(false).FirstOrDefault(x => x is AttachedSettingsUsage) is not AttachedSettingsUsage usages)
        {
            throw new InvalidOperationException($"无法注册附加设置控件 {type.FullName}，因为此控件没有用法信息。");
        }

        if (AttachedSettingsRegistryService.RegisteredControls.FirstOrDefault(x => x.Guid == info.Guid) != null)
        {
            throw new InvalidOperationException($"此附加设置控件id {info.Guid} 已经被占用。");
        }
        
        services.AddKeyedTransient<AttachedSettingsControlBase, T>(info.Guid);
        
        info.AttachedSettingsControlType = typeof(T);
        info.Targets = usages.Targets;
        
        AttachedSettingsRegistryService.RegisteredControls.Add(info);
        
        if (usages.Targets.HasFlag(AttachedSettingsTargets.Student))
        {
            AttachedSettingsRegistryService.StudentAttachedSettingsControls.Add(info);
        }
        if (usages.Targets.HasFlag(AttachedSettingsTargets.Prize))
        {
            AttachedSettingsRegistryService.PrizeAttachedSettingsControls.Add(info);
        }

        return services;
    }
}