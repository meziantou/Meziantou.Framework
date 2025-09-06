using System.Collections.Immutable;
using Meziantou.Framework.DependencyScanning.Scanners;

namespace Meziantou.Framework.DependencyScanning;

public sealed class ScannerOptions
{
    private static readonly ImmutableArray<DependencyScanner> DefaultScanners =
    [
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
        new RenovateExtendsDependencyScanner(),
    ];

    internal static ScannerOptions Default { get; } = new ScannerOptions();

    private DependencyScanner[] _enabledScanners;

    public ImmutableArray<DependencyScanner> Scanners
    {
        get => field; set
        {
            ThrowIfFrozen(); field = value;
        }
    } = DefaultScanners;

    public bool RecurseSubdirectories { get; set; } = true;
    public FileSystemEntryPredicate? ShouldRecursePredicate { get; set; }
    public FileSystemEntryPredicate? ShouldScanFilePredicate { get; set; }
    public int DegreeOfParallelism { get; set; } = 16;
    public IFileSystem FileSystem { get; set; } = Internals.FileSystem.Instance;

    public ImmutableHashSet<DependencyType> IncludedDependencyTypes
    {
        get => field;
        set
        {
            ThrowIfFrozen();
            field = value;
        }
    } = [];

    public ImmutableHashSet<DependencyType> ExcludedDependencyTypes
    {
        get => field;
        set
        {
            ThrowIfFrozen();
            field = value;
        }
    } = [];


    internal DependencyScanner[] EnabledScanners
    {
        get
        {
            return _enabledScanners ??= GetEnabledScanners();
        }
    }

    private DependencyScanner[] GetEnabledScanners()
    {
        if (IncludedDependencyTypes.Count > 0 || ExcludedDependencyTypes.Count > 0)
        {
            // Filter scanners based on supported dependency types
            var filteredScanners = new List<DependencyScanner>();

            foreach (var scanner in Scanners)
            {
                var supportedTypes = scanner.SupportedDependencyTypes;

                // If included types are specified, scanner must support at least one included type
                if (IncludedDependencyTypes.Count > 0)
                {
                    if (!supportedTypes.Any(type => IncludedDependencyTypes.Contains(type)))
                        continue;
                }

                // If excluded types are specified, scanner must support at least one non-excluded type
                if (ExcludedDependencyTypes.Count > 0)
                {
                    if (supportedTypes.All(type => ExcludedDependencyTypes.Contains(type)))
                        continue;
                }

                filteredScanners.Add(scanner);
            }

            return [.. filteredScanners];
        }

        return [.. Scanners];
    }

    private void ThrowIfFrozen()
    {
        if (_enabledScanners != null)
            throw new InvalidOperationException("The options are frozen and cannot be modified.");
    }
}
