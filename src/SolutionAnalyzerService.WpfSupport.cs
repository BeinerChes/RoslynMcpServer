using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace RoslynMcpServer;

/// <summary>
/// WPF/XAML support for solution analysis.
/// Runs design-time builds to generate *.g.cs files before loading solutions.
/// </summary>
public partial class SolutionAnalyzerService
{
    /// <summary>
    /// Checks if a filename is a generated XAML file (*.g.cs or *.g.i.cs).
    /// </summary>
    public static bool IsGeneratedXamlFile(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return false;

        return fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Scans a project's obj folder for generated XAML files (*.g.cs).
    /// </summary>
    public static List<string> ScanForGeneratedXamlFiles(string projectFilePath)
    {
        var result = new List<string>();

        if (string.IsNullOrEmpty(projectFilePath))
            return result;

        var projectDir = Path.GetDirectoryName(projectFilePath);
        if (string.IsNullOrEmpty(projectDir))
            return result;

        var objDir = Path.Combine(projectDir, "obj");
        if (!Directory.Exists(objDir))
            return result;

        try
        {
            var generatedFiles = Directory.GetFiles(objDir, "*.g.cs", SearchOption.AllDirectories);
            foreach (var file in generatedFiles)
            {
                if (IsGeneratedXamlFile(Path.GetFileName(file)))
                {
                    result.Add(file);
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Failed to scan for generated files in {objDir}: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Checks if a project is a WPF project by examining its properties.
    /// </summary>
    public static bool IsWpfProject(string projectFilePath)
    {
        if (!File.Exists(projectFilePath))
            return false;

        try
        {
            var content = File.ReadAllText(projectFilePath);

            // Check for UseWPF property (SDK-style projects)
            if (content.Contains("<UseWPF>true</UseWPF>", StringComparison.OrdinalIgnoreCase))
                return true;

            // Check for WPF references (old-style projects)
            if (content.Contains("PresentationCore", StringComparison.OrdinalIgnoreCase) ||
                content.Contains("PresentationFramework", StringComparison.OrdinalIgnoreCase))
                return true;

            // Check for XAML files
            if (content.Contains(".xaml", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        catch
        {
            // Ignore errors reading project file
        }

        return false;
    }

    /// <summary>
    /// Runs design-time build on a WPF project to generate *.g.cs files.
    /// This executes MarkupCompilePass1 and MarkupCompilePass2 targets.
    /// </summary>
    public static bool RunDesignTimeBuild(string projectFilePath)
    {
        if (!File.Exists(projectFilePath))
            return false;

        try
        {
            Console.Error.WriteLine($"Running design-time build for: {Path.GetFileName(projectFilePath)}");

            // Create a new project collection for this build
            using var projectCollection = new ProjectCollection();

            // Set design-time build properties
            var globalProperties = new Dictionary<string, string>
            {
                { "DesignTimeBuild", "true" },
                { "BuildingInsideVisualStudio", "true" },
                { "SkipCompilerExecution", "true" },
                { "ProvideCommandLineArgs", "true" },
                // Prevent full compilation, just generate sources
                { "BuildingProject", "false" }
            };

            // Load the project
            var project = projectCollection.LoadProject(projectFilePath, globalProperties, null);

            // Create build request for XAML generation targets
            var buildRequestData = new BuildRequestData(
                project.CreateProjectInstance(),
                ["MarkupCompilePass1", "MarkupCompilePass2"],
                null,
                BuildRequestDataFlags.None);

            // Configure build parameters with minimal logging
            var buildParameters = new BuildParameters(projectCollection)
            {
                Loggers = [new QuietLogger()],
                EnableNodeReuse = false
            };

            // Run the build
            var buildResult = BuildManager.DefaultBuildManager.Build(buildParameters, buildRequestData);

            if (buildResult.OverallResult == BuildResultCode.Success)
            {
                Console.Error.WriteLine($"  Design-time build succeeded for {Path.GetFileName(projectFilePath)}");
                return true;
            }
            else
            {
                Console.Error.WriteLine($"  Design-time build had issues for {Path.GetFileName(projectFilePath)} (may still have generated files)");
                // Even if build "fails", the targets may have generated the files we need
                return true;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  Warning: Design-time build failed for {Path.GetFileName(projectFilePath)}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Runs design-time builds for all WPF projects in a solution to generate *.g.cs files.
    /// Call this BEFORE loading the solution into MSBuildWorkspace.
    /// </summary>
    public static void RunDesignTimeBuildsForSolution(string solutionPath)
    {
        if (!File.Exists(solutionPath))
            return;

        var solutionDir = Path.GetDirectoryName(solutionPath);
        if (string.IsNullOrEmpty(solutionDir))
            return;

        try
        {
            // Parse solution file to find project paths
            var projectPaths = ParseSolutionForProjects(solutionPath);

            int wpfProjectCount = 0;
            foreach (var projectPath in projectPaths)
            {
                var fullPath = Path.IsPathRooted(projectPath)
                    ? projectPath
                    : Path.GetFullPath(Path.Combine(solutionDir, projectPath));

                if (File.Exists(fullPath) && IsWpfProject(fullPath))
                {
                    wpfProjectCount++;
                    RunDesignTimeBuild(fullPath);
                }
            }

            if (wpfProjectCount > 0)
            {
                Console.Error.WriteLine($"Ran design-time builds for {wpfProjectCount} WPF project(s)");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Error during design-time builds: {ex.Message}");
        }
    }

    /// <summary>
    /// Parses a solution file to extract project paths.
    /// </summary>
    private static List<string> ParseSolutionForProjects(string solutionPath)
    {
        var projects = new List<string>();

        try
        {
            var lines = File.ReadAllLines(solutionPath);
            foreach (var line in lines)
            {
                // Match Project lines: Project("{...}") = "Name", "Path", "{...}"
                if (line.StartsWith("Project(", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split('"');
                    if (parts.Length >= 6)
                    {
                        var projectPath = parts[5]; // The path is the 6th quoted string
                        if (projectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                            projectPath.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase))
                        {
                            projects.Add(projectPath);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Failed to parse solution file: {ex.Message}");
        }

        return projects;
    }

    /// <summary>
    /// Enhances a solution by adding generated XAML files (*.g.cs) from each project's obj folder.
    /// This is a fallback for when design-time build doesn't include all files.
    /// </summary>
    public static Solution EnhanceSolutionWithGeneratedFiles(Solution solution)
    {
        var projectIds = solution.Projects.Select(p => p.Id).ToList();

        foreach (var projectId in projectIds)
        {
            var project = solution.GetProject(projectId);
            if (project == null || string.IsNullOrEmpty(project.FilePath))
                continue;

            var generatedFiles = ScanForGeneratedXamlFiles(project.FilePath);
            if (generatedFiles.Count == 0)
                continue;

            Console.Error.WriteLine($"Found {generatedFiles.Count} generated XAML files for {project.Name}");

            foreach (var genFile in generatedFiles)
            {
                project = solution.GetProject(projectId);
                if (project == null)
                    break;

                var existingDoc = project.Documents
                    .FirstOrDefault(d => string.Equals(d.FilePath, genFile, StringComparison.OrdinalIgnoreCase));

                if (existingDoc != null)
                    continue;

                try
                {
                    var sourceText = SourceText.From(File.ReadAllText(genFile));
                    var fileName = Path.GetFileName(genFile);
                    var updatedProject = project.AddDocument(fileName, sourceText, filePath: genFile).Project;
                    solution = updatedProject.Solution;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Warning: Failed to add generated file {genFile}: {ex.Message}");
                }
            }
        }

        return solution;
    }

    /// <summary>
    /// Minimal logger for design-time builds - suppresses most output.
    /// </summary>
    private class QuietLogger : ILogger
    {
        public LoggerVerbosity Verbosity { get; set; } = LoggerVerbosity.Quiet;
        public string? Parameters { get; set; }

        public void Initialize(IEventSource eventSource)
        {
            // Only log errors
            eventSource.ErrorRaised += (sender, args) =>
            {
                Console.Error.WriteLine($"    Build error: {args.Message}");
            };
        }

        public void Shutdown() { }
    }
}
