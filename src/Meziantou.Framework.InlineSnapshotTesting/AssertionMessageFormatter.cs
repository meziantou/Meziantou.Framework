namespace Meziantou.Framework.InlineSnapshotTesting;

public abstract class AssertionMessageFormatter
{
    public abstract string FormatMessage(string? expected, string? actual);
}
