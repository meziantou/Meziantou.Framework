using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.Globbing;

namespace Meziantou.Framework.DependencyScanning
{
    public abstract class DependencyScanner
    {
        public static IAsyncEnumerable<Dependency> ScanDirectoryAsync(string path, ScannerOptions? options, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(path))
                throw new DirectoryNotFoundException(path);

            options ??= ScannerOptions.Default;
            if (options.Scanners.Count == 0)
                return EmptyAsyncEnumerable<Dependency>.Instance;

            if (options.DegreeOfParallelism == 1)
                return ScanDirectorySingleThreadedAsync(path, options, cancellationToken);

            if (options.Scanners.Count <= EnabledScannersArray32.MaxValues)
                return ScanDirectoryParallelAsync<EnabledScannersArray32>(path, options, cancellationToken);

            if (options.Scanners.Count <= EnabledScannersArray64.MaxValues)
                return ScanDirectoryParallelAsync<EnabledScannersArray64>(path, options, cancellationToken);

            return ScanDirectoryParallelAsync<EnabledScannersArray>(path, options, cancellationToken);
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

        private static async Task ScanDirectoryAsync<T>(string path, ScannerOptions options, DependencyFound onDependencyFound, CancellationToken cancellationToken = default)
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

        private static async Task ScanDirectoryParallelAsync<T>(string path, ScannerOptions options, DependencyFound onDependencyFound, CancellationToken cancellationToken = default)
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

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        private static async IAsyncEnumerable<Dependency> ScanDirectorySingleThreadedAsync(string path, ScannerOptions options, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var result = new List<Dependency>();
            await ScanDirectoryAsync(path, options, dep => { result.Add(dep); return default; }, cancellationToken).ConfigureAwait(false);
            foreach (var item in result)
                yield return item;
        }

        private static async IAsyncEnumerable<Dependency> ScanDirectoryParallelAsync<T>(string path, ScannerOptions options, [EnumeratorCancellation] CancellationToken cancellationToken = default)
            where T : struct, IEnabledScannersArray
        {
            var fileSystem = options.FileSystem;
            var filesToScanChannel = Channel.CreateBounded<FileToScan<T>>(new BoundedChannelOptions(1000)
            {
                AllowSynchronousContinuations = true,
                SingleWriter = true,
                SingleReader = false,
                FullMode = BoundedChannelFullMode.Wait,
            });

            var dependenciesChannel = Channel.CreateUnbounded<Dependency>(new UnboundedChannelOptions
            {
                AllowSynchronousContinuations = true,
                SingleWriter = false,
                SingleReader = true,
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
            var exceptions = new ConcurrentBag<Exception>();
            var tasks = new Task[options.DegreeOfParallelism];
            Array.Fill(tasks, Task.Run(async () =>
            {
                var reader = filesToScanChannel.Reader;
                while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (reader.TryRead(out var entry))
                    {
                        var scanFileContext = new ScanFileContext(entry.FullPath, d => dependenciesChannel.Writer.WriteAsync(d, cancellationToken), fileSystem, cancellationToken);
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
            }, cancellationToken));

            var whenAllTasks = Task.WhenAll(tasks);
            var writerCompleteTask = whenAllTasks.ContinueWith(_ => dependenciesChannel.Writer.Complete(), cancellationToken, TaskContinuationOptions.RunContinuationsAsynchronously, TaskScheduler.Default);
            await foreach (var value in dependenciesChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return value;
            }

            await Task.WhenAll(enumeratorTask, writerCompleteTask, whenAllTasks).ConfigureAwait(false);
        }

        public GlobCollection? FilePatterns { get; set; }

        public bool ShouldScanFile(ReadOnlySpan<char> fullPath)
        {
            return ShouldScanFileCore(new CandidateFileContext(Path.GetDirectoryName(fullPath), Path.GetFileName(fullPath)));
        }

        public bool ShouldScanFile(CandidateFileContext context)
        {
            if (FilePatterns != null)
                return FilePatterns.IsMatch(context.Directory, context.FileName);

            return ShouldScanFileCore(context);
        }

        protected abstract bool ShouldScanFileCore(CandidateFileContext context);

        public abstract ValueTask ScanAsync(ScanFileContext context);

        public static async Task UpdateAllAsync(IEnumerable<Dependency> dependencies, string newVersion, CancellationToken cancellationToken)
        {
            foreach (var dependency in dependencies.Where(d => d.Location.IsUpdatable))
            {
                await dependency.UpdateAsync(newVersion, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
