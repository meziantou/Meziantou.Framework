namespace Meziantou.Framework.InlineSnapshotTesting;

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

