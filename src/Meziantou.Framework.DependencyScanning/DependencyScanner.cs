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

namespace Meziantou.Framework.DependencyScanning
{
    public abstract class DependencyScanner
    {
        public static IAsyncEnumerable<Dependency> ScanDirectoryAsync(string path, ScannerOptions? options, CancellationToken cancellationToken = default)
        {
            options ??= ScannerOptions.Default;
            if (options.Scanners.Count == 0)
                return EmptyAsyncEnumerable<Dependency>.Instance;

            if (options.Scanners.Count <= EnabledScannersArray32.MaxValues)
                return ScanDirectoryAsync<EnabledScannersArray32>(path, options, cancellationToken);

            if (options.Scanners.Count <= EnabledScannersArray64.MaxValues)
                return ScanDirectoryAsync<EnabledScannersArray64>(path, options, cancellationToken);

            return ScanDirectoryAsync<EnabledScannersArray>(path, options, cancellationToken);
        }

        private static async IAsyncEnumerable<Dependency> ScanDirectoryAsync<T>(string path, ScannerOptions options, [EnumeratorCancellation] CancellationToken cancellationToken = default)
            where T : struct, IEnabledScannersArray
        {
            var fileSystem = options.FileSystem;
            if (Directory.Exists(path))
            {
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
                    using var enumerator = new ScannerFileEnumerator<T>(path, options);
                    while (enumerator.MoveNext())
                    {
                        await filesToScanChannel.Writer.WriteAsync(enumerator.Current, cancellationToken).ConfigureAwait(false);
                    }
                    filesToScanChannel.Writer.Complete();
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
                            var scanFileContext = new ScanFileContext(entry.FullPath, dependenciesChannel.Writer, fileSystem, cancellationToken);
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
                var writerCompleteTask = whenAllTasks.ContinueWith(_ => dependenciesChannel.Writer.Complete(), cancellationToken);
                await foreach (var value in dependenciesChannel.Reader.ReadAllAsync(cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    yield return value;
                }

                await writerCompleteTask.ConfigureAwait(false);
                await whenAllTasks.ConfigureAwait(false);
            }
            else
            {
                throw new DirectoryNotFoundException(path);
            }
        }

        public bool ShouldScanFile(ReadOnlySpan<char> fullPath)
        {
            return ShouldScanFile(new CandidateFileContext(Path.GetDirectoryName(fullPath), Path.GetFileName(fullPath)));
        }

        public abstract bool ShouldScanFile(CandidateFileContext context);

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
