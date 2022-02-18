using System.Collections.ObjectModel;
using Meziantou.Framework.DependencyScanning.Scanners;

namespace Meziantou.Framework.DependencyScanning;

public sealed class ScannerOptions
{
    private static readonly ReadOnlyCollection<DependencyScanner> s_defaultScanners = new(new DependencyScanner[]
    {
        new DockerfileDependencyScanner(),
        new DotNetGlobalJsonDependencyScanner(),
        new DotNetToolManifestDependencyScanner(),
        new GitHubActionsScanner(),
        new GitSubmoduleDependencyScanner(),
        new MsBuildReferencesDependencyScanner(),
        new NpmPackageJsonDependencyScanner(),
        new NuSpecDependencyScanner(),
        new PackagesConfigDependencyScanner(),
        new ProjectJsonDependencyScanner(),
        new PythonRequirementsDependencyScanner(),
    });

    internal static ScannerOptions Default { get; } = new ScannerOptions();

    public IReadOnlyList<DependencyScanner> Scanners { get; set; } = s_defaultScanners;
    public bool RecurseSubdirectories { get; set; } = true;
    public FileSystemEntryPredicate? ShouldRecursePredicate { get; set; }
    public FileSystemEntryPredicate? ShouldScanFilePredicate { get; set; }
    public int DegreeOfParallelism { get; set; } = 16;
    public IFileSystem FileSystem { get; set; } = Internals.FileSystem.Instance;
}
