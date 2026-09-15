using System.Collections.Concurrent;
using System.IO.Enumeration;
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
    /// <param name="path">The root directory path to scan.</param>
    /// <param name="options">The scanner options, or <see langword="null"/> to use defaults.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A collection of all discovered dependencies.</returns>
    public static async Task<IReadOnlyCollection<Dependency>> ScanDirectoryAsync(string path, ScannerOptions? options, CancellationToken cancellationToken = default)
    {
        var result = new ConcurrentBag<Dependency>();
        await ScanDirectoryAsync(path, options, result.Add, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>Scans a directory and its subdirectories for dependencies, invoking a callback for each dependency found.</summary>
    /// <param name="path">The root directory path to scan.</param>
    /// <param name="options">The scanner options, or <see langword="null"/> to use defaults.</param>
    /// <param name="onDependencyFound">The callback invoked when a dependency is found.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="DirectoryNotFoundException">The directory does not exist. The exception is reported through the returned task.</exception>
    public static async Task ScanDirectoryAsync(string path, ScannerOptions? options, DependencyFound onDependencyFound, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException(path);

        options ??= ScannerOptions.Default;
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
    /// <param name="rootDirectory">The root directory context for relative path resolution.</param>
    /// <param name="filePath">The path of the file being scanned.</param>
    /// <param name="content">The file content as a byte array.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A collection of dependencies found in the file.</returns>
    public static Task<IReadOnlyCollection<Dependency>> ScanFileAsync(string rootDirectory, string filePath, byte[] content, CancellationToken cancellationToken = default)
    {
        return ScanFileAsync(rootDirectory, filePath, content, scanners: null, cancellationToken);
    }

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

        var result = new ConcurrentBag<Dependency>();
        await ScanFileAsync(options, result.Add, rootDirectory, filePath, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>Scans a single file from the file system for dependencies.</summary>
    /// <param name="rootDirectory">The root directory context for relative path resolution.</param>
    /// <param name="filePath">The path of the file to scan.</param>
    /// <param name="options">The scanner options, or <see langword="null"/> to use defaults.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A collection of dependencies found in the file.</returns>
    public static async Task<IReadOnlyCollection<Dependency>> ScanFileAsync(string rootDirectory, string filePath, ScannerOptions? options, CancellationToken cancellationToken = default)
    {
        options ??= ScannerOptions.Default;
        var result = new ConcurrentBag<Dependency>();
        await ScanFileAsync(options, result.Add, rootDirectory, filePath, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>Scans multiple specific files for dependencies.</summary>
    /// <param name="rootDirectory">The root directory context for relative path resolution.</param>
    /// <param name="filePaths">The collection of file paths to scan.</param>
    /// <param name="options">The scanner options, or <see langword="null"/> to use defaults.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A collection of all dependencies found across the specified files.</returns>
    public static async Task<IReadOnlyCollection<Dependency>> ScanFilesAsync(string rootDirectory, IEnumerable<string> filePaths, ScannerOptions? options, CancellationToken cancellationToken = default)
    {
        options ??= ScannerOptions.Default;
        var result = new ConcurrentBag<Dependency>();
        await ScanFilesAsync(rootDirectory, filePaths, options, result.Add, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>Scans multiple specific files for dependencies, invoking a callback for each dependency found.</summary>
    /// <param name="rootDirectory">The root directory context for relative path resolution.</param>
    /// <param name="filePaths">The collection of file paths to scan.</param>
    /// <param name="options">The scanner options, or <see langword="null"/> to use defaults.</param>
    /// <param name="onDependencyFound">The callback invoked when a dependency is found.</param>
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

        var scanFileContext = new ScanFileContext(filePath, onDependencyFound, options, cancellationToken);
        try
        {
            foreach (var scanner in scanners)
            {
                if (!scanner.ShouldScanFile(rootDirectory, filePath))
                    continue;

                scanFileContext.ResetStream();
                await scanner.ScanAsync(scanFileContext).ConfigureAwait(false);
            }
        }
        finally
        {
            await scanFileContext.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task ScanDirectorySequentialAsync<T>(string path, ScannerOptions options, DependencyFound onDependencyFound, CancellationToken cancellationToken)
        where T : struct, IEnabledScannersArray
    {
        var scanners = options.EnabledScanners;
        if (scanners.Length is 0)
            return;

        using var enumerator = new ScannerFileEnumerator<T>(path, options);
        while (enumerator.MoveNext())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = enumerator.Current;
            var scanFileContext = new ScanFileContext(entry.FullPath, onDependencyFound, options, cancellationToken);
            try
            {
                for (var i = 0; i < scanners.Length; i++)
                {
                    if (!entry.Scanners.Get(i))
                    {
                        continue;
                    }

                    var scanner = scanners[i];

                    scanFileContext.ResetStream();
                    await scanner.ScanAsync(scanFileContext).ConfigureAwait(false);
                }
            }
            finally
            {
                await scanFileContext.DisposeAsync().ConfigureAwait(false);
            }
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

        // The first failure (in the enumerator or in any reader) cancels all the other tasks, so the enumerator
        // doesn't block forever on a full channel once no reader is left, and the error is reported promptly.
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
                using var enumerator = new ScannerFileEnumerator<T>(path, options);
                while (enumerator.MoveNext())
                {
                    linkedCancellationToken.ThrowIfCancellationRequested();
                    await filesToScanChannel.Writer.WriteAsync(enumerator.Current, linkedCancellationToken).ConfigureAwait(false);
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
            var scanners = options.EnabledScanners;
            while (await reader.WaitToReadAsync(linkedCancellationToken).ConfigureAwait(false))
            {
                while (reader.TryRead(out var entry))
                {
                    linkedCancellationToken.ThrowIfCancellationRequested();
                    var scanFileContext = new ScanFileContext(entry.FullPath, onDependencyFound, options, linkedCancellationToken);
                    try
                    {
                        for (var i = 0; i < scanners.Length; i++)
                        {
                            if (!entry.Scanners.Get(i))
                                continue;

                            var scanner = scanners[i];

                            scanFileContext.ResetStream();
                            await scanner.ScanAsync(scanFileContext).ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        await scanFileContext.DisposeAsync().ConfigureAwait(false);
                    }
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


    private sealed class SingleFileInMemoryFileSystem : IFileSystem
    {
        private readonly string _path;
        private readonly byte[] _content;

        public SingleFileInMemoryFileSystem(string path, byte[] content)
        {
            _path = path;
            _content = content;
        }

        public Stream OpenRead(string path)
        {
            if (path == _path)
                return new MemoryStream(_content);

            throw new FileNotFoundException("File not found", path);
        }

        public IEnumerable<string> GetFiles(string path, string pattern, SearchOption searchOptions)
        {
            // The only file known by this file system is the in-memory file
            var fileDirectory = Path.GetDirectoryName(_path.AsSpan());
            var searchDirectory = Path.TrimEndingDirectorySeparator(path.AsSpan());
            var isInSearchDirectory = fileDirectory.Equals(searchDirectory, StringComparison.Ordinal);
            if (!isInSearchDirectory && searchOptions is SearchOption.AllDirectories)
            {
                isInSearchDirectory = fileDirectory.Length > searchDirectory.Length
                    && fileDirectory.StartsWith(searchDirectory, StringComparison.Ordinal)
                    && (Path.EndsInDirectorySeparator(searchDirectory) || IsDirectorySeparator(fileDirectory[searchDirectory.Length]));
            }

            if (isInSearchDirectory && FileSystemName.MatchesWin32Expression(FileSystemName.TranslateWin32Expression(pattern), Path.GetFileName(_path.AsSpan()), ignoreCase: false))
                return [_path];

            return [];

            static bool IsDirectorySeparator(char c) => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;
        }

        public Stream OpenReadWrite(string path) => throw new NotSupportedException($"Cannot open '{path}' for writing: the dependencies were scanned from in-memory content, so their locations cannot be updated");
    }
}
