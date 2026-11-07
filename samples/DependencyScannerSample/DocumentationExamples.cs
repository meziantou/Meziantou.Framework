using System.Text.RegularExpressions;
using Meziantou.Framework.DependencyScanning;
using Meziantou.Framework.DependencyScanning.Scanners;
using Meziantou.Framework.Globbing;

namespace DependencyScannerSample;

public static class DocumentationExamples
{
    public static async Task ScanDirectory(CancellationToken cancellationToken)
    {
        #region DependencyScanner_ScanDirectory
        // Scan a directory for all dependencies
        var dependencies = await DependencyScanner.ScanDirectoryAsync(
            "C:\\MyProject",
            options: null,
            cancellationToken);

        foreach (var dependency in dependencies)
        {
            Console.WriteLine($"{dependency.Type}: {dependency.Name}@{dependency.Version}");
        }
        #endregion
    }

    public static async Task ScanDirectoryWithOptions(CancellationToken cancellationToken)
    {
        #region ScannerOptions_Example
        var options = new ScannerOptions
        {
            DegreeOfParallelism = 8,
            RecurseSubdirectories = true,
            IncludedDependencyTypes = [DependencyType.NuGet, DependencyType.Npm],
        };
        var dependencies = await DependencyScanner.ScanDirectoryAsync("C:\\MyProject", options, cancellationToken);
        #endregion

        Console.WriteLine(dependencies.Count);
    }

    public static async Task ScanDirectoryWithRegexScanner(CancellationToken cancellationToken)
    {
        #region RegexScanner_Example
        var scanner = new RegexScanner
        {
            FilePatterns = [Glob.Parse("**/*.custom", GlobDialect.Standard, GlobOptions.IgnoreCase)],
            DependencyType = DependencyType.DockerImage,
            Regex = new Regex(@"image:\s*(?<name>[a-z/]+)(:(?<version>[0-9.]+))?", RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(10)),
        };
        var options = new ScannerOptions { Scanners = [scanner] };
        var dependencies = await DependencyScanner.ScanDirectoryAsync("C:\\MyProject", options, cancellationToken);
        #endregion

        Console.WriteLine(dependencies.Count);
    }
}
