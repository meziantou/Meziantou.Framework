using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Meziantou.Extensions.Logging;

/// <summary>
/// A logger provider that writes all log messages to a file on disk.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private const int MaxQueuedMessages = 1024;

    private readonly string _logFilePath;
    private readonly StreamWriter? _writer;
    private readonly Channel<string>? _channel;
    private readonly Task? _writerTask;
    private bool _disposed;

    /// <summary>Gets the path to the log file.</summary>
    public string LogFilePath => _logFilePath;

    internal TimeProvider TimeProvider { get; }

    public FileLoggerProvider(string logsDirectory)
            : this(logsDirectory, TimeProvider.System)
    {
    }

    /// <summary>Creates a new FileLoggerProvider that writes to the specified directory.</summary>
    /// <param name="logsDirectory">The directory where log files will be written.</param>
    /// <param name="timeProvider">The time provider for timestamp generation.</param>
    public FileLoggerProvider(string logsDirectory, TimeProvider timeProvider)
    {
        TimeProvider = timeProvider;

        var pid = Environment.ProcessId;
        var timestamp = timeProvider.GetUtcNow().ToString("yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture);
        // Timestamp first so files sort chronologically by name
        _logFilePath = Path.Combine(logsDirectory, $"{timestamp}-{pid}.log");

        try
        {
            Directory.CreateDirectory(logsDirectory);
            _writer = new StreamWriter(_logFilePath, append: false, Encoding.UTF8)
            {
                AutoFlush = true
            };

            // Create bounded channel - blocks producers when full to provide backpressure
            _channel = Channel.CreateBounded<string>(new BoundedChannelOptions(MaxQueuedMessages)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });

            // Start background writer task
            _writerTask = Task.Run(ProcessLogQueueAsync);
        }
        catch (IOException ex)
        {
            // If we can't create the log file, warn on stderr and continue without file logging
            Console.Error.WriteLine($"Warning: Could not create log file at {_logFilePath}: {ex.Message}");
            _writer = null;
            _channel = null;
        }
    }

    private async Task ProcessLogQueueAsync()
    {
        if (_channel is null || _writer is null)
        {
            return;
        }

        try
        {
            await foreach (var message in _channel.Reader.ReadAllAsync())
            {
                await _writer.WriteLineAsync(message).ConfigureAwait(false);
            }
        }
        catch (ChannelClosedException)
        {
            // Expected when channel is completed during disposal
        }
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new FileLogger(this, categoryName);
    }

    internal void WriteLog(string message)
    {
        if (_channel is null)
        {
            return;
        }

        // Try to write to the channel - this will succeed as long as there's space
        // and the channel hasn't been completed yet
        if (_channel.Writer.TryWrite(message))
        {
            return;
        }

        // TryWrite failed - either channel is full (need backpressure) or completed (disposal)
        // Try async write which will wait for space or throw if completed
        try
        {
            // WaitToWriteAsync returns false if the channel is completed
            // This is cheaper than catching ChannelClosedException from WriteAsync
            if (!_channel.Writer.WaitToWriteAsync().AsTask().GetAwaiter().GetResult())
            {
                // Channel is completed - disposal is happening, message won't be written
                // This is the only case where we drop a message
                return;
            }

            // Space is available, write the message
            _channel.Writer.TryWrite(message);
        }
        catch (ChannelClosedException)
        {
            // Channel was completed between WaitToWriteAsync and TryWrite - rare race
        }
    }

    /// <summary>Flushes any pending writes to the log file.</summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (_writer is null || _disposed)
        {
            return;
        }

        await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Complete the channel to signal the writer task to finish
        // Any messages already in the channel will be drained by the writer task
        _channel?.Writer.TryComplete();

        // Wait for the writer task to finish processing ALL remaining messages
        _writerTask?.GetAwaiter().GetResult();

        _writer?.Dispose();
    }
}
