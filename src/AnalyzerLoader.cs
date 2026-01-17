using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RoslynMcpServer;

/// <summary>
/// Loads .NET analyzers from the Microsoft.CodeAnalysis.NetAnalyzers package.
/// </summary>
public static class AnalyzerLoader
{
    private static ImmutableArray<DiagnosticAnalyzer>? _cachedAnalyzers;
    private static ImmutableArray<Assembly>? _loadedAssemblies;
    private static readonly object _lock = new();

    /// <summary>
    /// Gets all .NET analyzers for C# code analysis.
    /// Results are cached after first load.
    /// </summary>
    public static ImmutableArray<DiagnosticAnalyzer> GetNetAnalyzers()
    {
        if (_cachedAnalyzers.HasValue)
            return _cachedAnalyzers.Value;

        lock (_lock)
        {
            if (_cachedAnalyzers.HasValue)
                return _cachedAnalyzers.Value;

            _cachedAnalyzers = LoadAnalyzersFromPackage();
            return _cachedAnalyzers.Value;
        }
    }

    /// <summary>
    /// Gets all loaded analyzer assemblies. Call GetNetAnalyzers() first to ensure assemblies are loaded.
    /// Used to extract CodeFixProviders from the same assemblies that provide analyzers.
    /// </summary>
    public static ImmutableArray<Assembly> GetLoadedAssemblies()
    {
        // Ensure analyzers are loaded first (which populates _loadedAssemblies)
        GetNetAnalyzers();
        return _loadedAssemblies ?? [];
    }

    private static ImmutableArray<DiagnosticAnalyzer> LoadAnalyzersFromPackage()
    {
        var analyzers = new List<DiagnosticAnalyzer>();
        var loadedAssemblies = new List<Assembly>();

        // Find analyzer DLLs in NuGet package cache
        var analyzerPaths = FindAnalyzerAssemblies();

        foreach (var path in analyzerPaths)
        {
            try
            {
                Console.Error.WriteLine($"Loading analyzer assembly: {Path.GetFileName(path)}");
                var assembly = Assembly.LoadFrom(path);
                loadedAssemblies.Add(assembly);

                var analyzerTypes = assembly.GetTypes()
                    .Where(t => !t.IsAbstract &&
                                typeof(DiagnosticAnalyzer).IsAssignableFrom(t) &&
                                t.GetConstructor(Type.EmptyTypes) != null);

                foreach (var type in analyzerTypes)
                {
                    try
                    {
                        if (Activator.CreateInstance(type) is DiagnosticAnalyzer analyzer)
                        {
                            analyzers.Add(analyzer);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Failed to create analyzer {type.Name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to load assembly {path}: {ex.Message}");
            }
        }

        _loadedAssemblies = [.. loadedAssemblies];
        Console.Error.WriteLine($"Loaded {analyzers.Count} analyzers from {loadedAssemblies.Count} assemblies");
        return [.. analyzers];
    }

    private static List<string> FindAnalyzerAssemblies()
    {
        // First, try to load from bundled analyzers in the application directory
        var result = FindBundledAnalyzers();
        if (result.Count > 0)
        {
            Console.Error.WriteLine($"Using bundled analyzers from application directory");
            return result;
        }

        // Fall back to NuGet packages cache
        Console.Error.WriteLine("Bundled analyzers not found, searching NuGet cache...");
        return FindAnalyzersInNuGetCache();
    }

    private static List<string> FindBundledAnalyzers()
    {
        var result = new List<string>();

        // Look for analyzers subfolder next to the executable
        var appDir = AppContext.BaseDirectory;
        var analyzersPath = Path.Combine(appDir, "analyzers");

        if (!Directory.Exists(analyzersPath))
            return result;

        // Get all DLLs recursively (excluding resource DLLs)
        var dlls = Directory.GetFiles(analyzersPath, "*.dll", SearchOption.AllDirectories)
            .Where(f => !f.Contains(".resources.", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (dlls.Count > 0)
        {
            Console.Error.WriteLine($"Found {dlls.Count} bundled analyzer DLLs");
            foreach (var dll in dlls)
            {
                Console.Error.WriteLine($"  - {Path.GetFileName(dll)}");
            }
        }

        return dlls;
    }

    private static List<string> FindAnalyzersInNuGetCache()
    {
        var result = new List<string>();

        // Get NuGet packages folder
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var nugetPackages = Path.Combine(userProfile, ".nuget", "packages");

        // Look for Microsoft.CodeAnalysis.NetAnalyzers package
        var netAnalyzersPath = Path.Combine(nugetPackages, "microsoft.codeanalysis.netanalyzers");

        if (!Directory.Exists(netAnalyzersPath))
        {
            Console.Error.WriteLine($"NetAnalyzers package not found at: {netAnalyzersPath}");
            return result;
        }

        // Find the latest version
        var versions = Directory.GetDirectories(netAnalyzersPath)
            .Select(d => new { Path = d, Version = ParseVersion(Path.GetFileName(d)) })
            .Where(v => v.Version != null)
            .OrderByDescending(v => v.Version)
            .ToList();

        if (versions.Count == 0)
        {
            Console.Error.WriteLine("No versions found for NetAnalyzers package");
            return result;
        }

        var latestVersion = versions[0].Path;
        Console.Error.WriteLine($"Using NetAnalyzers version: {Path.GetFileName(latestVersion)}");

        // Find analyzers - structure varies by version:
        // - v9.x: analyzers are in analyzers/dotnet/cs/
        // - v10.x: main analyzer in analyzers/dotnet/, C# specific in analyzers/dotnet/cs/
        var dotnetPath = Path.Combine(latestVersion, "analyzers", "dotnet");
        var csPath = Path.Combine(dotnetPath, "cs");

        // Get DLLs from both locations (excluding resource DLLs)
        if (Directory.Exists(dotnetPath))
        {
            var dotnetDlls = Directory.GetFiles(dotnetPath, "*.dll")
                .Where(f => !f.Contains(".resources.", StringComparison.OrdinalIgnoreCase));
            result.AddRange(dotnetDlls);
        }

        if (Directory.Exists(csPath))
        {
            var csDlls = Directory.GetFiles(csPath, "*.dll")
                .Where(f => !f.Contains(".resources.", StringComparison.OrdinalIgnoreCase));
            result.AddRange(csDlls);
        }

        if (result.Count == 0)
        {
            Console.Error.WriteLine($"No analyzer DLLs found at: {dotnetPath}");
        }
        else
        {
            Console.Error.WriteLine($"Found {result.Count} analyzer DLLs in NuGet cache");
            foreach (var dll in result)
            {
                Console.Error.WriteLine($"  - {Path.GetFileName(dll)}");
            }
        }

        return result;
    }

    private static Version? ParseVersion(string versionString)
    {
        // Handle versions like "9.0.0" or "9.0.0-preview.1.24530.6"
        var dashIndex = versionString.IndexOf('-');
        var cleanVersion = dashIndex > 0 ? versionString[..dashIndex] : versionString;

        return Version.TryParse(cleanVersion, out var version) ? version : null;
    }
}
