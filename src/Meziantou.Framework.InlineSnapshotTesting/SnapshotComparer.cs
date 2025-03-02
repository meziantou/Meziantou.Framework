using System.Text;
using Meziantou.Framework.HumanReadable.Utils;

namespace Meziantou.Framework.InlineSnapshotTesting;

public abstract class SnapshotComparer
{
    public static SnapshotComparer Default { get; } = new DefaultSnapshotComparer();

    public virtual string? NormalizeValue(string? value) => value;

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
