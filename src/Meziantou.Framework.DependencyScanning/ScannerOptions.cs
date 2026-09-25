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
///     IncludedDependencyTypes = [DependencyType.NuGet, DependencyType.Npm]
/// };
/// var dependencies = await DependencyScanner.ScanDirectoryAsync("C:\\MyProject", options, cancellationToken);
/// </code>
/// </example>
/// </summary>
public sealed class ScannerOptions
{
    private static readonly ImmutableArray<DependencyScanner> DefaultScanners =
    [
        new AgentPluginDependencyScanner(),
        new AzureDevOpsScanner(),
        new CargoDependencyScanner(),
        new DockerComposeDependencyScanner(),
        new DockerfileDependencyScanner(),
        new DotNetFileBasedAppDependencyScanner(),
        new DotNetGlobalJsonDependencyScanner(),
        new DotNetToolManifestDependencyScanner(),
        new GitHubActionsScanner(),
        new GitSubmoduleDependencyScanner(),
        new GoModuleDependencyScanner(),
        new HelmChartDependencyScanner(),
        new JavaDependencyScanner(),
        new KubernetesDependencyScanner(),
        new MsBuildReferencesDependencyScanner(),
        new NpmPackageJsonDependencyScanner(),
        new NuGetLockFileDependencyScanner(),
        new NuSpecDependencyScanner(),
        new PackagesConfigDependencyScanner(),
        new ProjectAssetsDependencyScanner(),
        new ProjectJsonDependencyScanner(),
        new ComposerDependencyScanner(),
        new PythonRequirementsDependencyScanner(),
        new PythonProjectDependencyScanner(),
        new RenovateExtendsDependencyScanner(),
        new RubyGemDependencyScanner(),
        new SwiftPackageDependencyScanner(),
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
    /// <remarks>
    /// Symbolic links and other reparse points to directories are never followed, whatever the predicate returns.
    /// The predicate is only supported with the default <see cref="FileSystem"/>: a directory scan with a custom file
    /// system throws a <see cref="NotSupportedException"/> when it is set.
    /// </remarks>
    public FileSystemEntryPredicate? ShouldRecursePredicate { get; set; }

    /// <summary>Gets or sets a predicate to filter which files to scan.</summary>
    /// <remarks>
    /// The predicate is only supported with the default <see cref="FileSystem"/>: a directory scan with a custom file
    /// system throws a <see cref="NotSupportedException"/> when it is set.
    /// </remarks>
    public FileSystemEntryPredicate? ShouldScanFilePredicate { get; set; }

    /// <summary>Gets or sets the callback invoked when a file cannot be scanned. Default is <see langword="null"/>: the file is silently skipped.</summary>
    /// <remarks>
    /// A file that cannot be opened or read, or that a scanner fails to scan, is skipped and the scan continues with
    /// the next file. The callback receives the path of the file and the exception. It can be invoked concurrently,
    /// and an exception it throws stops the scan. Cancellation is not a failure: the scan methods throw an
    /// <see cref="OperationCanceledException"/> when their cancellation token is canceled.
    /// </remarks>
    public FileScanFailed? OnFileScanFailed { get; set; }

    /// <summary>Gets or sets the maximum number of parallel scanning tasks, or <c>-1</c> to use <see cref="Environment.ProcessorCount"/>. Default is 16.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is <c>0</c> or less than <c>-1</c>.</exception>
    public int DegreeOfParallelism
    {
        get => field;
        set
        {
            if (value is 0 or < -1)
                throw new ArgumentOutOfRangeException(nameof(value), value, "The degree of parallelism must be greater than 0, or -1 to use the number of processors.");

            field = value;
        }
    } = 16;

    internal int EffectiveDegreeOfParallelism => DegreeOfParallelism is -1 ? Environment.ProcessorCount : DegreeOfParallelism;

    /// <summary>Gets or sets the file system implementation to use for file access.</summary>
    /// <remarks>
    /// With a custom file system, a directory scan lists the files with <see cref="IFileSystem.GetFiles"/> using the
    /// <c>*</c> pattern, and does not support <see cref="ShouldScanFilePredicate"/> and <see cref="ShouldRecursePredicate"/>.
    /// </remarks>
    public IFileSystem FileSystem { get; set; } = Internals.FileSystem.Instance;

    internal bool UsesDefaultFileSystem => ReferenceEquals(FileSystem, Internals.FileSystem.Instance);

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
