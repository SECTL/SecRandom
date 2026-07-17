using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Core.Models;
using SecRandom.Core.Plugins;
using SecRandom.Core.Services;

namespace SecRandom.Core.Extensions.Registry;

public static class PagesRegistryExtensions
{
    public static IServiceCollection AddMainPage<T>(this IServiceCollection services, string name) where T : UserControl
    {
        return services.AddPageTo<T>(PagesRegistryService.MainItems, name);
    }

    public static IServiceCollection AddMainPageSeparator(this IServiceCollection services,
        PageLocation location = PageLocation.Top)
    {
        PagesRegistryService.MainItems.Add(new PageInfo(true, location));
        return services;
    }

    public static IServiceCollection AddSettingsPage<T>(this IServiceCollection services, string name)
        where T : UserControl
    {
        return services.AddPageTo<T>(PagesRegistryService.SettingsItems, name);
    }

    public static IServiceCollection AddPluginMainPage(this IServiceCollection services,
        PluginPageRegistration registration)
    {
        registration.Validate();
        return services.AddPluginPageTo(PagesRegistryService.MainItems, registration);
    }

    public static IServiceCollection AddPluginSettingsPage(this IServiceCollection services,
        PluginPageRegistration registration)
    {
        registration.Validate();
        return services.AddPluginPageTo(PagesRegistryService.SettingsItems, registration);
    }

    public static IServiceCollection AddSettingsPageSeparator(this IServiceCollection services,
        PageLocation location = PageLocation.Top, bool isHide = false)
    {
        PagesRegistryService.SettingsItems.Add(new PageInfo(true, location) { IsHide = isHide });
        return services;
    }

    public static IServiceCollection AddGroup(this IServiceCollection services, PageGroupInfo info)
    {
        if (PagesRegistryService.GroupItems.FirstOrDefault(x => x.Id == info.Id) != null)
            throw new ArgumentException($"此设置页面id {info.Id} 已经被占用。");

        PagesRegistryService.GroupItems.Add(info);
        return services;
    }

    private static IServiceCollection AddPageTo<T>(this IServiceCollection services, IList<PageInfo> list, string name)
        where T : UserControl
    {
        var type = typeof(T);
        if (type.GetCustomAttributes(false).FirstOrDefault(x => x is PageInfo) is not PageInfo info)
            throw new ArgumentException($"无法注册设置页面 {type.FullName}，因为设置页面没有注册信息。");

        if (list.FirstOrDefault(x => x.Id == info.Id) != null) throw new ArgumentException($"此设置页面id {info.Id} 已经被占用。");

        info.Name = name;
        info.SettingsPageType = typeof(T);
        services.AddKeyedTransient<UserControl, T>(info.Id);
        list.Add(info);
        return services;
    }

    private static IServiceCollection AddPluginPageTo(this IServiceCollection services, IList<PageInfo> list,
        PluginPageRegistration registration)
    {
        if (list.FirstOrDefault(x => x.Id == registration.PageId) != null)
            throw new ArgumentException($"此设置页面id {registration.PageId} 已经被占用。");

        var info = new PageInfo(
            registration.PageId,
            registration.IconGlyph,
            registration.GroupId,
            registration.Location,
            registration.IsHide,
            registration.UseFullWidth,
            registration.HidePageTitle)
        {
            Name = registration.Name,
            SettingsPageType = registration.PageType
        };

        services.AddKeyedTransient(typeof(UserControl), registration.PageId, (provider, _) =>
            (UserControl)ActivatorUtilities.CreateInstance(provider, registration.PageType));
        list.Add(info);
        return services;
    }
}
