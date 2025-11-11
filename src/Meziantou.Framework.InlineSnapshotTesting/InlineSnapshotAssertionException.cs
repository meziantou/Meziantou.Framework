namespace Meziantou.Framework.InlineSnapshotTesting;

/// <summary>Represents assertion errors that occur when a snapshot doesn't match the expected value.</summary>
public sealed class InlineSnapshotAssertionException : InlineSnapshotException
{
    public InlineSnapshotAssertionException()
    {
    }

    public InlineSnapshotAssertionException(string message)
        : base(message)
    {
    }

    public InlineSnapshotAssertionException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
