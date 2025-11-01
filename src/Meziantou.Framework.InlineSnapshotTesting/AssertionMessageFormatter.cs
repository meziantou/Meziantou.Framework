namespace Meziantou.Framework.InlineSnapshotTesting;

/// <summary>
/// Provides methods for formatting snapshot comparison error messages.
/// </summary>
public abstract class AssertionMessageFormatter
{
    /// <summary>Formats an error message showing the difference between expected and actual snapshots.</summary>
    /// <param name="expected">The expected snapshot value.</param>
    /// <param name="actual">The actual snapshot value.</param>
    /// <returns>A formatted error message suitable for display in test results.</returns>
    public abstract string FormatMessage(string? expected, string? actual);
}
