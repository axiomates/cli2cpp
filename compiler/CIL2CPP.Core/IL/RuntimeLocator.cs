using System.Text.Json;

namespace CIL2CPP.Core.IL;

/// <summary>
/// Locates .NET runtime implementation assemblies on the local machine.
/// These are the actual assemblies with IL bodies (not reference assemblies).
/// </summary>
public class RuntimeLocator
{
    private const string SharedFrameworkName = "Microsoft.NETCore.App";
    private const string DotNetInfoBasePathPrefix = "Base Path:";
    private static readonly string[] LinuxDotNetPaths = { "/usr/share/dotnet", "/usr/local/share/dotnet" };

    /// <summary>
    /// Find the .NET shared runtime directory for the target framework.
    /// Uses runtimeconfig.json to determine the required version.
    /// </summary>
    public static string? FindRuntimeDirectory(string assemblyPath)
    {
        // Try to read version from runtimeconfig.json
        var runtimeConfigPath = FindRuntimeConfig(assemblyPath);
        var targetVersion = runtimeConfigPath != null
            ? ParseRuntimeVersion(runtimeConfigPath)
            : null;

        // Find the dotnet installation
        var dotnetRoot = FindDotNetRoot();
        if (dotnetRoot == null) return null;

        var sharedDir = Path.Combine(dotnetRoot, "shared", SharedFrameworkName);
        if (!Directory.Exists(sharedDir)) return null;

        // If we have a target version, find the best match
        if (targetVersion != null)
        {
            var exactMatch = Path.Combine(sharedDir, targetVersion);
            if (Directory.Exists(exactMatch))
                return exactMatch;

            // Try major.minor match (e.g., 8.0.x for 8.0.0)
            var parts = targetVersion.Split('.');
            if (parts.Length >= 2)
            {
                var prefix = $"{parts[0]}.{parts[1]}.";
                var candidates = Directory.GetDirectories(sharedDir)
                    .Where(d => Path.GetFileName(d).StartsWith(prefix))
                    .OrderByDescending(d => ParseVersion(Path.GetFileName(d)))
                    .ToList();
                if (candidates.Count > 0)
                    return candidates[0];
            }
        }

        // Fallback: use the latest installed version
        var allVersions = Directory.GetDirectories(sharedDir)
            .OrderByDescending(d => ParseVersion(Path.GetFileName(d)))
            .ToList();
        return allVersions.Count > 0 ? allVersions[0] : null;
    }

    /// <summary>
    /// Find the runtimeconfig.json file for an assembly.
    /// </summary>
    private static string? FindRuntimeConfig(string assemblyPath)
    {
        var dir = Path.GetDirectoryName(assemblyPath);
        if (dir == null) return null;

        var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);
        var configPath = Path.Combine(dir, $"{assemblyName}.runtimeconfig.json");
        return File.Exists(configPath) ? configPath : null;
    }

    /// <summary>
    /// Parse the target framework version from runtimeconfig.json.
    /// </summary>
    internal static string? ParseRuntimeVersion(string runtimeConfigPath)
    {
        try
        {
            var json = File.ReadAllText(runtimeConfigPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("runtimeOptions", out var options))
            {
                // Single framework
                if (options.TryGetProperty("framework", out var framework))
                {
                    if (framework.TryGetProperty("version", out var version))
                        return version.GetString();
                }

                // Multiple frameworks
                if (options.TryGetProperty("frameworks", out var frameworks))
                {
                    foreach (var fw in frameworks.EnumerateArray())
                    {
                        if (fw.TryGetProperty("name", out var name) &&
                            name.GetString() == SharedFrameworkName &&
                            fw.TryGetProperty("version", out var ver))
                        {
                            return ver.GetString();
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // Expected: invalid JSON or missing properties in runtimeconfig.json
        }
        return null;
    }

    /// <summary>
    /// Find the dotnet installation root directory.
    /// </summary>
    public static string? FindDotNetRoot()
    {
        // Check DOTNET_ROOT environment variable
        var envRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(envRoot) && Directory.Exists(envRoot))
            return envRoot;

        // Platform-specific default paths
        if (OperatingSystem.IsWindows())
        {
            var progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var winPath = Path.Combine(progFiles, "dotnet");
            if (Directory.Exists(winPath)) return winPath;
        }
        else
        {
            // Linux/macOS
            foreach (var path in LinuxDotNetPaths)
            {
                if (Directory.Exists(path)) return path;
            }
        }

        // Try to find dotnet from PATH
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("dotnet", "--info")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            var proc = System.Diagnostics.Process.Start(psi);
            if (proc != null)
            {
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(5000);

                // Parse "Base Path: C:\Program Files\dotnet\sdk\8.0.xxx\"
                foreach (var line in output.Split('\n'))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith(DotNetInfoBasePathPrefix))
                    {
                        var basePath = trimmed[DotNetInfoBasePathPrefix.Length..].Trim();
                        // Go up from sdk/{version}/ to dotnet root
                        var sdkDir = Directory.GetParent(basePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                        var dotnetDir = sdkDir?.Parent;
                        if (dotnetDir?.Exists == true)
                            return dotnetDir.FullName;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            // Expected: dotnet not in PATH, process failed, or output unreadable
        }

        return null;
    }

    /// <summary>
    /// Parse a version string, returning Version(0,0) if unparseable.
    /// Handles version strings with pre-release suffixes (e.g., "8.0.0-preview.1").
    /// </summary>
    internal static Version ParseVersion(string versionString)
    {
        // Strip pre-release suffix (e.g., "8.0.0-preview.1" → "8.0.0")
        var dashIndex = versionString.IndexOf('-');
        var cleanVersion = dashIndex >= 0 ? versionString[..dashIndex] : versionString;
        return Version.TryParse(cleanVersion, out var v) ? v : new Version(0, 0);
    }
}
