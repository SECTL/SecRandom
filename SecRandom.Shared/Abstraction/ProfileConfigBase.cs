using System.Text.Json.Serialization;

namespace SecRandom.Shared.Abstraction;

public abstract class ProfileConfigBase : ConfigBase
{
    [JsonIgnore] public abstract string Name { get; set; }

    public static bool IsValidProfileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.EndsWith('.') || name.EndsWith(' '))
            return false;

        foreach (var character in name)
            if (char.IsControl(character) || "<>:\"/\\|?*".Contains(character))
                return false;

        // Apply Windows device-name rules on every platform so archives remain portable.
        var stem = name.Split('.')[0].TrimEnd(' ', '.');
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("CONIN$", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("CLOCK$", StringComparison.OrdinalIgnoreCase))
            return false;

        return !(stem.Length == 4 &&
                 (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                  stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                 "123456789\u00b9\u00b2\u00b3".Contains(stem[3]));
    }

    public static void ValidateProfileName(string? name)
    {
        if (!IsValidProfileName(name))
            throw new ArgumentException("Profile name must be a portable single file name.", nameof(name));
    }

    protected string GetProfileFilePath(string root, string directory)
    {
        ValidateProfileName(Name);
        return Utils.GetFilePath(root, directory, $"{Name}.json");
    }
}