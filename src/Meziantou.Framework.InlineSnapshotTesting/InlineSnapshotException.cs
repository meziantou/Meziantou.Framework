namespace Meziantou.Framework.InlineSnapshotTesting;

/// <summary>Represents errors that occur during inline snapshot operations.</summary>
public class InlineSnapshotException : Exception
{
    public InlineSnapshotException()
    {
    }

    public InlineSnapshotException(string message)
        : base(message)
    {
    }

    public InlineSnapshotException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

