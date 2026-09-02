using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meziantou.Extensions.Logging;

/// <summary>
/// A logger provider that writes all log messages to a file on disk.
/// </summary>
/// <example>
/// <code>
/// using var provider = new FileLoggerProvider(new FileLoggerOptions
/// {
///     Directory = "logs",
///     RollInterval = RollInterval.Daily,
///     MaxRetainedFiles = 7,
/// });
///
/// using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(provider));
/// </code>
/// </example>
[ProviderAlias("File")]
public sealed class FileLoggerProvider : ILoggerProvider, ISupportExternalScope, IAsyncDisposable
{
    private readonly IDisposable? _optionsReloadToken;
    private readonly LogFileWriter? _fileWriter;

    // A null message is a wake-up marker for a FlushAsync call. The requests themselves are kept out
    // of the channel, so they cannot be discarded by FileLoggerOptions.QueueFullMode
    private readonly Channel<string?>? _channel;
    private readonly ConcurrentQueue<TaskCompletionSource> _pendingFlushes = new();
    private readonly Task? _writerTask;

    private FileLoggerOptions _options;
    private IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();
    private int _disposed;

    /// <summary>Gets the path of the file the messages are currently written to, or <see langword="null" /> when the log file could not be created.</summary>
    /// <remarks>The value changes when the log file is rolled.</remarks>
    public string? LogFilePath => _fileWriter?.CurrentFilePath;

    internal TimeProvider TimeProvider { get; }

    internal FileLoggerOptions CurrentOptions => Volatile.Read(ref _options);

    internal IExternalScopeProvider ScopeProvider => Volatile.Read(ref _scopeProvider);

    /// <summary>Initializes a new instance of the <see cref="FileLoggerProvider"/> class that writes to the specified directory.</summary>
    /// <param name="logsDirectory">The directory where log files will be written.</param>
    public FileLoggerProvider(string logsDirectory)
        : this(logsDirectory, TimeProvider.System)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="FileLoggerProvider"/> class that writes to the specified directory.</summary>
    /// <param name="logsDirectory">The directory where log files will be written.</param>
    /// <param name="timeProvider">The time provider used to generate the timestamps.</param>
    public FileLoggerProvider(string logsDirectory, TimeProvider timeProvider)
        : this(new FileLoggerOptions { Directory = logsDirectory }, timeProvider)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="FileLoggerProvider"/> class with the specified options.</summary>
    /// <param name="options">The options of the provider.</param>
    public FileLoggerProvider(FileLoggerOptions options)
        : this(options, TimeProvider.System)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="FileLoggerProvider"/> class with the specified options.</summary>
    /// <param name="options">The options of the provider.</param>
    /// <param name="timeProvider">The time provider used to generate the timestamps.</param>
    public FileLoggerProvider(FileLoggerOptions options, TimeProvider timeProvider)
        : this(options, optionsMonitor: null, timeProvider)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="FileLoggerProvider"/> class with the specified options.</summary>
    /// <param name="options">The options of the provider.</param>
    public FileLoggerProvider(IOptionsMonitor<FileLoggerOptions> options)
        : this(options, TimeProvider.System)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="FileLoggerProvider"/> class with the specified options.</summary>
    /// <param name="options">The options of the provider.</param>
    /// <param name="timeProvider">The time provider used to generate the timestamps.</param>
    public FileLoggerProvider(IOptionsMonitor<FileLoggerOptions> options, TimeProvider timeProvider)
        : this(GetCurrentValue(options), options, timeProvider)
    {
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The application must keep running when the log file cannot be created")]
    private FileLoggerProvider(FileLoggerOptions options, IOptionsMonitor<FileLoggerOptions>? optionsMonitor, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (string.IsNullOrEmpty(options.Directory))
            throw new InvalidOperationException($"The '{nameof(FileLoggerOptions)}.{nameof(FileLoggerOptions.Directory)}' option must be set");

        _options = options;
        TimeProvider = timeProvider;
        _optionsReloadToken = optionsMonitor?.OnChange(UpdateOptions);

        try
        {
            _fileWriter = new LogFileWriter(options.Directory, options, timeProvider);
        }
        catch (Exception ex)
        {
            // If we can't create the log file, warn on stderr and continue without file logging
            Console.Error.WriteLine($"Warning: Could not create a log file in '{options.Directory}': {ex.Message}");
            return;
        }

        // Bounded channel, the behavior when it is full is defined by FileLoggerOptions.QueueFullMode
        _channel = Channel.CreateBounded<string?>(
            new BoundedChannelOptions(options.MaxQueueLength)
            {
                FullMode = options.QueueFullMode switch
                {
                    FileLoggerQueueFullMode.DropWrite => BoundedChannelFullMode.DropWrite,
                    FileLoggerQueueFullMode.DropOldest => BoundedChannelFullMode.DropOldest,
                    _ => BoundedChannelFullMode.Wait,
                },
                SingleReader = true,
                SingleWriter = false,
            });

        // The messages are written synchronously, so use a dedicated thread instead of a thread pool thread
        _writerTask = Task.Factory.StartNew(ProcessLogQueueAsync, CancellationToken.None, TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach, TaskScheduler.Default).Unwrap();
    }

    private static FileLoggerOptions GetCurrentValue(IOptionsMonitor<FileLoggerOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.CurrentValue;
    }

    private void UpdateOptions(FileLoggerOptions options)
    {
        // The options related to the file itself (directory, file name, rolling) are only read when the provider is created
        Volatile.Write(ref _options, options);
    }

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName)
    {
        return new FileLogger(this, categoryName);
    }

    /// <inheritdoc/>
    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        Volatile.Write(ref _scopeProvider, scopeProvider ?? new LoggerExternalScopeProvider());
    }

    internal void WriteLog(string message)
    {
        var channel = _channel;
        if (channel is null)
            return;

        // Try to write to the channel - this will succeed as long as there's space
        // and the channel hasn't been completed yet
        if (channel.Writer.TryWrite(message))
            return;

        // TryWrite failed - the channel is full (need backpressure) or completed (disposal)
        try
        {
            // Another thread can take the room freed between WaitToWriteAsync and TryWrite, so keep
            // trying until the message is queued. Otherwise the message would be dropped even though
            // the caller asked to wait for room.
            // WaitToWriteAsync returns false if the channel is completed
            // This is cheaper than catching ChannelClosedException from WriteAsync
            while (channel.Writer.WaitToWriteAsync().AsTask().GetAwaiter().GetResult())
            {
                if (channel.Writer.TryWrite(message))
                    return;
            }
        }
        catch (ChannelClosedException)
        {
            // Channel was completed between WaitToWriteAsync and TryWrite - rare race
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The application must keep running when the log file cannot be written")]
    private async Task ProcessLogQueueAsync()
    {
        var channel = _channel!;
        var fileWriter = _fileWriter!;
        var lastFlush = TimeProvider.GetTimestamp();
        var pendingFlush = false;

        try
        {
            while (await channel.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                while (channel.Reader.TryRead(out var message))
                {
                    if (message is null)
                    {
                        // Wake-up marker written by FlushAsync
                        Flush();
                    }
                    else
                    {
                        fileWriter.WriteLine(message);
                        pendingFlush = true;

                        // Ensure the messages reach the disk even when the queue is never empty
                        if (TimeProvider.GetElapsedTime(lastFlush) >= CurrentOptions.FlushInterval)
                        {
                            Flush();
                        }
                    }
                }

                // The queue is empty, make the messages visible to the other processes
                if (pendingFlush)
                {
                    Flush();
                }

                // The queue is drained and flushed, so every message queued before these requests is
                // on the disk. Requests registered from now on are handled by the next iteration
                CompletePendingFlushes();
            }
        }
        catch (Exception ex)
        {
            // Complete the channel, so the loggers stop waiting for room in a queue that is never drained again
            channel.Writer.TryComplete();
            Console.Error.WriteLine($"Warning: Stopped writing to the log file '{fileWriter.CurrentFilePath}': {ex.Message}");
        }
        finally
        {
            // Release the pending FlushAsync calls, the queue is not drained anymore
            CompletePendingFlushes();
        }

        void Flush()
        {
            fileWriter.Flush();
            pendingFlush = false;
            lastFlush = TimeProvider.GetTimestamp();
        }
    }

    /// <summary>Waits for the pending messages to be written to the log file.</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        var channel = _channel;
        if (channel is null)
            return;

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingFlushes.Enqueue(completion);

        // Wake the writer up. The marker itself carries nothing, so it does not matter if
        // QueueFullMode discards it: a full queue means the writer is already busy and it completes
        // the pending requests as soon as it drains the queue
        if (!channel.Writer.TryWrite(null))
        {
            try
            {
                // WaitToWriteAsync returns false once the channel is completed. The writer task is
                // stopping, and it may already have released the pending requests, so do it here
                if (!await channel.Writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false))
                {
                    CompletePendingFlushes();
                    return;
                }

                channel.Writer.TryWrite(null);
            }
            catch (ChannelClosedException)
            {
                CompletePendingFlushes();
                return;
            }
        }

        await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private void CompletePendingFlushes()
    {
        while (_pendingFlushes.TryDequeue(out var completion))
        {
            completion.TrySetResult();
        }
    }

    /// <inheritdoc/>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Disposing the provider must not throw because of a log message")]
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is 1)
            return;

        _optionsReloadToken?.Dispose();

        // Complete the channel to signal the writer task to finish.
        // Any message already in the channel will be drained by the writer task
        _channel?.Writer.TryComplete();

        try
        {
            // Wait for the writer task to finish processing ALL remaining messages
            _writerTask?.GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // The error is already reported by the writer task
        }

        _fileWriter?.Dispose();
    }

    /// <inheritdoc/>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Disposing the provider must not throw because of a log message")]
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) is 1)
            return;

        _optionsReloadToken?.Dispose();
        _channel?.Writer.TryComplete();

        if (_writerTask is not null)
        {
            try
            {
                await _writerTask.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The error is already reported by the writer task
            }
        }

        _fileWriter?.Dispose();
    }
}
