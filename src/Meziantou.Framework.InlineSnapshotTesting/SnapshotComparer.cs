using Meziantou.Framework.HumanReadable.Utils;

namespace Meziantou.Framework.InlineSnapshotTesting;

/// <summary>Provides methods for comparing and normalizing snapshot values.</summary>
public abstract class SnapshotComparer
{
    /// <summary>Gets the default snapshot comparer that normalizes whitespace and compares line by line.</summary>
    public static SnapshotComparer Default { get; } = new DefaultSnapshotComparer();

    /// <summary>Normalizes a snapshot value before comparison. The default implementation returns the value unchanged.</summary>
    /// <param name="value">The value to normalize.</param>
    /// <returns>The normalized value.</returns>
    public virtual string? NormalizeValue(string? value) => value;

    /// <summary>Determines whether two snapshot values are equal.</summary>
    /// <param name="actual">The actual snapshot value.</param>
    /// <param name="expected">The expected snapshot value.</param>
    /// <returns>true if the values are equal; otherwise, false.</returns>
    public abstract bool AreEqual(string? actual, string? expected);

    private sealed class DefaultSnapshotComparer : SnapshotComparer
    {
        public override string? NormalizeValue(string? value)
        {
            if (value is null)
                return null;

            var sb = new StringBuilder(value.Length);
            foreach (var (line, eol) in StringUtils.EnumerateLines(value))
            {
                if (!line.IsWhiteSpace())
                {
                    if (!line.Contains('\t'))
                    {
                        sb.Append(line);
                    }
                    else
                    {
                        foreach (var c in line)
                        {
                            if (c == '\t')
                            {
                                sb.Append("    ");
                            }
                            else
                            {
                                sb.Append(c);
                            }
                        }
                    }
                }

                if (!eol.IsEmpty)
                {
                    sb.Append('\n');
                }
            }

            return sb.ToString();
        }

        public override bool AreEqual(string? actual, string? expected) => actual == expected;
    }
}
