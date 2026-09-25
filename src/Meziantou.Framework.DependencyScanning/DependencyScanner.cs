using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using Meziantou.Framework.DependencyScanning.Internals;

namespace Meziantou.Framework.DependencyScanning;

/// <summary>
/// Provides the base class for scanning source code files to discover dependencies across multiple package ecosystems and formats.
/// <example>
/// <code>
/// // Scan a directory for all dependencies
/// var dependencies = await DependencyScanner.ScanDirectoryAsync(
///     "C:\\MyProject",
///     options: null,
///     cancellationToken);
/// 
/// foreach (var dependency in dependencies)
/// {
///     Console.WriteLine($"{dependency.Type}: {dependency.Name}@{dependency.Version}");
/// }
/// </code>
/// </example>
/// </summary>
public abstract class DependencyScanner
{
    private const uint SupportedDependencyTypesMaskInitializedFlag = 1u << 31;

    // Bit N is set when the dependency type with value N is supported (N < 31). The high bit indicates the mask is computed.
    private uint _supportedDependencyTypesMask;

    internal protected abstract IReadOnlyCollection<DependencyType> SupportedDependencyTypes { get; }

    internal bool IsDependencyTypeSupported(DependencyType type)
    {
        var value = (int)type;
        if ((uint)value >= 31)
            return SupportedDependencyTypes?.Contains(type) is true;

        var mask = _supportedDependencyTypesMask;
        if (mask is 0)
        {
            mask = SupportedDependencyTypesMaskInitializedFlag;
            if (SupportedDependencyTypes is { } supportedTypes)
            {
                foreach (var supportedType in supportedTypes)
                {
                    if ((uint)supportedType < 31)
                    {
                        mask |= 1u << (int)supportedType;
                    }
                }
            }

            _supportedDependencyTypesMask = mask;
        }

        return (mask & (1u << value)) is not 0;
    }

    /// <summary>Scans a directory and its subdirectories for dependencies.</summary>
    /// <remarks>
    /// A file that cannot be read, or that a scanner fails to scan, is skipped and reported to <see cref="ScannerOptions.OnFileScanFailed"/>.
    /// Symbolic links to directories are not followed, and symbolic links to files outside <paramref name="path"/> are not scanned.
    /// </remarks>
    /// <param name="path">The root directory path to scan.</param>
    /// <param name="options">The scanner options, or <see langword="null"/> to use defaults.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A collection of all discovered dependencies.</returns>
    /// <exception cref="DirectoryNotFoundException">The directory does not exist. The exception is reported through the returned task.</exception>
    /// <exception cref="NotSupportedException"><see cref="ScannerOptions.FileSystem"/> is a custom file system, and a file or directory predicate is set. The exception is reported through the returned task.</exception>
    public static async Task<IReadOnlyCollection<Dependency>> ScanDirectoryAsync(string path, ScannerOptions? options, CancellationToken cancellationToken = default)
    {
        // Scanners only ever produce, and the collection is enumerated once at the end. A ConcurrentBag
        // allocated a node per dependency and made every enumeration lock and copy each thread-local list,
        // which the returned collection then paid again on each pass. Snapshotting once keeps the callers cheap.
        var result = new ConcurrentQueue<Dependency>();
        await ScanDirectoryAsync(path, options, result.Enqueue, cancellationToken).ConfigureAwait(false);
        return result.ToArray();
    }

    /// <summary>Scans a directory and its subdirectories for dependencies, invoking a callback for each dependency found.</summary>
    /// <remarks>
    /// <para>
    /// Files are scanned concurrently unless <see cref="ScannerOptions.DegreeOfParallelism"/> is 1, so
    /// <paramref name="onDependencyFound"/> can be invoked concurrently from several threads and must be thread-safe.
    /// An exception thrown by <paramref name="onDependencyFound"/> stops the scan.
    /// </para>
    /// <para>
    /// A file that cannot be read, or that a scanner fails to scan, is skipped and reported to <see cref="ScannerOptions.OnFileScanFailed"/>.
    /// Symbolic links to directories are not followed, and symbolic links to files outside <paramref name="path"/> are not scanned.
    /// </para>
    /// </remarks>
    /// <param name="path">The root directory path to scan.</param>
    /// <param name="options">The scanner options, or <see langword="null"/> to use defaults.</param>
    /// <param name="onDependencyFound">The callback invoked when a dependency is found. It can be invoked concurrently.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="DirectoryNotFoundException">The directory does not exist. The exception is reported through the returned task.</exception>
    /// <exception cref="NotSupportedException"><see cref="ScannerOptions.FileSystem"/> is a custom file system, and a file or directory predicate is set. The exception is reported through the returned task.</exception>
    public static async Task ScanDirectoryAsync(string path, ScannerOptions? options, DependencyFound onDependencyFound, CancellationToken cancellationToken = default)
    {
        options ??= ScannerOptions.Default;
        if (options.UsesDefaultFileSystem)
        {
            if (!Directory.Exists(path))
                throw new DirectoryNotFoundException(path);
        }
        else if (options.ShouldScanFilePredicate is not null || options.ShouldRecursePredicate is not null)
        {
            // The predicates take a FileSystemEntry, which only an enumeration of the physical disk can create
            throw new NotSupportedException($"{nameof(ScannerOptions.ShouldScanFilePredicate)} and {nameof(ScannerOptions.ShouldRecursePredicate)} are not supported with a custom file system. Filter the files in {nameof(IFileSystem)}.{nameof(IFileSystem.GetFiles)}, or scan a list of files with {nameof(ScanFilesAsync)}.");
        }

        var scanners = options.EnabledScanners;
        if (scanners.Length is 0)
            return;

        var degreeOfParallelism = options.EffectiveDegreeOfParallelism;
        if (scanners.Length <= EnabledScannersArray32.MaxValues)
        {
            await ScanDirectoryAsync<EnabledScannersArray32>(path, options, degreeOfParallelism, onDependencyFound, cancellationToken).ConfigureAwait(false);
        }
        else if (scanners.Length <= EnabledScannersArray64.MaxValues)
        {
            await ScanDirectoryAsync<EnabledScannersArray64>(path, options, degreeOfParallelism, onDependencyFound, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await ScanDirectoryAsync<EnabledScannersArray>(path, options, degreeOfParallelism, onDependencyFound, cancellationToken).ConfigureAwait(false);
        }
    }

    private static Task ScanDirectoryAsync<T>(string path, ScannerOptions options, int degreeOfParallelism, DependencyFound onDependencyFound, CancellationToken cancellationToken)
        where T : struct, IEnabledScannersArray
    {
        return degreeOfParallelism is 1
            ? ScanDirectorySequentialAsync<T>(path, options, onDependencyFound, cancellationToken)
            : ScanDirectoryParallelAsync<T>(path, options, degreeOfParallelism, onDependencyFound, cancellationToken);
    }

    /// <summary>Scans a single file from memory for dependencies.</summary>
    /// <remarks>
    /// When a scanner fails to scan the content, the dependencies found before the failure are returned, and the
    /// failure is not reported. The locations of the dependencies are not updatable, because there is no file to write.
    /// </remarks>
    /// <param name="rootDirectory">The root directory context for relative path resolution.</param>
    /// <param name="filePath">The path of the file being scanned.</param>
    /// <param name="content">The file content as a byte array.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A collection of dependencies found in the file.</returns>
    public static Task<IReadOnlyCollection<Dependency>> ScanFileAsync(string rootDirectory, string filePath, byte[] content, CancellationToken cancellationToken = default)
    {
        return ScanFileAsync(rootDirectory, filePath, content, scanners: null, cancellationToken);
    }

    /// <summary>Scans a single file from memory for dependencies, using the specified scanners.</summary>
    /// <remarks>
    /// When a scanner fails to scan the content, the dependencies found before the failure are returned, and the
    /// failure is not reported. The locations of the dependencies are not updatable, because there is no file to write.
    /// </remarks>
    /// <param name="rootDirectory">The root directory context for relative path resolution.</param>
    /// <param name="filePath">The path of the file being scanned.</param>
    /// <param name="content">The file content as a byte array.</param>
    /// <param name="scanners">The scanners to use, or <see langword="null"/> to use the default scanners.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A collection of dependencies found in the file.</returns>
    public static async Task<IReadOnlyCollection<Dependency>> ScanFileAsync(string rootDirectory, string filePath, byte[] content, IReadOnlyList<DependencyScanner>? scanners, CancellationToken cancellationToken = default)
    {
        var options = new ScannerOptions
        {
            FileSystem = new SingleFileInMemoryFileSystem(filePath, content),
        };

        if (scanners is not null)
        {
            options.Scanners = [.. scanners];
        }

        var result = new ConcurrentQueue<Dependency>();
        await ScanFileAsync(options, result.Enqueue, rootDirectory, filePath, cancellationToken).ConfigureAwait(false);
        return result.ToArray();
    }

    /// <summary>Scans a single file from the file system for dependencies.</summary>
    /// <remarks>
    /// A single file is handled like a file of a directory scan: when the file cannot be read, or a scanner fails to
    /// scan it, the failure is reported to <see cref="ScannerOptions.OnFileScanFailed"/>, the remaining scanners are
    /// skipped, and the dependencies found before the failure are returned.
    /// </remarks>
    /// <param name="rootDirectory">The root directory context for relative path resolution.</param>
    /// <param name="filePath">The path of the file to scan.</param>
    /// <param name="options">The scanner options, or <see langword="null"/> to use defaults.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A collection of dependencies found in the file.</returns>
    public static async Task<IReadOnlyCollection<Dependency>> ScanFileAsync(string rootDirectory, string filePath, ScannerOptions? options, CancellationToken cancellationToken = default)
    {
        options ??= ScannerOptions.Default;
        var result = new ConcurrentQueue<Dependency>();
        await ScanFileAsync(options, result.Enqueue, rootDirectory, filePath, cancellationToken).ConfigureAwait(false);
        return result.ToArray();
    }

    /// <summary>Scans multiple specific files for dependencies.</summary>
    /// <remarks>A file that cannot be read, or that a scanner fails to scan, is skipped and reported to <see cref="ScannerOptions.OnFileScanFailed"/>.</remarks>
    /// <param name="rootDirectory">The root directory context for relative path resolution.</param>
    /// <param name="filePaths">The collection of file paths to scan.</param>
    /// <param name="options">The scanner options, or <see langword="null"/> to use defaults.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A collection of all dependencies found across the specified files.</returns>
    public static async Task<IReadOnlyCollection<Dependency>> ScanFilesAsync(string rootDirectory, IEnumerable<string> filePaths, ScannerOptions? options, CancellationToken cancellationToken = default)
    {
        options ??= ScannerOptions.Default;
        var result = new ConcurrentQueue<Dependency>();
        await ScanFilesAsync(rootDirectory, filePaths, options, result.Enqueue, cancellationToken).ConfigureAwait(false);
        return result.ToArray();
    }

    /// <summary>Scans multiple specific files for dependencies, invoking a callback for each dependency found.</summary>
    /// <remarks>
    /// Files are scanned concurrently unless <see cref="ScannerOptions.DegreeOfParallelism"/> is 1, so
    /// <paramref name="onDependencyFound"/> can be invoked concurrently from several threads and must be thread-safe.
    /// An exception thrown by <paramref name="onDependencyFound"/> stops the scan. A file that cannot be read, or that
    /// a scanner fails to scan, is skipped and reported to <see cref="ScannerOptions.OnFileScanFailed"/>.
    /// </remarks>
    /// <param name="rootDirectory">The root directory context for relative path resolution.</param>
    /// <param name="filePaths">The collection of file paths to scan.</param>
    /// <param name="options">The scanner options, or <see langword="null"/> to use defaults.</param>
    /// <param name="onDependencyFound">The callback invoked when a dependency is found. It can be invoked concurrently.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public static Task ScanFilesAsync(string rootDirectory, IEnumerable<string> filePaths, ScannerOptions? options, DependencyFound onDependencyFound, CancellationToken cancellationToken = default)
    {
        options ??= ScannerOptions.Default;
        if (options.EnabledScanners.Length is 0)
            return Task.CompletedTask;

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = options.EffectiveDegreeOfParallelism,
            CancellationToken = cancellationToken,
        };
        return Parallel.ForEachAsync(filePaths, parallelOptions, async (filePath, cancellationToken) => await ScanFileAsync(options, onDependencyFound, rootDirectory, filePath, cancellationToken).ConfigureAwait(false));
    }

    private static async Task ScanFileAsync(ScannerOptions options, DependencyFound onDependencyFound, string rootDirectory, string filePath, CancellationToken cancellationToken)
    {
        var scanners = options.EnabledScanners;
        if (scanners.Length is 0)
            return;

        // Scanners that filter on the relative directory need both paths absolute and normalized. A custom file system
        // gets the path as the caller wrote it, as it may not be a path of the physical disk.
        var (candidateRootDirectory, candidateFilePath) = ScanPathUtilities.NormalizeCandidatePaths(rootDirectory, filePath);
        var fullPath = options.UsesDefaultFileSystem ? candidateFilePath : filePath;

        var scanFileContext = new ScanFileContext(fullPath, onDependencyFound, options, cancellationToken);
        try
        {
            foreach (var scanner in scanners)
            {
                if (!scanner.ShouldScanFile(candidateRootDirectory, candidateFilePath))
                    continue;

                scanFileContext.ResetStream();
                await scanner.ScanAsync(scanFileContext).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (scanFileContext.IsFileScanFailure(ex))
        {
            options.OnFileScanFailed?.Invoke(fullPath, ex);
        }
        finally
        {
            await scanFileContext.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task ScanFileAsync<T>(ScannerOptions options, DependencyFound onDependencyFound, FileToScan<T> file, CancellationToken cancellationToken)
        where T : struct, IEnabledScannersArray
    {
        var scanners = options.EnabledScanners;
        var scanFileContext = new ScanFileContext(file.FullPath, onDependencyFound, options, cancellationToken);
        try
        {
            for (var i = 0; i < scanners.Length; i++)
            {
                if (!file.Scanners.Get(i))
                    continue;

                scanFileContext.ResetStream();
                await scanners[i].ScanAsync(scanFileContext).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (scanFileContext.IsFileScanFailure(ex))
        {
            // One file the scanners cannot read must not lose the dependencies of all the other files
            options.OnFileScanFailed?.Invoke(file.FullPath, ex);
        }
        finally
        {
            await scanFileContext.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static IEnumerable<FileToScan<T>> EnumerateFilesToScan<T>(string path, ScannerOptions options)
        where T : struct, IEnabledScannersArray
    {
        if (options.UsesDefaultFileSystem)
        {
            using var enumerator = new ScannerFileEnumerator<T>(path, options);
            while (enumerator.MoveNext())
            {
                yield return enumerator.Current;
            }

            yield break;
        }

        var searchOption = options.RecurseSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        foreach (var filePath in options.FileSystem.GetFiles(path, "*", searchOption))
        {
            if (TryGetScanners<T>(options, path, filePath, out var scanners))
                yield return new FileToScan<T>(scanners, filePath);
        }
    }

    private static bool TryGetScanners<T>(ScannerOptions options, string rootDirectory, string filePath, out T scanners)
        where T : struct, IEnabledScannersArray
    {
        scanners = new T();
        try
        {
            var (candidateRootDirectory, candidateFilePath) = ScanPathUtilities.NormalizeCandidatePaths(rootDirectory, filePath);
            var enabledScanners = options.EnabledScanners;
            for (var i = 0; i < enabledScanners.Length; i++)
            {
                if (enabledScanners[i].ShouldScanFile(candidateRootDirectory, candidateFilePath))
                {
                    scanners.Set(i);
                }
            }
        }
        catch (Exception ex)
        {
            options.OnFileScanFailed?.Invoke(filePath, ex);
            return false;
        }

        return !scanners.IsEmpty;
    }

    private static async Task ScanDirectorySequentialAsync<T>(string path, ScannerOptions options, DependencyFound onDependencyFound, CancellationToken cancellationToken)
        where T : struct, IEnabledScannersArray
    {
        foreach (var file in EnumerateFilesToScan<T>(path, options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ScanFileAsync(options, onDependencyFound, file, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task ScanDirectoryParallelAsync<T>(string path, ScannerOptions options, int degreeOfParallelism, DependencyFound onDependencyFound, CancellationToken cancellationToken)
        where T : struct, IEnabledScannersArray
    {
        var filesToScanChannel = Channel.CreateBounded<FileToScan<T>>(new BoundedChannelOptions(10000)
        {
            AllowSynchronousContinuations = true,
            SingleWriter = true,
            SingleReader = false,
            FullMode = BoundedChannelFullMode.Wait,
        });

        // The first failure (in the enumerator, in a callback, or a cancellation) cancels all the other tasks, so the
        // enumerator doesn't block forever on a full channel once no reader is left, and the error is reported promptly.
        // A file that cannot be scanned is not a failure: it is skipped and reported to OnFileScanFailed.
        using var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var linkedCancellationToken = linkedCancellationTokenSource.Token;
        Exception? firstException = null;

        var tasks = new Task[degreeOfParallelism + 1];
        tasks[0] = Task.Run(() => RunAndCaptureFirstExceptionAsync(EnumerateFilesAsync), CancellationToken.None);
        for (var i = 1; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(() => RunAndCaptureFirstExceptionAsync(ScanFilesFromChannelAsync), CancellationToken.None);
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        if (firstException is not null)
        {
            ExceptionDispatchInfo.Throw(firstException);
        }

        async Task RunAndCaptureFirstExceptionAsync(Func<Task> action)
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (Interlocked.CompareExchange(ref firstException, ex, comparand: null) is null)
                {
                    await linkedCancellationTokenSource.CancelAsync().ConfigureAwait(false);
                }
            }
        }

        async Task EnumerateFilesAsync()
        {
            try
            {
                foreach (var file in EnumerateFilesToScan<T>(path, options))
                {
                    linkedCancellationToken.ThrowIfCancellationRequested();
                    await filesToScanChannel.Writer.WriteAsync(file, linkedCancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                filesToScanChannel.Writer.Complete();
            }
        }

        async Task ScanFilesFromChannelAsync()
        {
            var reader = filesToScanChannel.Reader;
            while (await reader.WaitToReadAsync(linkedCancellationToken).ConfigureAwait(false))
            {
                while (reader.TryRead(out var file))
                {
                    linkedCancellationToken.ThrowIfCancellationRequested();
                    await ScanFileAsync(options, onDependencyFound, file, linkedCancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>Determines whether this scanner should scan the specified file.</summary>
    /// <param name="rootDirectory">The root directory path.</param>
    /// <param name="fullPath">The full path of the file.</param>
    /// <returns><see langword="true"/> if the file should be scanned; otherwise, <see langword="false"/>.</returns>
    public bool ShouldScanFile(ReadOnlySpan<char> rootDirectory, ReadOnlySpan<char> fullPath)
    {
        return ShouldScanFileCore(new CandidateFileContext(rootDirectory, Path.GetDirectoryName(fullPath), Path.GetFileName(fullPath)));
    }

    /// <summary>Determines whether this scanner should scan the specified file.</summary>
    /// <param name="context">The context containing information about the candidate file.</param>
    /// <returns><see langword="true"/> if the file should be scanned; otherwise, <see langword="false"/>.</returns>
    public bool ShouldScanFile(CandidateFileContext context)
    {
        return ShouldScanFileCore(context);
    }

    /// <summary>When overridden in a derived class, determines whether this scanner should scan the specified file.</summary>
    /// <param name="context">The context containing information about the candidate file.</param>
    /// <returns><see langword="true"/> if the file should be scanned; otherwise, <see langword="false"/>.</returns>
    protected abstract bool ShouldScanFileCore(CandidateFileContext context);

    /// <summary>When overridden in a derived class, performs the actual scanning of a file to discover dependencies.</summary>
    /// <param name="context">The context containing the file to scan and methods to report discovered dependencies.</param>
    public abstract ValueTask ScanAsync(ScanFileContext context);
}
