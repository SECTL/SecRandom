using System.Runtime.Versioning;
using Android.Content;
using Android.Content.PM;
using Android.Provider;
using SecRandom.Android.Storage;
using SecRandom.Shared;

namespace SecRandom.Android;

[SupportedOSPlatform("android24.0")]
public static class AndroidDataDirectoryLauncher
{
    public static bool TryOpenPath(string path)
    {
        try
        {
            var providerRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(GetProviderRootPath()));
            var dataRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Utils.GetMobileDataRootPath()));
            var fullPath = Path.GetFullPath(path);

            // Android has no browsable install directory; an out-of-root request (for example the
            // app package root) opens the SecRandom application directory instead.
            if (!IsSameOrDescendant(fullPath, providerRoot))
                fullPath = providerRoot;

            if (IsProtectedPath(fullPath, dataRoot))
                return false;

            var targetDirectory = File.Exists(fullPath) ? Path.GetDirectoryName(fullPath)! : fullPath;
            var relativePath = Path.GetRelativePath(providerRoot, targetDirectory);
            if (relativePath == ".")
                relativePath = "";

            var context = global::Android.App.Application.Context
                          ?? throw new InvalidOperationException("The Android application context is unavailable.");
            var authority = $"{context.PackageName}.documents";
            var documentUri = SecRandomDocumentsProvider.BuildDocumentUri(authority, relativePath);

            if (TryBrowseDirectory(context, documentUri))
                return true;

            if (TryPickDirectory(context, documentUri))
                return true;

            global::Android.Util.Log.Warn("SecRandom.DataDir", "No file manager or picker handled the data directory URI.");
            return false;
        }
        catch (Exception exception)
        {
            global::Android.Util.Log.Error("SecRandom.DataDir", exception.ToString());
            return false;
        }
    }

    private static string GetProviderRootPath()
    {
        var providerRoot = Path.GetDirectoryName(Utils.GetMobileDataRootPath())
                           ?? throw new InvalidOperationException("The Android provider root is unavailable.");
        return providerRoot;
    }

    private static bool TryBrowseDirectory(Context context, global::Android.Net.Uri documentUri)
    {
        using var intent = new Intent(Intent.ActionView);
        intent.SetDataAndType(documentUri, DocumentsContract.Document.MimeTypeDir);
        intent.AddFlags(ActivityFlags.NewTask |
                        ActivityFlags.GrantReadUriPermission |
                        ActivityFlags.GrantWriteUriPermission |
                        ActivityFlags.GrantPrefixUriPermission);
        TargetSystemFileManager(intent, context);

        try
        {
            context.StartActivity(intent);
            global::Android.Util.Log.Info("SecRandom.DataDir",
                $"ACTION_VIEW started documentUri={documentUri} mime={DocumentsContract.Document.MimeTypeDir}");
            return true;
        }
        catch (ActivityNotFoundException)
        {
            global::Android.Util.Log.Warn("SecRandom.DataDir",
                $"ACTION_VIEW had no handler for {documentUri}; falling back to the directory picker.");
            return false;
        }
    }

    private static bool TryPickDirectory(Context context, global::Android.Net.Uri documentUri)
    {
        using var intent = new Intent(Intent.ActionOpenDocumentTree);
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            intent.PutExtra(DocumentsContract.ExtraInitialUri, documentUri);
        intent.AddFlags(ActivityFlags.NewTask |
                        ActivityFlags.GrantReadUriPermission |
                        ActivityFlags.GrantWriteUriPermission |
                        ActivityFlags.GrantPrefixUriPermission);

        try
        {
            context.StartActivity(intent);
            global::Android.Util.Log.Info("SecRandom.DataDir",
                $"ACTION_OPEN_DOCUMENT_TREE started with INITIAL_URI={documentUri}");
            return true;
        }
        catch (ActivityNotFoundException)
        {
            global::Android.Util.Log.Warn("SecRandom.DataDir",
                "ACTION_OPEN_DOCUMENT_TREE had no handler either.");
            return false;
        }
    }

    private static bool IsSameOrDescendant(string path, string rootPath)
    {
        return string.Equals(path, rootPath, StringComparison.Ordinal)
               || path.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static bool IsProtectedPath(string path, string dataRoot)
    {
        var securityDirectory = Path.Combine(dataRoot, "config", "security");
        var voiceDirectory = Path.Combine(dataRoot, "config", "voice");
        return IsSameOrDescendant(path, securityDirectory)
               || IsSameOrDescendant(path, voiceDirectory);
    }

    private static void TargetSystemFileManager(Intent intent, Context context)
    {
        var packageManager = context.PackageManager;
        var fileManagerActivity = packageManager
            ?.QueryIntentActivities(intent, 0)
            .Select(info => info.ActivityInfo)
            .FirstOrDefault(activity => activity?.PackageName is not null
                                        && packageManager.CheckPermission(
                                            global::Android.Manifest.Permission.ManageDocuments,
                                            activity.PackageName) == Permission.Granted);

        if (fileManagerActivity?.PackageName is not null && fileManagerActivity.Name is not null)
        {
            intent.SetClassName(fileManagerActivity.PackageName, fileManagerActivity.Name);
            global::Android.Util.Log.Info("SecRandom.DataDir",
                $"Targeted file manager activity {fileManagerActivity.PackageName}/{fileManagerActivity.Name}.");
        }
        else
        {
            global::Android.Util.Log.Warn("SecRandom.DataDir",
                "No MANAGE_DOCUMENTS activity matched ACTION_VIEW; relying on system resolution.");
        }
    }
}
