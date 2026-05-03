using FluentAvalonia.UI.Controls;
using SecRandom.Core.Controls;
using SecRandom.Core.Enums;

namespace SecRandom.Core.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public class PageInfo : Attribute
{
    public bool IsSeparator { get; }

    public string Name { get; set; } = string.Empty;
    public string Id { get; }
    public string IconGlyph { get; }
    public string? GroupId { get; }
    public PageLocation Location { get; }
    
    public bool IsHide { get; }
    public bool UseFullWidth { get; }
    public bool HidePageTitle { get; }

    public Type? SettingsPageType { get; set; }
    
    public PageInfo(bool isSeparator, PageLocation location = PageLocation.Top)
    {
        if (isSeparator)
        {
            IsSeparator = true;
            
            Id = "separator";
            IconGlyph = "";
            Location = location;

            IsHide = false;
            UseFullWidth = false;
            HidePageTitle = false;
        }
        else
        {
            throw new ArgumentException("isSeparator 为 false!!!!!");
        }
    }
    
    public PageInfo(string id, string iconGlyph, string? groupId = null, PageLocation location = PageLocation.Top, bool isHide = false, bool useFullWidth = false, bool hidePageTitle = false)
    {
        IsSeparator = false;
        
        Id = id;
        IconGlyph = iconGlyph;
        GroupId = groupId;
        Location = location;

        IsHide = isHide;
        UseFullWidth = useFullWidth;
        HidePageTitle = hidePageTitle;
    }

    public NavigationViewItemBase ToNavigationViewItemBase()
    {
        if (IsSeparator)
        {
            return new NavigationViewItemSeparator();
        }

        return new NavigationViewItem
        {
            IconSource = new FluentIconSource(IconGlyph),
            Content = Name,
            Tag = this
        };
    }
}