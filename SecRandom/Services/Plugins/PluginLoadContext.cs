using System.Reflection;
using System.Runtime.Loader;
using SecRandom.PluginSdk;

namespace SecRandom.Services.Plugins;

internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly string _pluginDirectory;
    private readonly IReadOnlyList<PluginLoadContext> _dependencies;
    private readonly HashSet<Assembly> _loadedAssemblies = [];

    public PluginLoadContext(string entryAssemblyPath, IReadOnlyList<PluginLoadContext> dependencies)
        : base($"SecRandom.Plugin[{Path.GetFileNameWithoutExtension(entryAssemblyPath)}]")
    {
        _resolver = new AssemblyDependencyResolver(entryAssemblyPath);
        _pluginDirectory = Path.GetDirectoryName(entryAssemblyPath)
                           ?? throw new ArgumentException("The plugin assembly path has no directory.", nameof(entryAssemblyPath));
        _dependencies = dependencies;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is not null && IsHostAssembly(assemblyName.Name))
            return null;

        return TryLoad(assemblyName);
    }

    private Assembly? TryLoad(AssemblyName assemblyName)
    {
        foreach (var dependency in _dependencies)
        {
            var assembly = dependency.TryLoad(assemblyName);
            if (assembly is not null)
                return assembly;
        }

        var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName)
                           ?? Path.Combine(_pluginDirectory, assemblyName.Name + ".dll");
        if (!File.Exists(assemblyPath))
            return null;

        var loaded = LoadFromAssemblyPath(assemblyPath);
        lock (_loadedAssemblies)
            _loadedAssemblies.Add(loaded);
        return loaded;
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName)
                          ?? Path.Combine(_pluginDirectory, unmanagedDllName);
        return File.Exists(libraryPath) ? LoadUnmanagedDllFromPath(libraryPath) : nint.Zero;
    }

    /// <summary>
    ///     Returns true when any stack frame of the exception (or its inner exceptions) originates from an
    ///     assembly loaded by this context. Used to attribute process-level failures to the responsible plugin.
    /// </summary>
    public bool OwnsException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (OwnsStackTrace(current.StackTrace))
                return true;
            if (current.TargetSite?.DeclaringType?.Assembly is { } targetAssembly && OwnsAssembly(targetAssembly))
                return true;
        }

        return false;
    }

    private bool OwnsStackTrace(string? stackTrace)
    {
        if (string.IsNullOrWhiteSpace(stackTrace))
            return false;

        foreach (var frameText in stackTrace.Split('\n'))
        {
            var typeName = ExtractFirstTypeName(frameText);
            if (typeName is null)
                continue;

            var type = Type.GetType(typeName, throwOnError: false);
            if (type is not null && OwnsAssembly(type.Assembly))
                return true;
        }

        return false;
    }

    private bool OwnsAssembly(Assembly assembly)
    {
        lock (_loadedAssemblies)
            return _loadedAssemblies.Contains(assembly);
    }

    private static string? ExtractFirstTypeName(string frameText)
    {
        // Stack frames look like "   at Namespace.Type.Method(args) in ..." or "   at Namespace.Type.Method".
        var trimmed = frameText.Trim();
        if (!trimmed.StartsWith("at ", StringComparison.Ordinal))
            return null;

        var openParen = trimmed.IndexOf('(');
        var end = openParen >= 0 ? openParen : trimmed.Length;
        var methodSegment = trimmed[3..end];
        var methodNameStart = methodSegment.LastIndexOf('.');
        return methodNameStart > 0 ? methodSegment[..methodNameStart] : methodSegment;
    }

    private static bool IsHostAssembly(string assemblyName)
    {
        return assemblyName is "SecRandom.PluginSdk" or "SecRandom.Core" or "SecRandom.Shared"
               || assemblyName.StartsWith("Avalonia", StringComparison.Ordinal)
               || assemblyName.StartsWith("FluentAvalonia", StringComparison.Ordinal)
               || assemblyName.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal);
    }
}
