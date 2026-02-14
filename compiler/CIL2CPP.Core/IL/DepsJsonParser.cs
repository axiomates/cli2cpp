using System.Text.Json;

namespace CIL2CPP.Core.IL;

/// <summary>
/// Parses {Assembly}.deps.json files produced by dotnet build to discover
/// referenced assemblies and their file paths.
/// </summary>
public class DepsJsonParser
{
    private const char LibraryVersionSeparator = '/';

    /// <summary>
    /// Information about a referenced library from deps.json.
    /// </summary>
    public record DepsLibrary(
        string Name,
        string Version,
        string Type, // "package", "project", or "reference"
        string? Path, // Relative path in NuGet cache
        List<string> RuntimeDlls // DLL file names within the package
    );

    /// <summary>
    /// Parse a deps.json file and return all library references.
    /// </summary>
    public static List<DepsLibrary> Parse(string depsJsonPath)
    {
        var result = new List<DepsLibrary>();
        if (!File.Exists(depsJsonPath)) return result;

        var json = File.ReadAllText(depsJsonPath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Parse "libraries" section for metadata (type, path)
        var libraryInfos = new Dictionary<string, (string Type, string? Path)>();
        if (root.TryGetProperty("libraries", out var libraries))
        {
            foreach (var lib in libraries.EnumerateObject())
            {
                var type = lib.Value.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
                var path = lib.Value.TryGetProperty("path", out var p) ? p.GetString() : null;
                libraryInfos[lib.Name] = (type, path);
            }
        }

        // Parse "targets" section for runtime DLLs
        if (root.TryGetProperty("targets", out var targets))
        {
            foreach (var target in targets.EnumerateObject())
            {
                foreach (var lib in target.Value.EnumerateObject())
                {
                    var runtimeDlls = new List<string>();
                    if (lib.Value.TryGetProperty("runtime", out var runtime))
                    {
                        foreach (var dll in runtime.EnumerateObject())
                        {
                            runtimeDlls.Add(dll.Name);
                        }
                    }

                    // Extract name/version from key (e.g., "Newtonsoft.Json/13.0.3")
                    var parts = lib.Name.Split(LibraryVersionSeparator);
                    var name = parts[0];
                    var version = parts.Length > 1 ? parts[1] : "";

                    var type = "";
                    string? path = null;
                    if (libraryInfos.TryGetValue(lib.Name, out var info))
                    {
                        type = info.Type;
                        path = info.Path;
                    }

                    if (runtimeDlls.Count > 0)
                    {
                        result.Add(new DepsLibrary(name, version, type, path, runtimeDlls));
                    }
                }
                break; // Only parse the first target framework
            }
        }

        return result;
    }

    /// <summary>
    /// Resolve actual DLL paths on disk for all NuGet package libraries.
    /// Uses the NuGet global packages folder.
    /// </summary>
    public static List<string> ResolvePackagePaths(List<DepsLibrary> libraries)
    {
        var paths = new List<string>();
        var nugetFolder = GetNuGetPackagesFolder();
        if (nugetFolder == null) return paths;

        foreach (var lib in libraries)
        {
            if (lib.Type != "package" || lib.Path == null) continue;

            foreach (var dllRelPath in lib.RuntimeDlls)
            {
                var fullPath = Path.Combine(nugetFolder, lib.Path, dllRelPath);
                if (File.Exists(fullPath))
                    paths.Add(fullPath);
            }
        }

        return paths;
    }

    /// <summary>
    /// Get the NuGet global packages folder path.
    /// </summary>
    public static string? GetNuGetPackagesFolder()
    {
        // Check NUGET_PACKAGES environment variable first
        var envPath = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrEmpty(envPath) && Directory.Exists(envPath))
            return envPath;

        // Default: ~/.nuget/packages
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var defaultPath = Path.Combine(home, ".nuget", "packages");
        if (Directory.Exists(defaultPath))
            return defaultPath;

        return null;
    }
}
