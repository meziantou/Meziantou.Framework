using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Meziantou.Framework.DependencyScanning
{
    public sealed class ScannerOptions
    {
        private static readonly ReadOnlyCollection<DependencyScanner> s_defaultScanners = new ReadOnlyCollection<DependencyScanner>(new DependencyScanner[]
        {
            new DockerfileDependencyScanner(),
            new GitSubmoduleDependencyScanner(),
            new NpmPackageJsonDependencyScanner(),
            new NuSpecDependencyScanner(),
            new PackageReferencesDependencyScanner(),
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
}
