namespace SecRandom.Shared.DataRoot;

/// <summary>
///     Resolution order for the desktop data root:
///     <c>--data-root</c> → <c>SECRANDOM_DATA_ROOT</c> → anchor → legacy claim → mode default.
///     A legacy location is only claimed while the new default is empty, so an upgrade never silently starts
///     from an empty directory while the user's data still sits somewhere else.
/// </summary>
public static class DesktopDataRootResolver
{
    public static DesktopDataRootResolution Resolve(DesktopDataRootOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var appDirectory = Path.GetFullPath(options.AppDirectory ?? AppContext.BaseDirectory);
        var packageRoot = ResolvePackageRoot(appDirectory, options.PackageRoot);
        var packageKind = options.PackageKind ?? DataRootPaths.ReadPackageKind(appDirectory);
        var isPortable = DataRootPaths.IsPortablePackageLayout(packageRoot, appDirectory)
                         && DataRootPaths.IsPortablePackageKind(packageKind);

        var anchorPath = options.AnchorPath ?? DataRootPaths.GetAnchorPath(packageRoot, isPortable);
        var standardDataRoot = Path.GetFullPath(options.StandardDataRoot ?? DataRootPaths.GetStandardDataRoot());
        var packageDataRoot = Path.Combine(packageRoot, DataRootPaths.DataDirectoryName);
        var defaultMode = isPortable || options.IsDevelopmentBuild
            ? DataRootMode.Package
            : DataRootMode.Standard;
        var defaultRoot = defaultMode == DataRootMode.Package ? packageDataRoot : standardDataRoot;

        var payloadDataRoots = DataRootPaths.EnumeratePayloadDirectories(packageRoot)
            .Select(directory => Path.Combine(directory, DataRootPaths.DataDirectoryName))
            .Where(DataRootPaths.HasData)
            .ToArray();

        var others = new List<string>();
        void RememberOther(string? candidate, string currentRoot)
        {
            if (string.IsNullOrWhiteSpace(candidate)
                || !DataRootPaths.HasData(candidate)
                || DataRootPaths.PathEquals(candidate, currentRoot)
                || others.Any(existing => DataRootPaths.PathEquals(existing, candidate)))
                return;

            others.Add(Path.GetFullPath(candidate));
        }

        if (!string.IsNullOrWhiteSpace(options.CommandLineDataRoot))
        {
            var root = Path.GetFullPath(options.CommandLineDataRoot);
            RememberOther(packageDataRoot, root);
            RememberOther(standardDataRoot, root);
            foreach (var payloadRoot in payloadDataRoots)
                RememberOther(payloadRoot, root);
            return Build(root, DataRootMode.Custom, DataRootSource.CommandLine, needsNotice: false);
        }

        if (!string.IsNullOrWhiteSpace(options.EnvironmentDataRoot))
        {
            var root = Path.GetFullPath(options.EnvironmentDataRoot);
            RememberOther(packageDataRoot, root);
            RememberOther(standardDataRoot, root);
            foreach (var payloadRoot in payloadDataRoots)
                RememberOther(payloadRoot, root);
            return Build(root, DataRootMode.Custom, DataRootSource.Environment, needsNotice: false);
        }

        var anchorResult = DataRootAnchorStore.Read(anchorPath);
        if (anchorResult is { Status: DataRootAnchorStatus.Valid, Anchor: { } anchor })
        {
            var root = Path.GetFullPath(anchor.DataRoot);
            if (isPortable && DataRootModeExtensions.Parse(anchor.Mode) == DataRootMode.Package
                && !DataRootPaths.HasData(packageDataRoot)
                && payloadDataRoots.Any(payload => DataRootPaths.PathEquals(payload, root)))
                return Build(packageDataRoot, DataRootMode.Package, DataRootSource.RepairedPayload,
                    needsNotice: false, payloadDataRootToRepair: root);
            if (isPortable && DataRootModeExtensions.Parse(anchor.Mode) == DataRootMode.Package
                && !DataRootPaths.PathEquals(root, packageDataRoot)
                && (DataRootPaths.HasData(packageDataRoot) || !DataRootPaths.HasData(root)))
            {
                if (!DataRootPaths.HasData(packageDataRoot) && payloadDataRoots.Length > 0)
                    return Build(packageDataRoot, DataRootMode.Package, DataRootSource.RepairedPayload,
                        needsNotice: false, payloadDataRootToRepair: payloadDataRoots[0]);
                root = packageDataRoot;
            }
            RememberOther(packageDataRoot, root);
            RememberOther(standardDataRoot, root);
            foreach (var payloadRoot in payloadDataRoots)
                RememberOther(payloadRoot, root);

            return Build(
                root,
                DataRootModeExtensions.Parse(anchor.Mode),
                DataRootSource.Anchor,
                needsNotice: IsUnprotectedLayout(root));
        }

        // No anchor: claim the first location that actually holds data, newest expectations first.
        if (isPortable || options.IsDevelopmentBuild)
        {
            if (DataRootPaths.HasData(packageDataRoot))
            {
                RememberOther(standardDataRoot, packageDataRoot);
                foreach (var payloadRoot in payloadDataRoots)
                    RememberOther(payloadRoot, packageDataRoot);
                return Build(packageDataRoot, DataRootMode.Package, DataRootSource.ModeDefault, needsNotice: false);
            }

            if (payloadDataRoots.Length > 0)
            {
                foreach (var payloadRoot in payloadDataRoots)
                    RememberOther(payloadRoot, packageDataRoot);
                return Build(
                    packageDataRoot,
                    DataRootMode.Package,
                    DataRootSource.RepairedPayload,
                    needsNotice: false,
                    payloadDataRootToRepair: payloadDataRoots[0]);
            }

            RememberOther(standardDataRoot, packageDataRoot);
            return Build(packageDataRoot, DataRootMode.Package, DataRootSource.ModeDefault, needsNotice: false);
        }

        if (DataRootPaths.HasData(standardDataRoot))
        {
            RememberOther(packageDataRoot, standardDataRoot);
            foreach (var payloadRoot in payloadDataRoots)
                RememberOther(payloadRoot, standardDataRoot);
            return Build(standardDataRoot, DataRootMode.Standard, DataRootSource.ModeDefault, needsNotice: false);
        }

        if (DataRootPaths.HasData(packageDataRoot))
        {
            RememberOther(standardDataRoot, packageDataRoot);
            foreach (var payloadRoot in payloadDataRoots)
                RememberOther(payloadRoot, packageDataRoot);
            return Build(
                packageDataRoot,
                DataRootMode.Package,
                DataRootSource.Legacy,
                needsNotice: true);
        }

        if (payloadDataRoots.Length > 0)
        {
            foreach (var payloadRoot in payloadDataRoots)
                RememberOther(payloadRoot, packageDataRoot);
            return Build(
                packageDataRoot,
                DataRootMode.Package,
                DataRootSource.RepairedPayload,
                needsNotice: true,
                payloadDataRootToRepair: payloadDataRoots[0]);
        }

        RememberOther(packageDataRoot, standardDataRoot);
        return Build(standardDataRoot, DataRootMode.Standard, DataRootSource.ModeDefault, needsNotice: false);

        DesktopDataRootResolution Build(
            string root,
            DataRootMode mode,
            DataRootSource source,
            bool needsNotice,
            string? payloadDataRootToRepair = null)
        {
            return new DesktopDataRootResolution
            {
                AppDirectory = appDirectory,
                PackageRoot = packageRoot,
                IsPortablePackage = isPortable,
                Mode = mode,
                Source = source,
                DataRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)),
                AnchorPath = Path.GetFullPath(anchorPath),
                PackageKind = packageKind,
                PayloadDataRootToRepair = payloadDataRootToRepair,
                OtherDataRoots = others.ToArray(),
                NeedsMigrationNotice = needsNotice
            };
        }

        bool IsUnprotectedLayout(string root)
        {
            // A root inside the running payload is replaced by the next update, no matter the mode.
            return DataRootPaths.IsSameOrNested(appDirectory, root);
        }
    }

    /// <summary>Same package-root rule the rest of the app uses: portable layout wins over the executable folder.</summary>
    public static string ResolvePackageRoot(string appDirectory, string? configuredPackageRoot)
    {
        if (DataRootPaths.IsPortablePackageLayout(configuredPackageRoot!, appDirectory))
            return Path.GetFullPath(configuredPackageRoot!);

        var parent = Directory.GetParent(Path.GetFullPath(appDirectory))?.FullName;
        return DataRootPaths.IsPortablePackageLayout(parent!, appDirectory)
            ? Path.GetFullPath(parent!)
            : Path.GetFullPath(appDirectory);
    }
}
