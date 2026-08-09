using System.Runtime.Versioning;
using Android.App;
using Android.Content;
using AndroidX.Core.Content;
using SecRandom.Mobile;
using LR = SecRandom.Langs.Mobile.Resources;

namespace SecRandom.Android;

[SupportedOSPlatform("android24.0")]
public sealed class AndroidUpdateInstaller : IMobileUpdateInstaller
{
    public bool IsSupported => true;

    public async Task<string> StagePackageAsync(byte[] bytes, string assetName, CancellationToken ct)
    {
        var directory = Path.Combine(Application.Context!.CacheDir!.AbsolutePath!, "updates");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, assetName);
        await File.WriteAllBytesAsync(path + ".partial", bytes, ct);
        File.Move(path + ".partial", path, true);
        return path;
    }

    public void OpenInstaller(string packagePath)
    {
        var context = Application.Context ?? throw new InvalidOperationException(LR.M_AndroidContextUnavailable);
        var uri = FileProvider.GetUriForFile(context,
            $"{context.PackageName}.updatefileprovider", new Java.IO.File(packagePath));
        var intent = new Intent(Intent.ActionView)
            .SetDataAndType(uri, "application/vnd.android.package-archive")
            .AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);
        context.StartActivity(intent);
    }
}
