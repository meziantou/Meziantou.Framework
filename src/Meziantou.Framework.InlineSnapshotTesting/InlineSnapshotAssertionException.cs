namespace Meziantou.Framework.InlineSnapshotTesting;

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
