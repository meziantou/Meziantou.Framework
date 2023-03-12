using Meziantou.Framework.InlineSnapshotTesting.Utils;

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
            if (value == null)
                return null;

            value = StringUtils.ReplaceLineEndings(value, "\n");
            value = value.Replace("\t", "    ", StringComparison.Ordinal);
            return value;
        }

        public override bool AreEqual(string actual, string expected) => actual == expected;
    }
}
