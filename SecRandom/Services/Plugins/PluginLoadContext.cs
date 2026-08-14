using System.Reflection;
using System.Runtime.Loader;
using SecRandom.PluginSdk;

namespace SecRandom.Services.Plugins;

internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly string _pluginDirectory;
    private readonly IReadOnlyList<PluginLoadContext> _dependencies;

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
        return File.Exists(assemblyPath) ? LoadFromAssemblyPath(assemblyPath) : null;
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName)
                          ?? Path.Combine(_pluginDirectory, unmanagedDllName);
        return File.Exists(libraryPath) ? LoadUnmanagedDllFromPath(libraryPath) : nint.Zero;
    }

    private static bool IsHostAssembly(string assemblyName)
    {
        return assemblyName is "SecRandom.PluginSdk" or "SecRandom.Core" or "SecRandom.Shared"
               || assemblyName.StartsWith("Avalonia", StringComparison.Ordinal)
               || assemblyName.StartsWith("FluentAvalonia", StringComparison.Ordinal)
               || assemblyName.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal);
    }
}
