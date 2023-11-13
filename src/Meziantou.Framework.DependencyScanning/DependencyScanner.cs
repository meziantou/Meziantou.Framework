using System.Collections.Concurrent;
using System.Threading.Channels;
using Meziantou.Framework.DependencyScanning.Internals;

namespace Meziantou.Framework.DependencyScanning;

public abstract class DependencyScanner
{
    public static async Task<IReadOnlyCollection<Dependency>> ScanDirectoryAsync(string path, ScannerOptions? options, CancellationToken cancellationToken = default)
    {
        var result = new ConcurrentBag<Dependency>();
        await ScanDirectoryAsync(path, options, result.Add, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public static Task ScanDirectoryAsync(string path, ScannerOptions? options, DependencyFound onDependencyFound, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException(path);

        options ??= ScannerOptions.Default;
        if (options.Scanners.Count == 0)
            return Task.CompletedTask;

        if (options.Scanners.Count <= EnabledScannersArray32.MaxValues)
            return options.DegreeOfParallelism == 1 ? ScanDirectoryAsync<EnabledScannersArray32>(path, options, onDependencyFound, cancellationToken) : ScanDirectoryParallelAsync<EnabledScannersArray32>(path, options, onDependencyFound, cancellationToken);

        if (options.Scanners.Count <= EnabledScannersArray64.MaxValues)
            return options.DegreeOfParallelism == 1 ? ScanDirectoryAsync<EnabledScannersArray64>(path, options, onDependencyFound, cancellationToken) : ScanDirectoryParallelAsync<EnabledScannersArray64>(path, options, onDependencyFound, cancellationToken);

        return options.DegreeOfParallelism == 1 ? ScanDirectoryAsync<EnabledScannersArray>(path, options, onDependencyFound, cancellationToken) : ScanDirectoryParallelAsync<EnabledScannersArray>(path, options, onDependencyFound, cancellationToken);
    }

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
            options.Scanners = scanners;
        }

        var result = new ConcurrentBag<Dependency>();
        await ScanFileAsync(options, result.Add, rootDirectory, filePath, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public static async Task<IReadOnlyCollection<Dependency>> ScanFileAsync(string rootDirectory, string filePath, ScannerOptions? options, CancellationToken cancellationToken = default)
    {
        options ??= ScannerOptions.Default;
        if (options.Scanners.Count == 0)
            return Array.Empty<Dependency>();

        var result = new ConcurrentBag<Dependency>();
        await ScanFileAsync(options, result.Add, rootDirectory, filePath, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public static async Task<IReadOnlyCollection<Dependency>> ScanFilesAsync(string rootDirectory, IEnumerable<string> filePaths, ScannerOptions? options, CancellationToken cancellationToken = default)
    {
        options ??= ScannerOptions.Default;
        if (options.Scanners.Count == 0)
            return Array.Empty<Dependency>();

        var result = new ConcurrentBag<Dependency>();
        await ScanFilesAsync(rootDirectory, filePaths, options, result.Add, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public static Task ScanFilesAsync(string rootDirectory, IEnumerable<string> filePaths, ScannerOptions? options, DependencyFound onDependencyFound, CancellationToken cancellationToken = default)
    {
        options ??= ScannerOptions.Default;
        if (options.Scanners.Count == 0)
            return Task.CompletedTask;

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = options.DegreeOfParallelism,
            CancellationToken = cancellationToken,
        };
        return Parallel.ForEachAsync(filePaths, parallelOptions, async (filePath, cancellationToken) => await ScanFileAsync(options, onDependencyFound, rootDirectory, filePath, cancellationToken).ConfigureAwait(false));
    }

    private static async Task ScanFileAsync(ScannerOptions options, DependencyFound onDependencyFound, string rootDirectory, string filePath, CancellationToken cancellationToken)
    {
        var scanFileContext = new ScanFileContext(filePath, onDependencyFound, options.FileSystem, cancellationToken);
        try
        {
            foreach (var scanner in options.Scanners)
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

    private static async Task ScanDirectoryAsync<T>(string path, ScannerOptions options, DependencyFound onDependencyFound, CancellationToken cancellationToken)
        where T : struct, IEnabledScannersArray
    {
        var fileSystem = options.FileSystem;
        using var enumerator = new ScannerFileEnumerator<T>(path, options);
        while (enumerator.MoveNext())
        {
            var entry = enumerator.Current;
            var scanFileContext = new ScanFileContext(entry.FullPath, onDependencyFound, fileSystem, cancellationToken);
            try
            {
                for (var i = 0; i < options.Scanners.Count; i++)
                {
                    if (!entry.Scanners.Get(i))
                    {
                        continue;
                    }

                    var scanner = options.Scanners[i];

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

    private static Task ScanDirectoryParallelAsync<T>(string path, ScannerOptions options, DependencyFound onDependencyFound, CancellationToken cancellationToken)
        where T : struct, IEnabledScannersArray
    {
        var fileSystem = options.FileSystem;
        var filesToScanChannel = Channel.CreateBounded<FileToScan<T>>(new BoundedChannelOptions(10000)
        {
            AllowSynchronousContinuations = true,
            SingleWriter = true,
            SingleReader = false,
            FullMode = BoundedChannelFullMode.Wait,
        });

        // Start enumerating
        var enumeratorTask = Task.Run(async () =>
        {
            try
            {
                using var enumerator = new ScannerFileEnumerator<T>(path, options);
                while (enumerator.MoveNext())
                {
                    await filesToScanChannel.Writer.WriteAsync(enumerator.Current, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                filesToScanChannel.Writer.Complete();
            }
        }, cancellationToken);

        // Parse files
        var tasks = new Task[options.DegreeOfParallelism + 1];
        tasks[0] = enumeratorTask;
        Array.Fill(tasks, Task.Run(async () =>
        {
            var reader = filesToScanChannel.Reader;
            while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (reader.TryRead(out var entry))
                {
                    var scanFileContext = new ScanFileContext(entry.FullPath, onDependencyFound, fileSystem, cancellationToken);
                    try
                    {
                        for (var i = 0; i < options.Scanners.Count; i++)
                        {
                            if (!entry.Scanners.Get(i))
                                continue;

                            var scanner = options.Scanners[i];

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
        }, cancellationToken), startIndex: 1, options.DegreeOfParallelism);

        return Task.WhenAll(tasks);
    }

    public bool ShouldScanFile(ReadOnlySpan<char> rootDirectory, ReadOnlySpan<char> fullPath)
    {
        return ShouldScanFileCore(new CandidateFileContext(rootDirectory, Path.GetDirectoryName(fullPath), Path.GetFileName(fullPath)));
    }

    public bool ShouldScanFile(CandidateFileContext context)
    {
        return ShouldScanFileCore(context);
    }

    protected abstract bool ShouldScanFileCore(CandidateFileContext context);

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

        public IEnumerable<string> GetFiles(string path, string pattern, SearchOption searchOptions) => throw new NotSupportedException();
        public Stream OpenReadWrite(string path) => throw new NotSupportedException();
    }
}
