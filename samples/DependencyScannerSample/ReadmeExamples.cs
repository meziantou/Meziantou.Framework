using System.Collections.Concurrent;
using System.IO.Enumeration;
using System.Text.RegularExpressions;
using Meziantou.Framework.DependencyScanning;
using Meziantou.Framework.DependencyScanning.Scanners;
using Meziantou.Framework.Globbing;

namespace DependencyScannerSample;

// The examples of src/Meziantou.Framework.DependencyScanning/readme.md. They scan a folder that only
// exists on the reader's machine, so CI compiles them but does not run them.
public static class ReadmeExamples
{
    public static async Task ScanDirectory(CancellationToken cancellationToken)
    {
        #region DependencyScanner_ScanDirectory
        // Scan with default options
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

    public static async Task ScanWithCustomOptions(CancellationToken cancellationToken)
    {
        #region DependencyScanner_CustomOptions
        var options = new ScannerOptions
        {
            // Number of parallel scanning tasks (default: 16)
            DegreeOfParallelism = 8,

            // Recurse into subdirectories (default: true)
            RecurseSubdirectories = true,

            // Filter files to scan (FileSystemEntry is in System.IO.Enumeration)
            ShouldScanFilePredicate = (ref FileSystemEntry entry) => !entry.FileName.StartsWith('.'),

            // Filter directories to recurse into
            ShouldRecursePredicate = (ref FileSystemEntry entry) => entry.FileName is not ("node_modules" or "bin"),

            // Called when a file cannot be read or parsed. The file is skipped and the scan continues.
            OnFileScanFailed = (filePath, exception) => Console.Error.WriteLine($"Cannot scan {filePath}: {exception.Message}"),
        };

        var dependencies = await DependencyScanner.ScanDirectoryAsync(
            @"C:\MyProject",
            options,
            cancellationToken);
        #endregion

        _ = dependencies;
    }

    public static ScannerOptions ErrorHandling()
    {
        #region DependencyScanner_ErrorHandling
        var options = new ScannerOptions
        {
            OnFileScanFailed = (filePath, exception) => throw new InvalidOperationException($"Cannot scan '{filePath}'", exception),
        };
        #endregion

        return options;
    }

    public static ScannerOptions[] FilterByDependencyType()
    {
        #region DependencyScanner_FilterByDependencyType
        var options = new ScannerOptions
        {
            // Only scan for specific dependency types
            IncludedDependencyTypes = [DependencyType.NuGet, DependencyType.Npm],
        };

        // Or exclude specific types
        var options2 = new ScannerOptions
        {
            ExcludedDependencyTypes = [DependencyType.DockerImage],
        };
        #endregion

        return [options, options2];
    }

    public static async Task<ConcurrentQueue<Dependency>> StreamDependencies(CancellationToken cancellationToken)
    {
        #region DependencyScanner_StreamDependencies
        var dependencies = new ConcurrentQueue<Dependency>();
        await DependencyScanner.ScanDirectoryAsync(
            "C:\\MyProject",
            options: null,
            onDependencyFound: dependency =>
            {
                // Called concurrently
                dependencies.Enqueue(dependency);
            },
            cancellationToken);
        #endregion

        return dependencies;
    }

    public static async Task ScanIndividualFiles(CancellationToken cancellationToken)
    {
        #region DependencyScanner_ScanFiles
        // Scan a single file
        var dependencies = await DependencyScanner.ScanFileAsync(
            rootDirectory: "C:\\MyProject",
            filePath: "C:\\MyProject\\package.json",
            options: null,
            cancellationToken);

        // Scan multiple specific files
        var filePaths = new[]
        {
            "C:\\MyProject\\package.json",
            "C:\\MyProject\\MyProject.csproj",
        };

        var allDependencies = await DependencyScanner.ScanFilesAsync(
            rootDirectory: "C:\\MyProject",
            filePaths,
            options: null,
            cancellationToken);
        #endregion

        _ = dependencies;
        _ = allDependencies;
    }

    public static async Task UpdateDependencyVersions(CancellationToken cancellationToken)
    {
        #region DependencyScanner_UpdateVersions
        var dependencies = await DependencyScanner.ScanDirectoryAsync(
            "C:\\MyProject",
            options: null,
            cancellationToken);

        // Update all NuGet packages to version 2.0.0
        foreach (var dependency in dependencies.Where(d => d.Type == DependencyType.NuGet))
        {
            if (dependency.VersionLocation?.IsUpdatable == true)
            {
                await dependency.UpdateVersionAsync("2.0.0", cancellationToken);
            }
        }
        #endregion
    }

    public static async Task CustomRegexScanner(CancellationToken cancellationToken)
    {
        #region DependencyScanner_CustomRegexScanner
        var options = new ScannerOptions
        {
            Scanners =
            [
                new RegexScanner
                {
                    FilePatterns = [Glob.Parse("**/*.custom", GlobDialect.Standard, GlobOptions.IgnoreCase)],
                    DependencyType = DependencyType.DockerImage,
                    Regex = new Regex(@"image:\s*(?<name>[a-z/]+)(:(?<version>[0-9.]+))?", RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(10)),
                },
            ],
        };

        var dependencies = await DependencyScanner.ScanDirectoryAsync(
            "C:\\MyProject",
            options,
            cancellationToken);
        #endregion

        _ = dependencies;
    }
}
