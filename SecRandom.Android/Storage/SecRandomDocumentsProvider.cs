using System.Runtime.Versioning;
using System.Text;
using Android.Content;
using Android.Database;
using Android.OS;
using Android.Provider;
using Android.Webkit;
using SecRandom.Shared;
using JavaFile = Java.IO.File;
using JavaFileNotFoundException = Java.IO.FileNotFoundException;

namespace SecRandom.Android.Storage;

[ContentProvider(["${applicationId}.documents"],
    Exported = true,
    GrantUriPermissions = true,
    Permission = global::Android.Manifest.Permission.ManageDocuments)]
[IntentFilter([DocumentsContract.ProviderInterface])]
[SupportedOSPlatform("android24.0")]
public sealed class SecRandomDocumentsProvider : DocumentsProvider
{
    private const string RootId = "secrandom";
    private const string RootDocumentId = "root";
    private const string DataDirectoryName = "data";
    private const string EncodedPathPrefix = "path:";
    private const string DefaultMimeType = "application/octet-stream";
    private const string ProtectedSecurityDirectory = "config/security";

    private static readonly string[] DefaultRootProjection =
    [
        DocumentsContract.Root.ColumnRootId,
        DocumentsContract.Root.ColumnMimeTypes,
        DocumentsContract.Root.ColumnFlags,
        DocumentsContract.Root.ColumnIcon,
        DocumentsContract.Root.ColumnTitle,
        DocumentsContract.Root.ColumnSummary,
        DocumentsContract.Root.ColumnDocumentId,
        DocumentsContract.Root.ColumnAvailableBytes
    ];

    private static readonly string[] DefaultDocumentProjection =
    [
        DocumentsContract.Document.ColumnDocumentId,
        DocumentsContract.Document.ColumnMimeType,
        DocumentsContract.Document.ColumnDisplayName,
        DocumentsContract.Document.ColumnLastModified,
        DocumentsContract.Document.ColumnFlags,
        DocumentsContract.Document.ColumnSize
    ];

    private string Authority => Context?.PackageName + ".documents"
                                ?? throw new InvalidOperationException("The provider context is unavailable.");

    private string DataPath
    {
        get
        {
            var dataPath = Utils.GetMobileDataRootPath();
            Directory.CreateDirectory(dataPath);
            return new JavaFile(dataPath).CanonicalPath;
        }
    }

    private string RootPath
    {
        get
        {
            var rootPath = Path.GetDirectoryName(DataPath)
                           ?? throw new InvalidOperationException("The mobile data parent directory is unavailable.");
            Directory.CreateDirectory(rootPath);
            return new JavaFile(rootPath).CanonicalPath;
        }
    }

    public override bool OnCreate()
    {
        _ = RootPath;
        return true;
    }

    public override ICursor QueryRoots(string[]? projection)
    {
        var columns = ResolveProjection(projection, DefaultRootProjection);
        var result = new MatrixCursor(columns);
        var rootFile = new JavaFile(DataPath);
        var row = result.NewRow()!;

        foreach (var column in columns)
        {
            AddValue(row, column, column switch
            {
                DocumentsContract.Root.ColumnRootId => RootId,
                DocumentsContract.Root.ColumnMimeTypes => "*/*",
                DocumentsContract.Root.ColumnFlags => DocumentRootFlags.LocalOnly |
                                                      DocumentRootFlags.SupportsCreate |
                                                      DocumentRootFlags.SupportsIsChild,
                DocumentsContract.Root.ColumnIcon => Context?.ApplicationInfo?.Icon ?? 0,
                DocumentsContract.Root.ColumnTitle => GetApplicationLabel(),
                DocumentsContract.Root.ColumnSummary => "SecRandom data",
                DocumentsContract.Root.ColumnDocumentId => RootDocumentId,
                DocumentsContract.Root.ColumnAvailableBytes => rootFile.UsableSpace,
                _ => null
            });
        }

        return result;
    }

    public override ICursor QueryDocument(string? documentId, string[]? projection)
    {
        var columns = ResolveProjection(projection, DefaultDocumentProjection);
        var result = new MatrixCursor(columns);
        IncludeDocument(result, columns, GetPathForDocumentId(documentId));
        SetDocumentNotificationUri(result, documentId!);
        return result;
    }

    public override ICursor QueryChildDocuments(string? parentDocumentId, string[]? projection, string? sortOrder)
    {
        var columns = ResolveProjection(projection, DefaultDocumentProjection);
        var result = new MatrixCursor(columns);
        var parentPath = GetPathForDocumentId(parentDocumentId);
        EnsureDirectory(parentPath);

        var childPaths = string.Equals(parentPath, RootPath, StringComparison.Ordinal)
            ? [DataPath]
            : Directory.EnumerateFileSystemEntries(parentPath);

        foreach (var childPath in childPaths
                     .OrderBy(path => Directory.Exists(path) ? 0 : 1)
                     .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                IncludeDocument(result, columns, GetCanonicalContainedPath(childPath));
            }
            catch (JavaFileNotFoundException)
            {
                // Ignore protected, symbolic-link, and malformed entries.
            }
        }

        SetChildNotificationUri(result, parentDocumentId!);
        return result;
    }

    public override ParcelFileDescriptor OpenDocument(string? documentId, string? mode, CancellationSignal? signal)
    {
        var path = GetPathForDocumentId(documentId);
        EnsureFile(path);

        try
        {
            return ParcelFileDescriptor.Open(new JavaFile(path), ParcelFileDescriptor.ParseMode(mode ?? "r"))
                   ?? throw new JavaFileNotFoundException($"Unable to open document '{documentId}'.");
        }
        catch (JavaFileNotFoundException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new JavaFileNotFoundException(exception.Message);
        }
    }

    public override string CreateDocument(string? parentDocumentId, string? mimeType, string? displayName)
    {
        var parentPath = GetPathForDocumentId(parentDocumentId);
        EnsureDirectory(parentPath);
        var destinationPath = GetUniqueChildPath(parentPath, ValidateDisplayName(displayName));

        if (mimeType == DocumentsContract.Document.MimeTypeDir)
            Directory.CreateDirectory(destinationPath);
        else
            using (new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { }

        NotifyChildDocumentsChanged(parentDocumentId!);
        return GetDocumentIdForPath(destinationPath);
    }

    public override void DeleteDocument(string? documentId)
    {
        EnsureMutableDocument(documentId);
        var path = GetPathForDocumentId(documentId);
        var parentId = GetDocumentIdForPath(Path.GetDirectoryName(path)!);

        if (Directory.Exists(path))
            Directory.Delete(path, true);
        else if (File.Exists(path))
            File.Delete(path);
        else
            throw NotFound(documentId);

        NotifyChildDocumentsChanged(parentId);
    }

    public override string RenameDocument(string? documentId, string? displayName)
    {
        EnsureMutableDocument(documentId);
        var sourcePath = GetPathForDocumentId(documentId);
        var parentPath = Path.GetDirectoryName(sourcePath)!;
        var destinationPath = GetCanonicalContainedPath(Path.Combine(parentPath, ValidateDisplayName(displayName)));
        if (File.Exists(destinationPath) || Directory.Exists(destinationPath))
            throw new Java.Lang.IllegalStateException($"A document named '{displayName}' already exists.");

        MovePath(sourcePath, destinationPath);
        NotifyChildDocumentsChanged(GetDocumentIdForPath(parentPath));
        return GetDocumentIdForPath(destinationPath);
    }

    public override string CopyDocument(string? sourceDocumentId, string? targetParentDocumentId)
    {
        EnsureMutableDocument(sourceDocumentId);
        var sourcePath = GetPathForDocumentId(sourceDocumentId);
        var targetParentPath = GetPathForDocumentId(targetParentDocumentId);
        EnsureDirectory(targetParentPath);
        EnsureDirectoryIsNotWithinSource(sourcePath, targetParentPath);

        var destinationPath = GetUniqueChildPath(targetParentPath, Path.GetFileName(sourcePath));
        CopyPath(sourcePath, destinationPath);
        NotifyChildDocumentsChanged(targetParentDocumentId!);
        return GetDocumentIdForPath(destinationPath);
    }

    public override string MoveDocument(string? sourceDocumentId, string? sourceParentDocumentId,
        string? targetParentDocumentId)
    {
        EnsureMutableDocument(sourceDocumentId);
        var sourcePath = GetPathForDocumentId(sourceDocumentId);
        var sourceParentPath = GetPathForDocumentId(sourceParentDocumentId);
        var targetParentPath = GetPathForDocumentId(targetParentDocumentId);
        EnsureDirectory(targetParentPath);

        if (!string.Equals(Path.GetDirectoryName(sourcePath), sourceParentPath, StringComparison.Ordinal))
            throw new Java.Lang.IllegalArgumentException("The supplied source parent does not contain the document.");
        if (string.Equals(sourceParentPath, targetParentPath, StringComparison.Ordinal))
            return sourceDocumentId!;

        EnsureDirectoryIsNotWithinSource(sourcePath, targetParentPath);
        var destinationPath = GetCanonicalContainedPath(Path.Combine(targetParentPath, Path.GetFileName(sourcePath)));
        if (File.Exists(destinationPath) || Directory.Exists(destinationPath))
            throw new Java.Lang.IllegalStateException("A document with the same name already exists in the destination.");

        MovePath(sourcePath, destinationPath);
        NotifyChildDocumentsChanged(sourceParentDocumentId!);
        NotifyChildDocumentsChanged(targetParentDocumentId!);
        return GetDocumentIdForPath(destinationPath);
    }

    public override bool IsChildDocument(string? parentDocumentId, string? documentId)
    {
        try
        {
            var parentPath = GetPathForDocumentId(parentDocumentId);
            var childPath = GetPathForDocumentId(documentId);
            return !string.Equals(parentPath, childPath, StringComparison.Ordinal) && IsContainedBy(childPath, parentPath);
        }
        catch
        {
            return false;
        }
    }

    [SupportedOSPlatform("android26.0")]
    public override DocumentsContract.Path FindDocumentPath(string? parentDocumentId, string? childDocumentId)
    {
        var childPath = GetPathForDocumentId(childDocumentId);
        if (!Directory.Exists(childPath) && !File.Exists(childPath))
            throw NotFound(childDocumentId);

        var parentPath = parentDocumentId is null ? RootPath : GetPathForDocumentId(parentDocumentId);
        if (!string.Equals(parentPath, childPath, StringComparison.Ordinal) && !IsContainedBy(childPath, parentPath))
            throw new JavaFileNotFoundException("The requested document is not below the supplied parent.");

        var documentIds = new List<string> { GetDocumentIdForPath(parentPath) };
        var currentPath = parentPath;
        foreach (var segment in Path.GetRelativePath(parentPath, childPath)
                     .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            currentPath = Path.Combine(currentPath, segment);
            documentIds.Add(GetDocumentIdForPath(currentPath));
        }

        return new DocumentsContract.Path(parentDocumentId is null ? RootId : null, documentIds)!;
    }

    internal static global::Android.Net.Uri BuildDocumentUri(string authority, string relativePath) =>
        DocumentsContract.BuildDocumentUri(authority, GetDocumentIdForRelativePath(relativePath))!;

    private void IncludeDocument(MatrixCursor cursor, IReadOnlyList<string> columns, string path)
    {
        if (!Directory.Exists(path) && !File.Exists(path))
            throw NotFound(GetDocumentIdForPath(path));

        var isDirectory = Directory.Exists(path);
        var isRoot = string.Equals(path, RootPath, StringComparison.Ordinal);
        var isDataRoot = string.Equals(path, DataPath, StringComparison.Ordinal);
        var flags = isDirectory ? DocumentContractFlags.DirSupportsCreate : DocumentContractFlags.SupportsWrite;
        if (!isRoot && !isDataRoot)
        {
            flags |= DocumentContractFlags.SupportsDelete |
                     DocumentContractFlags.SupportsRename |
                     DocumentContractFlags.SupportsCopy |
                     DocumentContractFlags.SupportsMove;
        }

        var row = cursor.NewRow()!;
        foreach (var column in columns)
        {
            AddValue(row, column, column switch
            {
                DocumentsContract.Document.ColumnDocumentId => GetDocumentIdForPath(path),
                DocumentsContract.Document.ColumnMimeType => isDirectory
                    ? DocumentsContract.Document.MimeTypeDir
                    : GetMimeType(path),
                DocumentsContract.Document.ColumnDisplayName => isRoot
                    ? GetApplicationLabel()
                    : Path.GetFileName(path),
                DocumentsContract.Document.ColumnLastModified => new DateTimeOffset(File.GetLastWriteTimeUtc(path)).ToUnixTimeMilliseconds(),
                DocumentsContract.Document.ColumnFlags => flags,
                DocumentsContract.Document.ColumnSize => isDirectory ? null : new FileInfo(path).Length,
                _ => null
            });
        }
    }

    private string GetPathForDocumentId(string? documentId)
    {
        if (string.Equals(documentId, RootDocumentId, StringComparison.Ordinal))
            return RootPath;
        if (string.IsNullOrEmpty(documentId) || !documentId.StartsWith(EncodedPathPrefix, StringComparison.Ordinal))
            throw NotFound(documentId);

        try
        {
            var encodedPath = documentId[EncodedPathPrefix.Length..].Replace('-', '+').Replace('_', '/');
            encodedPath = encodedPath.PadRight(encodedPath.Length + (4 - encodedPath.Length % 4) % 4, '=');
            var relativePath = Encoding.UTF8.GetString(Convert.FromBase64String(encodedPath));
            if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
                throw NotFound(documentId);

            return GetCanonicalContainedPath(Path.Combine(RootPath,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }
        catch (JavaFileNotFoundException)
        {
            throw;
        }
        catch
        {
            throw NotFound(documentId);
        }
    }

    private string GetDocumentIdForPath(string path)
    {
        var canonicalPath = GetCanonicalContainedPath(path);
        if (string.Equals(canonicalPath, RootPath, StringComparison.Ordinal))
            return RootDocumentId;

        return GetDocumentIdForRelativePath(Path.GetRelativePath(RootPath, canonicalPath).Replace('\\', '/'));
    }

    private static string GetDocumentIdForRelativePath(string relativePath)
    {
        var normalizedPath = relativePath.Replace('\\', '/').Trim('/');
        if (string.IsNullOrEmpty(normalizedPath) || normalizedPath == ".")
            return RootDocumentId;
        if (Path.IsPathRooted(relativePath) || normalizedPath.Split('/').Any(segment => segment is "." or ".."))
            throw new ArgumentException("The document path must be relative to the provider root.", nameof(relativePath));

        return EncodedPathPrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(normalizedPath))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private string GetCanonicalContainedPath(string path)
    {
        // Android exposes app-private storage through /data/user/0 (a symlink to /data/data),
        // so canonicalization must be the security boundary: a symlink that stays inside the
        // canonical data root is safe, while anything resolving outside it is rejected.
        var canonicalPath = new JavaFile(path).CanonicalPath;
        if (!string.Equals(canonicalPath, RootPath, StringComparison.Ordinal)
            && !string.Equals(canonicalPath, DataPath, StringComparison.Ordinal)
            && !IsContainedBy(canonicalPath, DataPath))
            throw new JavaFileNotFoundException("The document path is outside the provider root.");
        if (IsProtectedPath(canonicalPath))
            throw new JavaFileNotFoundException("The requested document is protected.");

        return canonicalPath;
    }

    private bool IsProtectedPath(string path)
    {
        var protectedPath = Path.Combine(DataPath, ProtectedSecurityDirectory.Replace('/', Path.DirectorySeparatorChar));
        return string.Equals(path, protectedPath, StringComparison.Ordinal) || IsContainedBy(path, protectedPath);
    }

    private static bool IsContainedBy(string childPath, string parentPath) =>
        childPath.StartsWith(parentPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            StringComparison.Ordinal);

    private static string ValidateDisplayName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName) || displayName is "." or ".." ||
            displayName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            !string.Equals(Path.GetFileName(displayName), displayName, StringComparison.Ordinal))
        {
            throw new Java.Lang.IllegalArgumentException("The document name is invalid.");
        }

        return displayName;
    }

    private string GetUniqueChildPath(string parentPath, string displayName)
    {
        var candidate = GetCanonicalContainedPath(Path.Combine(parentPath, displayName));
        if (!File.Exists(candidate) && !Directory.Exists(candidate))
            return candidate;

        var extension = Path.GetExtension(displayName);
        var baseName = Path.GetFileNameWithoutExtension(displayName);
        for (var suffix = 1; suffix < int.MaxValue; suffix++)
        {
            candidate = GetCanonicalContainedPath(Path.Combine(parentPath, $"{baseName} ({suffix}){extension}"));
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                return candidate;
        }

        throw new Java.Lang.IllegalStateException("Unable to allocate a unique document name.");
    }

    private void CopyPath(string sourcePath, string destinationPath)
    {
        sourcePath = GetCanonicalContainedPath(sourcePath);
        destinationPath = GetCanonicalContainedPath(destinationPath);
        if (File.Exists(sourcePath))
        {
            File.Copy(sourcePath, destinationPath, false);
            return;
        }
        if (!Directory.Exists(sourcePath))
            throw new JavaFileNotFoundException($"Document '{sourcePath}' does not exist.");

        Directory.CreateDirectory(destinationPath);
        try
        {
            foreach (var childPath in Directory.EnumerateFileSystemEntries(sourcePath))
                CopyPath(childPath, Path.Combine(destinationPath, Path.GetFileName(childPath)));
        }
        catch
        {
            Directory.Delete(destinationPath, true);
            throw;
        }
    }

    private static void MovePath(string sourcePath, string destinationPath)
    {
        if (Directory.Exists(sourcePath))
            Directory.Move(sourcePath, destinationPath);
        else if (File.Exists(sourcePath))
            File.Move(sourcePath, destinationPath);
        else
            throw new JavaFileNotFoundException($"Document '{sourcePath}' does not exist.");
    }

    private static void EnsureDirectory(string path)
    {
        if (!Directory.Exists(path))
            throw new JavaFileNotFoundException($"Directory '{path}' does not exist.");
    }

    private static void EnsureFile(string path)
    {
        if (!File.Exists(path))
            throw new JavaFileNotFoundException($"File '{path}' does not exist.");
    }

    private static void EnsureMutableDocument(string? documentId)
    {
        if (string.Equals(documentId, RootDocumentId, StringComparison.Ordinal)
            || string.Equals(documentId, GetDocumentIdForRelativePath(DataDirectoryName), StringComparison.Ordinal))
            throw new Java.Lang.UnsupportedOperationException("The provider root cannot be modified.");
    }

    private static void EnsureDirectoryIsNotWithinSource(string sourcePath, string targetParentPath)
    {
        if (Directory.Exists(sourcePath) &&
            (string.Equals(sourcePath, targetParentPath, StringComparison.Ordinal) || IsContainedBy(targetParentPath, sourcePath)))
        {
            throw new Java.Lang.IllegalArgumentException("A directory cannot be copied or moved into itself.");
        }
    }

    private string GetApplicationLabel()
    {
        var context = Context ?? throw new InvalidOperationException("The provider context is unavailable.");
        var packageManager = context.PackageManager
                             ?? throw new InvalidOperationException("The Android package manager is unavailable.");
        return context.ApplicationInfo?.LoadLabel(packageManager)?.ToString()
               ?? context.PackageName
               ?? "SecRandom";
    }

    private static string GetMimeType(string path)
    {
        var extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return string.IsNullOrEmpty(extension)
            ? DefaultMimeType
            : MimeTypeMap.Singleton?.GetMimeTypeFromExtension(extension) ?? DefaultMimeType;
    }

    private static string[] ResolveProjection(string[]? requestedProjection, string[] defaultProjection) =>
        requestedProjection is { Length: > 0 } ? requestedProjection : defaultProjection;

    private static void AddValue(MatrixCursor.RowBuilder row, string column, object? value)
    {
        Java.Lang.Object? javaValue = value switch
        {
            null => null,
            string stringValue => new Java.Lang.String(stringValue),
            int intValue => Java.Lang.Integer.ValueOf(intValue),
            long longValue => Java.Lang.Long.ValueOf(longValue),
            DocumentRootFlags rootFlags => Java.Lang.Integer.ValueOf((int)rootFlags),
            DocumentContractFlags documentFlags => Java.Lang.Integer.ValueOf((int)documentFlags),
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported cursor value type.")
        };
        row.Add(column, javaValue);
    }

    private void SetDocumentNotificationUri(MatrixCursor cursor, string documentId)
    {
        var resolver = Context?.ContentResolver;
        if (resolver is not null)
            cursor.SetNotificationUri(resolver, DocumentsContract.BuildDocumentUri(Authority, documentId)!);
    }

    private void SetChildNotificationUri(MatrixCursor cursor, string parentDocumentId)
    {
        var resolver = Context?.ContentResolver;
        if (resolver is not null)
            cursor.SetNotificationUri(resolver, DocumentsContract.BuildChildDocumentsUri(Authority, parentDocumentId)!);
    }

    private void NotifyChildDocumentsChanged(string parentDocumentId)
    {
        Context?.ContentResolver?.NotifyChange(
            DocumentsContract.BuildChildDocumentsUri(Authority, parentDocumentId)!, null);
    }

    private static JavaFileNotFoundException NotFound(string? documentId) =>
        new($"Document '{documentId}' does not exist.");
}
