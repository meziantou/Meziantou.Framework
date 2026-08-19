namespace Meziantou.Extensions.Logging;

/// <summary>Specifies the algorithm used to compress the log files.</summary>
public enum LogFileCompression
{
    /// <summary>The log files are not compressed.</summary>
    None,

    /// <summary>The log files are compressed using gzip, and the <c>.gz</c> extension is added to their name.</summary>
    GZip,

    /// <summary>The log files are compressed using Brotli, and the <c>.br</c> extension is added to their name.</summary>
    Brotli,
}
