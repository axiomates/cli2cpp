using Mono.Cecil;

namespace CIL2CPP.Core.IL;

/// <summary>
/// Custom assembly resolver that searches configured directories and caches loaded assemblies.
/// Used to enable cross-assembly type resolution in Mono.Cecil.
/// </summary>
public class CIL2CPPAssemblyResolver : BaseAssemblyResolver
{
    private readonly List<string> _searchPaths = new();
    private readonly Dictionary<string, AssemblyDefinition> _cache = new();

    /// <summary>
    /// Add a directory to search for assemblies.
    /// </summary>
    public new void AddSearchDirectory(string directory)
    {
        if (!_searchPaths.Contains(directory))
            _searchPaths.Add(directory);
    }

    /// <summary>
    /// Pre-register an already-loaded assembly so it won't be loaded again.
    /// </summary>
    public void RegisterAssembly(AssemblyDefinition assembly)
    {
        _cache[assembly.Name.Name] = assembly;
    }

    public override AssemblyDefinition Resolve(AssemblyNameReference name)
    {
        return Resolve(name, new ReaderParameters());
    }

    public override AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
    {
        // Check cache first
        if (_cache.TryGetValue(name.Name, out var cached))
            return cached;

        // Search configured directories
        foreach (var dir in _searchPaths)
        {
            var candidate = Path.Combine(dir, name.Name + ".dll");
            if (File.Exists(candidate))
            {
                var readerParams = new ReaderParameters
                {
                    ReadSymbols = false,
                    ReadWrite = false,
                    AssemblyResolver = this
                };
                var asm = AssemblyDefinition.ReadAssembly(candidate, readerParams);
                _cache[name.Name] = asm;
                return asm;
            }
        }

        // Fallback to base resolver (GAC, etc.)
        try
        {
            var asm = base.Resolve(name, parameters);
            if (asm != null)
            {
                _cache[name.Name] = asm;
                return asm;
            }
        }
        catch (AssemblyResolutionException)
        {
            // Expected: base resolver couldn't find the assembly
        }

        throw new AssemblyResolutionException(name);
    }

    /// <summary>
    /// Try to resolve an assembly without throwing.
    /// </summary>
    public AssemblyDefinition? TryResolve(AssemblyNameReference name)
    {
        try
        {
            return Resolve(name);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Get all currently loaded assemblies.
    /// </summary>
    public IReadOnlyDictionary<string, AssemblyDefinition> LoadedAssemblies => _cache;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var asm in _cache.Values)
                asm.Dispose();
            _cache.Clear();
        }
        base.Dispose(disposing);
    }
}
