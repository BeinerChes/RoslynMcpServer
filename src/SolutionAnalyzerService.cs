using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis.MSBuild;

namespace RoslynMcpServer;

/// <summary>
/// Service for analyzing .NET solutions using Roslyn.
/// </summary>
public partial class SolutionAnalyzerService
{
    private static bool _msBuildRegistered;
    private static readonly object _lockObject = new();

    /// <summary>
    /// Ensures MSBuild is registered. Must be called before any Roslyn operations.
    /// </summary>
    public static void EnsureMSBuildRegistered()
    {
        if (_msBuildRegistered) return;

        lock (_lockObject)
        {
            if (_msBuildRegistered) return;

            if (!MSBuildLocator.IsRegistered)
            {
                var instances = MSBuildLocator.QueryVisualStudioInstances().ToList();
                if (instances.Count == 0)
                {
                    throw new InvalidOperationException(
                        "No MSBuild instances found. Please install Visual Studio or the .NET SDK.");
                }

                // Use the newest version available
                var instance = instances.OrderByDescending(i => i.Version).First();
                MSBuildLocator.RegisterInstance(instance);
                Console.Error.WriteLine($"Registered MSBuild: {instance.Name} {instance.Version}");
            }

            _msBuildRegistered = true;
        }
    }

    /// <summary>
    /// Loads a solution and returns projects in build order (dependencies first).
    /// </summary>
    public async Task<ProjectBuildOrderResult> GetProjectsInBuildOrderAsync(string solutionPath)
    {
        EnsureMSBuildRegistered();

        if (!File.Exists(solutionPath))
        {
            return new ProjectBuildOrderResult
            {
                Success = false,
                Error = $"Solution file not found: {solutionPath}"
            };
        }

        using var workspace = MSBuildWorkspace.Create();

        // Log any workspace failures
        workspace.WorkspaceFailed += (sender, args) =>
        {
            Console.Error.WriteLine($"Workspace warning: {args.Diagnostic.Message}");
        };

        try
        {
            Console.Error.WriteLine($"Loading solution: {solutionPath}");
            var solution = await workspace.OpenSolutionAsync(solutionPath);

            var projectGraph = solution.GetProjectDependencyGraph();
            var sortedProjectIds = projectGraph.GetTopologicallySortedProjects();

            var projects = new List<ProjectInfo>();
            foreach (var projectId in sortedProjectIds)
            {
                var project = solution.GetProject(projectId);
                if (project is null) continue;

                var dependencies = projectGraph.GetProjectsThatThisProjectDirectlyDependsOn(projectId)
                    .Select(depId => solution.GetProject(depId)?.Name)
                    .Where(name => name is not null)
                    .Cast<string>()
                    .ToList();

                projects.Add(new ProjectInfo
                {
                    Name = project.Name,
                    FilePath = project.FilePath ?? "unknown",
                    Language = project.Language,
                    Dependencies = dependencies
                });
            }

            return new ProjectBuildOrderResult
            {
                Success = true,
                SolutionPath = solutionPath,
                Projects = projects
            };
        }
        catch (Exception ex)
        {
            return new ProjectBuildOrderResult
            {
                Success = false,
                Error = $"Failed to load solution: {ex.Message}"
            };
        }
    }
}
