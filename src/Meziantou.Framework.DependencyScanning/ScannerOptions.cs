using System.Collections.Immutable;
using Meziantou.Framework.DependencyScanning.Scanners;

namespace Meziantou.Framework.DependencyScanning;

/// <summary>
/// Provides configuration options for dependency scanning operations.
/// <example>
/// <code>
/// var options = new ScannerOptions
/// {
///     DegreeOfParallelism = 8,
///     RecurseSubdirectories = true,
///     IncludedDependencyTypes = [DependencyType.NuGet, DependencyType.Npm].ToImmutableHashSet()
/// };
/// var dependencies = await DependencyScanner.ScanDirectoryAsync("C:\\MyProject", options, cancellationToken);
/// </code>
/// </example>
/// </summary>
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

    private DependencyScanner[]? _enabledScanners;

    /// <summary>Gets or sets the collection of scanners to use. Defaults to all built-in scanners.</summary>
    public ImmutableArray<DependencyScanner> Scanners
    {
        get => field; set
        {
            ThrowIfFrozen(); field = value;
        }
    } = DefaultScanners;

    /// <summary>Gets or sets a value indicating whether to recurse into subdirectories. Default is <see langword="true"/>.</summary>
    public bool RecurseSubdirectories { get; set; } = true;

    /// <summary>Gets or sets a predicate to filter which subdirectories to recurse into.</summary>
    public FileSystemEntryPredicate? ShouldRecursePredicate { get; set; }

    /// <summary>Gets or sets a predicate to filter which files to scan.</summary>
    public FileSystemEntryPredicate? ShouldScanFilePredicate { get; set; }

    /// <summary>Gets or sets the maximum number of parallel scanning tasks. Default is 16.</summary>
    public int DegreeOfParallelism { get; set; } = 16;

    /// <summary>Gets or sets the file system implementation to use for file access.</summary>
    public IFileSystem FileSystem { get; set; } = Internals.FileSystem.Instance;

    /// <summary>Gets or sets the set of dependency types to include. When set, only these types will be scanned. Default is empty (all types included).</summary>
    public ImmutableHashSet<DependencyType> IncludedDependencyTypes
    {
        get => field;
        set
        {
            ThrowIfFrozen();
            field = value;
        }
    } = [];

    /// <summary>Gets or sets the set of dependency types to exclude from scanning. Default is empty (no types excluded).</summary>
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
        if (_enabledScanners is not null)
            throw new InvalidOperationException("The options are frozen and cannot be modified.");
    }
}
