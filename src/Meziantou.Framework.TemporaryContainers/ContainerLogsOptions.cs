namespace Meziantou.Framework.TemporaryContainers;

/// <summary>Selects the log entries returned by <see cref="TemporaryContainer.GetLogsAsync(ContainerLogsOptions, CancellationToken)"/>.</summary>
public sealed class ContainerLogsOptions
{
    /// <summary>Initializes a new instance of the <see cref="ContainerLogsOptions"/> class, which returns every entry and follows the new ones.</summary>
    public ContainerLogsOptions()
    {
    }

    internal ContainerLogsOptions(ContainerLogsOptions other)
    {
        Since = other.Since;
        Until = other.Until;
        Tail = other.Tail;
        Follow = other.Follow;
        IncludeStandardOutput = other.IncludeStandardOutput;
        IncludeStandardError = other.IncludeStandardError;
    }

    /// <summary>Gets or sets the time of the oldest entry to return. Entries written at exactly this time are returned.</summary>
    /// <remarks>The time is compared with the timestamps the runtime records, using the clock of the runtime. Not supported by the Apple <c>container</c> runtime, which does not record timestamps.</remarks>
    public DateTimeOffset? Since { get; set; }

    /// <summary>Gets or sets the time of the newest entry to return. Entries written at exactly this time are returned.</summary>
    /// <remarks>The time is compared with the timestamps the runtime records, using the clock of the runtime. Not supported by the Apple <c>container</c> runtime, which does not record timestamps.</remarks>
    public DateTimeOffset? Until { get; set; }

    /// <summary>Gets or sets the number of entries to return from the end of the logs, or <see langword="null"/> to return all of them.</summary>
    /// <remarks>When <see cref="Since"/> or <see cref="Until"/> is set too, the runtime takes the last entries first, then drops the ones outside the time range.</remarks>
    public int? Tail
    {
        get;
        set
        {
            if (value is < 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "The number of entries cannot be negative.");

            field = value;
        }
    }

    /// <summary>Gets or sets a value indicating whether the stream keeps returning new entries until the container stops or the enumeration is cancelled. The default value is <see langword="true"/>.</summary>
    public bool Follow { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the entries written to the standard output are returned. The default value is <see langword="true"/>.</summary>
    public bool IncludeStandardOutput { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the entries written to the standard error are returned. The default value is <see langword="true"/>.</summary>
    public bool IncludeStandardError { get; set; } = true;

    internal bool Includes(LogStream stream) => stream is LogStream.Stderr ? IncludeStandardError : IncludeStandardOutput;
}
