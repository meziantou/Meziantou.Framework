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

    // A message with a null text is a wake-up marker for a FlushAsync call. The requests themselves are
    // kept out of the channel, so they cannot be discarded by FileLoggerOptions.QueueFullMode
    private readonly Channel<LogMessage>? _channel;
    private readonly ConcurrentQueue<TaskCompletionSource> _pendingFlushes = new();
    private readonly Thread? _writerThread;
    private readonly TaskCompletionSource? _writerCompletion;

    private readonly MutableExternalScopeProvider _scopeProvider = new(new LoggerExternalScopeProvider());

    private FileLoggerOptions _options;
    private int _disposed;

    /// <summary>Gets the path of the file the messages are currently written to, or <see langword="null" /> when the log file could not be created.</summary>
    /// <remarks>The value changes when the log file is rolled.</remarks>
    public string? LogFilePath => _fileWriter?.CurrentFilePath;

    internal TimeProvider TimeProvider { get; }

    internal FileLoggerOptions CurrentOptions => Volatile.Read(ref _options);

    internal IExternalScopeProvider ScopeProvider => _scopeProvider;

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
        _channel = Channel.CreateBounded<LogMessage>(
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

        // The messages are written synchronously and the loop blocks while the queue is empty, so it
        // needs a thread of its own. TaskCreationOptions.LongRunning would not be enough: it only
        // covers the synchronous prefix of an async method, and every continuation after the first
        // await runs on the thread pool, where the loggers blocking in QueueFullMode.Wait can starve it
        _writerCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _writerThread = new Thread(ProcessLogQueue)
        {
            IsBackground = true,
            Name = "Meziantou.Extensions.Logging.FileLogger",
        };
        _writerThread.Start();
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

    /// <summary>Sets the external scope provider supplied by the logger factory.</summary>
    /// <param name="scopeProvider">The scope provider the factory uses to track scopes.</param>
    /// <remarks>
    /// Implementing <see cref="ISupportExternalScope"/> is what makes the factory route its scopes
    /// through this provider, including the ones it synthesises from the current activity when
    /// <c>LoggerFactoryOptions.ActivityTrackingOptions</c> is set.
    /// </remarks>
    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        _scopeProvider.Current = scopeProvider;
    }

    internal void WriteLog(string message, DateTimeOffset timestamp)
    {
        var channel = _channel;
        if (channel is null)
            return;

        var logMessage = new LogMessage(message, timestamp);

        // Try to write to the channel - this will succeed as long as there's space
        // and the channel hasn't been completed yet
        if (channel.Writer.TryWrite(logMessage))
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
                if (channel.Writer.TryWrite(logMessage))
                    return;
            }
        }
        catch (ChannelClosedException)
        {
            // Channel was completed between WaitToWriteAsync and TryWrite - rare race
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The application must keep running when the log file cannot be written")]
    private void ProcessLogQueue()
    {
        var channel = _channel!;
        var fileWriter = _fileWriter!;
        var lastFlush = TimeProvider.GetTimestamp();
        var pendingFlush = false;
        var writeFailed = false;

        try
        {
            // Blocking is the point: this is a dedicated thread, and it must not depend on the thread pool
            while (channel.Reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
            {
                // Take the requests registered so far: every message logged before them is already in the
                // queue drained below. A request registered while the queue is drained refers to a message
                // that may not be read yet, so it is left to the next iteration
                var flushCount = _pendingFlushes.Count;

                while (channel.Reader.TryRead(out var message))
                {
                    // A failure on a single message must not stop the writer. The log file can become
                    // writable again, and the loop still has to drain the queue and release FlushAsync
                    try
                    {
                        if (message.Message is null)
                        {
                            // Wake-up marker written by FlushAsync
                            Flush();
                        }
                        else
                        {
                            fileWriter.WriteLine(message.Message, message.Timestamp);
                            pendingFlush = true;

                            // Ensure the messages reach the disk even when the queue is never empty
                            if (TimeProvider.GetElapsedTime(lastFlush) >= CurrentOptions.FlushInterval)
                            {
                                Flush();
                            }
                        }

                        writeFailed = false;
                    }
                    catch (Exception ex)
                    {
                        ReportWriteFailure(ex);
                    }
                }

                // The queue is empty, make the messages visible to the other processes
                try
                {
                    if (pendingFlush)
                    {
                        Flush();
                    }
                }
                catch (Exception ex)
                {
                    ReportWriteFailure(ex);
                }

                // The queue is drained and flushed, so every message queued before these requests is on the disk
                for (var i = 0; i < flushCount; i++)
                {
                    if (!_pendingFlushes.TryDequeue(out var completion))
                        break;

                    completion.TrySetResult();
                }

                // The marker of a request registered while the queue was drained may have been discarded
                // by FileLoggerOptions.QueueFullMode, so make sure the writer wakes up again for it
                if (!_pendingFlushes.IsEmpty)
                {
                    channel.Writer.TryWrite(default);
                }
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
            _writerCompletion!.TrySetResult();
        }

        void Flush()
        {
            fileWriter.Flush();
            pendingFlush = false;
            lastFlush = TimeProvider.GetTimestamp();
        }

        void ReportWriteFailure(Exception exception)
        {
            // The data of the failed write is lost, do not try to flush it again
            pendingFlush = false;

            // Report the first failure and the failures that follow a successful write, so a log file
            // that stays unavailable does not flood the standard error stream
            if (!writeFailed)
            {
                writeFailed = true;
                Console.Error.WriteLine($"Warning: Could not write to the log file '{fileWriter.CurrentFilePath}': {exception.Message}");
            }
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
        if (!channel.Writer.TryWrite(default))
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

                channel.Writer.TryWrite(default);
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

        // Wait for the writer thread to finish processing ALL remaining messages
        _writerThread?.Join();

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

        if (_writerCompletion is not null)
        {
            // Wait for the writer thread to finish processing ALL remaining messages, without blocking
            await _writerCompletion.Task.ConfigureAwait(false);
        }

        _fileWriter?.Dispose();
    }

    /// <summary>A message to write to the log file, or a wake-up marker for a FlushAsync call when <see cref="Message"/> is null.</summary>
    private readonly struct LogMessage(string? message, DateTimeOffset timestamp)
    {
        public string? Message { get; } = message;

        /// <summary>Gets the UTC time at which the message was logged, used to roll the log file.</summary>
        public DateTimeOffset Timestamp { get; } = timestamp;
    }
}
