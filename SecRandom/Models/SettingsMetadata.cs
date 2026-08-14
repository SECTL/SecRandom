namespace SecRandom.Models;

public class SettingsMetadata
{
    public bool IsPage { get; set; } = false;
    public string PageId { get; set; } = string.Empty;
    public string PageName { get; set; } = string.Empty;

    public bool IsCategory { get; set; } = false;
    public string CategoryId { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;

    public string Id { get; set; } = string.Empty;
    public string ControlId { get; set; } = string.Empty;
    public string CategoryControlId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public override string ToString()
    {
        if (IsPage)
            return PageName;

        if (IsCategory)
        {
            if (Description == string.Empty)
                return $@"{PageName} - {CategoryName}";

            return $@"{PageName} - {CategoryName} | {Description}";
        }

        if (Description == string.Empty)
            return $@"{PageName} - {CategoryName} - {Name}";

        return $@"{PageName} - {CategoryName} - {Name} | {Description}";
    }
}
