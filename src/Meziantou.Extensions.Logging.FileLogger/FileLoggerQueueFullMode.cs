namespace Meziantou.Extensions.Logging;

/// <summary>Determines the behavior of the file logger when the message queue is full.</summary>
public enum FileLoggerQueueFullMode
{
    /// <summary>Blocks the thread that logs the message until the queue has room for it.</summary>
    Wait,

    /// <summary>Drops the new message when the queue is full.</summary>
    DropWrite,

    /// <summary>Drops the oldest queued message to make room for the new one.</summary>
    DropOldest,
}
