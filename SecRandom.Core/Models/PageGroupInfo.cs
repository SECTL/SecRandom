namespace SecRandom.Core.Models;

public class PageGroupInfo
{
    public string Name { get; set; }
    public string Id { get; }
    public string IconGlyph { get; }
    
    public PageGroupInfo(string name, string id, string iconGlyph)
    {
        Name = name;
        Id = id;
        IconGlyph = iconGlyph;
    }
}