namespace Meziantou.Framework.SnapshotTesting;

/// <summary>Represents errors that occur during snapshot operations.</summary>
public class SnapshotException : Exception
{
    public SnapshotException()
    {
    }

    public SnapshotException(string message)
        : base(message)
    {
    }

    public SnapshotException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

