using Mono.Cecil;

namespace CIL2CPP.Core.IL;

/// <summary>
/// Classification of assemblies by origin.
/// </summary>
public enum AssemblyKind
{
    /// <summary>The user's own project assembly (the root).</summary>
    User,
    /// <summary>A third-party NuGet package assembly.</summary>
    ThirdParty,
    /// <summary>A .NET Base Class Library assembly.</summary>
    BCL
}

/// <summary>
/// Manages loading and classification of multiple assemblies for cross-assembly compilation.
/// Loads the root assembly and can lazily load referenced assemblies (third-party + BCL).
/// </summary>
public class AssemblySet : IDisposable
{
    private readonly CIL2CPPAssemblyResolver _resolver;
    private readonly Dictionary<string, AssemblyDefinition> _loadedAssemblies = new();
    private readonly HashSet<string> _packageNames = new();
    private readonly string? _runtimeDir;
    private readonly string _rootAssemblyDir;

    /// <summary>The entry-point assembly provided by the user.</summary>
    public AssemblyDefinition RootAssembly { get; }

    /// <summary>Name of the root assembly.</summary>
    public string RootAssemblyName => RootAssembly.Name.Name;

    /// <summary>
    /// Create an AssemblySet from a root DLL path.
    /// Discovers dependencies via deps.json and runtimeconfig.json.
    /// </summary>
    public AssemblySet(string rootDllPath, BuildConfiguration? config = null)
    {
        _rootAssemblyDir = Path.GetDirectoryName(Path.GetFullPath(rootDllPath))!;
        _resolver = new CIL2CPPAssemblyResolver();
        _resolver.AddSearchDirectory(_rootAssemblyDir);

        // Discover NuGet package names from deps.json
        var depsJsonPath = Path.Combine(_rootAssemblyDir,
            Path.GetFileNameWithoutExtension(rootDllPath) + ".deps.json");
        if (File.Exists(depsJsonPath))
        {
            var deps = DepsJsonParser.Parse(depsJsonPath);
            foreach (var lib in deps)
            {
                if (lib.Type == "package")
                    _packageNames.Add(lib.Name);
            }

            // Add resolved NuGet package paths as search directories
            var packagePaths = DepsJsonParser.ResolvePackagePaths(deps);
            foreach (var pkgPath in packagePaths)
            {
                var pkgDir = Path.GetDirectoryName(pkgPath);
                if (pkgDir != null)
                    _resolver.AddSearchDirectory(pkgDir);
            }
        }

        // Add .NET runtime directory for BCL resolution
        _runtimeDir = RuntimeLocator.FindRuntimeDirectory(rootDllPath);
        if (_runtimeDir != null)
            _resolver.AddSearchDirectory(_runtimeDir);

        // Load the root assembly
        var readSymbols = config?.ReadDebugSymbols ?? false;
        ReaderParameters readerParams;
        if (readSymbols)
        {
            try
            {
                readerParams = new ReaderParameters
                {
                    ReadSymbols = true,
                    ReadWrite = false,
                    AssemblyResolver = _resolver
                };
                RootAssembly = AssemblyDefinition.ReadAssembly(rootDllPath, readerParams);
                _resolver.RegisterAssembly(RootAssembly);
                _loadedAssemblies[RootAssemblyName] = RootAssembly;
                return;
            }
            catch
            {
                // PDB not found, fall through
            }
        }

        readerParams = new ReaderParameters
        {
            ReadSymbols = false,
            ReadWrite = false,
            AssemblyResolver = _resolver
        };
        RootAssembly = AssemblyDefinition.ReadAssembly(rootDllPath, readerParams);
        _resolver.RegisterAssembly(RootAssembly);
        _loadedAssemblies[RootAssemblyName] = RootAssembly;
    }

    /// <summary>
    /// Load a referenced assembly by name. Returns null if not found.
    /// Assemblies are cached after first load.
    /// </summary>
    public AssemblyDefinition? LoadAssembly(string name)
    {
        if (_loadedAssemblies.TryGetValue(name, out var cached))
            return cached;

        var nameRef = AssemblyNameReference.Parse(name);
        var asm = _resolver.TryResolve(nameRef);
        if (asm != null)
            _loadedAssemblies[name] = asm;

        return asm;
    }

    /// <summary>
    /// Classify an assembly by its origin.
    /// </summary>
    public AssemblyKind ClassifyAssembly(string assemblyName)
    {
        if (assemblyName == RootAssemblyName)
            return AssemblyKind.User;

        if (_packageNames.Contains(assemblyName))
            return AssemblyKind.ThirdParty;

        // Check if the assembly exists in the .NET runtime directory
        if (_runtimeDir != null)
        {
            var runtimePath = Path.Combine(_runtimeDir, assemblyName + ".dll");
            if (File.Exists(runtimePath))
                return AssemblyKind.BCL;
        }

        // Check if it's in the output directory (likely a ProjectReference)
        var outputPath = Path.Combine(_rootAssemblyDir, assemblyName + ".dll");
        if (File.Exists(outputPath))
            return AssemblyKind.User;

        // Default: treat as BCL (could also be a framework assembly)
        return AssemblyKind.BCL;
    }

    /// <summary>
    /// Get all types from all loaded assemblies, with their classification.
    /// Skips the special &lt;Module&gt; type.
    /// </summary>
    public IEnumerable<(TypeDefinition Type, AssemblyKind Kind)> GetAllLoadedTypes()
    {
        foreach (var (name, asm) in _loadedAssemblies)
        {
            var kind = ClassifyAssembly(name);
            foreach (var type in GetAllTypesFromAssembly(asm))
            {
                yield return (type, kind);
            }
        }
    }

    /// <summary>
    /// Get all currently loaded assemblies.
    /// </summary>
    public IReadOnlyDictionary<string, AssemblyDefinition> LoadedAssemblies => _loadedAssemblies;

    /// <summary>
    /// Get the underlying resolver for advanced resolution scenarios.
    /// </summary>
    public CIL2CPPAssemblyResolver Resolver => _resolver;

    private static IEnumerable<TypeDefinition> GetAllTypesFromAssembly(AssemblyDefinition asm)
    {
        foreach (var type in asm.MainModule.Types)
        {
            if (type.Name == "<Module>")
                continue;

            yield return type;

            foreach (var nested in GetNestedTypesRecursive(type))
                yield return nested;
        }
    }

    private static IEnumerable<TypeDefinition> GetNestedTypesRecursive(TypeDefinition type)
    {
        foreach (var nested in type.NestedTypes)
        {
            yield return nested;
            foreach (var deepNested in GetNestedTypesRecursive(nested))
                yield return deepNested;
        }
    }

    public void Dispose()
    {
        // Resolver owns the cached assemblies, disposing it disposes all of them
        _resolver.Dispose();
        _loadedAssemblies.Clear();
    }
}
