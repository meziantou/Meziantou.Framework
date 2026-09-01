using System.IO.Compression;
using Microsoft.Extensions.Logging;

namespace Meziantou.Extensions.Logging;

/// <summary>Options for the <see cref="FileLoggerProvider"/>.</summary>
public sealed class FileLoggerOptions
{
    private int _maxQueueLength = 1024;
    private long? _maxFileSizeInBytes;
    private int? _maxRetainedFiles;
    private TimeSpan _flushInterval = TimeSpan.FromSeconds(1);
    private string _fileNameExtension = ".log";
    private string _fileNamePrefix = "";
    private string _formatterName = FileFormatterNames.Simple;

    /// <summary>Gets or sets the directory where log files are written. This value is required.</summary>
    public string? Directory { get; set; }

    /// <summary>Gets or sets the prefix added at the beginning of the log file names. Defaults to an empty string.</summary>
    public string FileNamePrefix
    {
        get => _fileNamePrefix;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _fileNamePrefix = value;
        }
    }

    /// <summary>Gets or sets the extension of the log file names, including the leading dot. Defaults to <c>.log</c>.</summary>
    public string FileNameExtension
    {
        get => _fileNameExtension;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _fileNameExtension = value;
        }
    }

    /// <summary>Gets or sets a value indicating whether the identifier of the current process is part of the log file names. Defaults to <see langword="true" />.</summary>
    public bool IncludeProcessIdInFileName { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether an existing log file is reused instead of creating a new one. Defaults to <see langword="false" />.
    /// When a matching file is already in use by another process, a new file is created.
    /// </summary>
    /// <remarks>
    /// A file is reused only when its name matches the name the provider computes when it starts, so this option has no effect unless the name is stable:
    /// <list type="bullet">
    /// <item><description><see cref="RollInterval" /> must be set. <see cref="RollInterval.None" /> includes the seconds in the name, so the name changes on every start.</description></item>
    /// <item><description><see cref="IncludeProcessIdInFileName" /> must be <see langword="false" /> to reuse a file across restarts, as the identifier of the process changes.</description></item>
    /// </list>
    /// With the default values of these two options, a new file is created on every start even when this option is <see langword="true" />.
    /// </remarks>
    public bool Append { get; set; }

    /// <summary>Gets or sets how often a new log file is created based on time. Defaults to <see cref="RollInterval.None" />.</summary>
    public RollInterval RollInterval { get; set; }

    /// <summary>Gets or sets the maximum size of a log file, in bytes, after which a new file is created. Defaults to <see langword="null" /> (no limit).</summary>
    public long? MaxFileSizeInBytes
    {
        get => _maxFileSizeInBytes;
        set
        {
            if (value is not null)
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(value.GetValueOrDefault(), 1, nameof(value));
            }

            _maxFileSizeInBytes = value;
        }
    }

    /// <summary>Gets or sets the maximum number of log files to keep in the directory. Older files are deleted when a new file is created. Defaults to <see langword="null" /> (no limit).</summary>
    public int? MaxRetainedFiles
    {
        get => _maxRetainedFiles;
        set
        {
            if (value is not null)
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(value.GetValueOrDefault(), 1, nameof(value));
            }

            _maxRetainedFiles = value;
        }
    }

    /// <summary>Gets or sets the Unix file mode applied to the log files when they are created. Defaults to <see langword="null" />, which uses the default mode of the platform.</summary>
    /// <remarks>
    /// The default mode of most Unix systems makes the log files readable by every local user. Set this option to <see cref="System.IO.UnixFileMode.UserRead" /> | <see cref="System.IO.UnixFileMode.UserWrite" /> when the messages can contain sensitive data.
    /// The value is ignored on Windows, and the mode is still filtered by the umask of the process. The mode of the directory is not changed.
    /// </remarks>
    public UnixFileMode? UnixCreateMode { get; set; }

    /// <summary>Gets or sets the algorithm used to compress the log files. Defaults to <see cref="LogFileCompression.None" />.</summary>
    public LogFileCompression Compression { get; set; }

    /// <summary>Gets or sets when the log files are compressed. Defaults to <see cref="LogFileCompressionMode.Continuous" />. This value is ignored when <see cref="Compression" /> is <see cref="LogFileCompression.None" />.</summary>
    /// <remarks><see cref="Append" /> is ignored when the messages are compressed continuously, as appending to a compressed file would produce a file that most tools cannot read.</remarks>
    public LogFileCompressionMode CompressionMode { get; set; }

    /// <summary>Gets or sets the compression level. Defaults to <see cref="System.IO.Compression.CompressionLevel.Optimal" />. This value is ignored when <see cref="Compression" /> is <see cref="LogFileCompression.None" />.</summary>
    public CompressionLevel CompressionLevel { get; set; } = CompressionLevel.Optimal;

    /// <summary>Gets or sets the minimum level of the messages written to the file. Defaults to <see cref="LogLevel.Trace" />.</summary>
    public LogLevel MinLevel { get; set; } = LogLevel.Trace;

    /// <summary>Gets or sets a value indicating whether scopes are included in the log messages. Defaults to <see langword="false" />.</summary>
    public bool IncludeScopes { get; set; }

    /// <summary>Gets or sets a value indicating whether the category is included in the log messages. Defaults to <see langword="true" />.</summary>
    public bool IncludeCategory { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the log level is included in the log messages. Defaults to <see langword="true" />.</summary>
    public bool IncludeLogLevel { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the event id is included in the log messages. Defaults to <see langword="false" />.</summary>
    public bool IncludeEventId { get; set; }

    /// <summary>Gets or sets a value indicating whether the identifier of the thread that logged the message is included in the log messages. Defaults to <see langword="false" />.</summary>
    public bool IncludeThreadId { get; set; }

    /// <summary>Gets or sets a value indicating whether the trace id and the span id of the current <see cref="System.Diagnostics.Activity" /> are included in the log messages. Defaults to <see langword="false" />.</summary>
    public bool IncludeActivityTracking { get; set; }

    /// <summary>Gets or sets a value indicating whether only the last segment of the category is written, for instance <c>Program</c> instead of <c>Sample.Program</c>. Defaults to <see langword="false" />.</summary>
    public bool UseShortCategoryName { get; set; }

    /// <summary>Gets or sets the format string used to format the timestamp of the log messages. Set it to <see langword="null" /> to omit the timestamp.</summary>
    [StringSyntax(StringSyntaxAttribute.DateTimeFormat)]
    public string? TimestampFormat { get; set; } = "yyyy-MM-dd HH:mm:ss.fff";

    /// <summary>Gets or sets a value indicating whether the UTC timezone is used to format the timestamps. Defaults to <see langword="true" />.</summary>
    public bool UseUtcTimestamp { get; set; } = true;

    /// <summary>Gets or sets the maximum number of messages that can be queued before <see cref="QueueFullMode" /> applies. Defaults to <c>1024</c>.</summary>
    public int MaxQueueLength
    {
        get => _maxQueueLength;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            _maxQueueLength = value;
        }
    }

    /// <summary>Gets or sets the behavior when the message queue is full. Defaults to <see cref="FileLoggerQueueFullMode.Wait" />.</summary>
    public FileLoggerQueueFullMode QueueFullMode { get; set; }

    /// <summary>Gets or sets the maximum duration between two flushes when messages are logged continuously. Defaults to 1 second.</summary>
    /// <remarks>The pending messages are always flushed as soon as the queue is empty, so this value only matters when the logger cannot keep up with the messages.</remarks>
    public TimeSpan FlushInterval
    {
        get => _flushInterval;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            _flushInterval = value;
        }
    }

    /// <summary>Gets or sets the name of the formatter to use. Supported values are <see cref="FileFormatterNames.Simple" /> and <see cref="FileFormatterNames.Json" />. This value is ignored when <see cref="Formatter" /> is set.</summary>
    public string FormatterName
    {
        get => _formatterName;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _formatterName = value;
        }
    }

    /// <summary>Gets or sets the formatter used to write the log entries. When <see langword="null" />, the formatter is resolved from <see cref="FormatterName" />.</summary>
    public FileFormatter? Formatter { get; set; }

    internal FileFormatter GetFormatter()
    {
        if (Formatter is not null)
            return Formatter;

        return FormatterName.Equals(FileFormatterNames.Json, StringComparison.OrdinalIgnoreCase)
            ? JsonFileFormatter.Instance
            : SimpleFileFormatter.Instance;
    }
}
