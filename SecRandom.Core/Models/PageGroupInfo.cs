using System.ComponentModel;

namespace SecRandom.Core.Models;

public class PageGroupInfo
{
    public PageGroupInfo(string name, [Localizable(false)] string id, [Localizable(false)] string iconGlyph)
    {
        Name = name;
        Id = id;
        IconGlyph = iconGlyph;
    }

    public string Name { get; set; }
    public string Id { get; }
    public string IconGlyph { get; }
}