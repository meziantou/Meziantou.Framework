using System.Collections.ObjectModel;
using Meziantou.Framework.DependencyScanning.Scanners;

namespace Meziantou.Framework.DependencyScanning;

public sealed class ScannerOptions
{
    private static readonly ReadOnlyCollection<DependencyScanner> DefaultScanners = new(new DependencyScanner[]
    {
        new AzureDevOpsScanner(),
        new DockerfileDependencyScanner(),
        new DotNetGlobalJsonDependencyScanner(),
        new DotNetToolManifestDependencyScanner(),
        new GitHubActionsScanner(),
        new GitSubmoduleDependencyScanner(),
        new HelmChartDependencyScanner(),
        new MsBuildReferencesDependencyScanner(),
        new NpmPackageJsonDependencyScanner(),
        new NuSpecDependencyScanner(),
        new PackagesConfigDependencyScanner(),
        new ProjectJsonDependencyScanner(),
        new PythonRequirementsDependencyScanner(),
    });

    internal static ScannerOptions Default { get; } = new ScannerOptions();

    public IReadOnlyList<DependencyScanner> Scanners { get; set; } = DefaultScanners;
    public bool RecurseSubdirectories { get; set; } = true;
    public FileSystemEntryPredicate? ShouldRecursePredicate { get; set; }
    public FileSystemEntryPredicate? ShouldScanFilePredicate { get; set; }
    public int DegreeOfParallelism { get; set; } = 16;
    public IFileSystem FileSystem { get; set; } = Internals.FileSystem.Instance;
}
