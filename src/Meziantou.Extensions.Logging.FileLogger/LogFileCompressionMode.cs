namespace Meziantou.Extensions.Logging;

/// <summary>Specifies when the log files are compressed.</summary>
public enum LogFileCompressionMode
{
    /// <summary>
    /// The messages are compressed as they are written, so the log file is never written uncompressed.
    /// The compressed stream is finalized when the file is rolled or when the provider is disposed, so the current log file may not be readable by all the tools while the application is running.
    /// </summary>
    Continuous,

    /// <summary>
    /// The log file is compressed once it is rolled, so the current log file is a plain text file.
    /// The compression happens on the thread that writes the messages, so rolling a big file delays the messages.
    /// </summary>
    OnRoll,
}
