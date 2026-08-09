using SecRandom.Platforms.Abstractions;

namespace SecRandom.Platforms.Windows;

internal sealed class WindowsRemovableStorageBindingMarker : IRemovableStorageBindingMarker
{
    public string FileName => ".SecRandom.safety.key";

    public bool TryHide(string path)
    {
        try
        {
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden | FileAttributes.System);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
