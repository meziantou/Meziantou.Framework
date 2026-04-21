namespace Meziantou.Framework.SnapshotTesting;

/// <summary>Represents assertion errors that occur when a snapshot does not match.</summary>
public sealed class SnapshotAssertionException : SnapshotException
{
    public SnapshotAssertionException()
    {
    }

    public SnapshotAssertionException(string message)
        : base(message)
    {
    }

    public SnapshotAssertionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

