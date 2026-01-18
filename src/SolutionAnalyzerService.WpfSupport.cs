using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace RoslynMcpServer;

/// <summary>
/// WPF/XAML support for solution analysis.
/// Handles inclusion of generated *.g.cs files that MSBuildWorkspace doesn't include automatically.
/// </summary>
public partial class SolutionAnalyzerService
{
    /// <summary>
    /// Checks if a filename is a generated XAML file (*.g.cs or *.g.i.cs).
    /// </summary>
    /// <param name="fileName">The filename to check.</param>
    /// <returns>True if the file is a generated XAML file.</returns>
    public static bool IsGeneratedXamlFile(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return false;

        // XAML compilation generates:
        // - *.g.cs (generated code for XAML)
        // - *.g.i.cs (generated intermediate code)
        return fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Scans a project's obj folder for generated XAML files (*.g.cs).
    /// </summary>
    /// <param name="projectFilePath">Path to the .csproj file.</param>
    /// <returns>List of absolute paths to generated files.</returns>
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
            // Search recursively for *.g.cs files in obj folder
            // These are typically in obj/Debug/net8.0-windows/ or similar
            var generatedFiles = Directory.GetFiles(objDir, "*.g.cs", SearchOption.AllDirectories);

            foreach (var file in generatedFiles)
            {
                // Only include actual XAML-generated files
                if (IsGeneratedXamlFile(Path.GetFileName(file)))
                {
                    result.Add(file);
                }
            }
        }
        catch (Exception ex)
        {
            // Log but don't fail - this is an enhancement, not a requirement
            Console.Error.WriteLine($"Warning: Failed to scan for generated files in {objDir}: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Enhances a solution by adding generated XAML files (*.g.cs) from each project's obj folder.
    /// This fixes false positive errors like CS0103 for InitializeComponent and x:Name fields.
    /// </summary>
    /// <param name="solution">The solution to enhance.</param>
    /// <returns>Enhanced solution with generated files included.</returns>
    public static Solution EnhanceSolutionWithGeneratedFiles(Solution solution)
    {
        // Collect project IDs first since we'll modify the solution
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
                // Re-get project from updated solution
                project = solution.GetProject(projectId);
                if (project == null)
                    break;

                // Check if this file is already in the project
                var existingDoc = project.Documents
                    .FirstOrDefault(d => string.Equals(d.FilePath, genFile, StringComparison.OrdinalIgnoreCase));

                if (existingDoc != null)
                    continue;

                try
                {
                    var sourceText = SourceText.From(File.ReadAllText(genFile));
                    var fileName = Path.GetFileName(genFile);

                    // Add as a generated document
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
}
