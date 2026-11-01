namespace Meziantou.Framework.TemporaryContainers.Internals;

/// <summary>The position of a reader in the logs of a container, so that a reader that attaches again resumes where it stopped instead of reading the whole log again.</summary>
/// <remarks>
/// <para>
/// When the runtime records timestamps, the position is a timestamp: the reader attaches with <see cref="ContainerLogsOptions.Since"/>,
/// so the runtime does not send what was already read, and the log driver can rotate or truncate the earlier entries
/// without moving the position. The timestamps all come from the clock of the runtime, which is not always the clock of
/// the host (wslc reports the time of its virtual machine), so the position is never compared with a time measured
/// on the host. Since is inclusive, and several entries can share a timestamp, so the position also counts how many
/// entries written at that exact time were read.
/// </para>
/// <para>
/// A runtime that records no timestamps (Apple's <c>container</c>) replays its logs from the creation of the container on
/// every attach, so the position is the number of entries read.
/// </para>
/// </remarks>
internal sealed class LogCursor
{
    private DateTimeOffset? _timestamp;
    private int _entriesAtTimestamp;
    private int _entryCount;

    /// <summary>Initializes a cursor at the creation of the container.</summary>
    /// <param name="usesTimestamps">Whether the runtime records the time of every entry.</param>
    public LogCursor(bool usesTimestamps) => UsesTimestamps = usesTimestamps;

    private LogCursor(LogCursor other)
    {
        UsesTimestamps = other.UsesTimestamps;
        _timestamp = other._timestamp;
        _entriesAtTimestamp = other._entriesAtTimestamp;
        _entryCount = other._entryCount;
    }

    /// <summary>Gets whether the position is a timestamp rather than a number of entries.</summary>
    public bool UsesTimestamps { get; }

    /// <summary>Gets the time to attach from, or <see langword="null"/> to attach from the creation of the container.</summary>
    public DateTimeOffset? Since => _timestamp;

    /// <summary>Creates a cursor after every entry written up to, and including, a time.</summary>
    public static LogCursor After(DateTimeOffset timestamp) => new(usesTimestamps: true) { _timestamp = timestamp, _entriesAtTimestamp = int.MaxValue };

    /// <summary>Creates a cursor after the first entries of the log, for a runtime that records no timestamps.</summary>
    public static LogCursor AfterEntries(int count) => new(usesTimestamps: false) { _entryCount = count };

    public LogCursor Clone() => new(this);

    /// <summary>Starts reading a stream attached with <see cref="Since"/>: the stream decides which of its entries were already read and moves the cursor past the others.</summary>
    public Reader BeginRead() => new(this);

    internal sealed class Reader
    {
        private readonly LogCursor _cursor;
        private readonly DateTimeOffset? _startTimestamp;
        private readonly int _startEntriesAtTimestamp;
        private readonly int _startEntryCount;
        private int _seenAtStartTimestamp;
        private int _index;

        public Reader(LogCursor cursor)
        {
            _cursor = cursor;
            _startTimestamp = cursor._timestamp;
            _startEntriesAtTimestamp = cursor._entriesAtTimestamp;
            _startEntryCount = cursor._entryCount;
        }

        /// <summary>Determines whether an entry of the stream was not read yet, and if so moves the cursor past it.</summary>
        public bool TryAdvance(LogEntry entry)
        {
            if (!_cursor.UsesTimestamps)
            {
                if (_index++ < _startEntryCount)
                    return false;

                _cursor._entryCount = _index;
                return true;
            }

            // An entry the runtime could not time-stamp cannot be placed, so it is not skipped and does not move the
            // cursor.
            if (entry.Timestamp is not { } timestamp)
                return true;

            if (_startTimestamp is { } start)
            {
                if (timestamp < start)
                    return false;

                if (timestamp == start && _seenAtStartTimestamp++ < _startEntriesAtTimestamp)
                    return false;
            }

            // The CLIs write the two streams to two pipes, so an entry can arrive after a newer one of the other stream.
            // The cursor only moves forward.
            if (_cursor._timestamp is not { } current || timestamp > current)
            {
                _cursor._timestamp = timestamp;
                _cursor._entriesAtTimestamp = 1;
            }
            else if (timestamp == current)
            {
                _cursor._entriesAtTimestamp++;
            }

            return true;
        }
    }
}
