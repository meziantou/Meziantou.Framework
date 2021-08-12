using System.Collections.Generic;

namespace Meziantou.Framework.Templating;

internal static class ParsedBlockComparer
{
    public static IComparer<ParsedBlock> IndexComparer { get; } = new ParsedBlockIndexComparer();

    private sealed class ParsedBlockIndexComparer : IComparer<ParsedBlock>
    {
        public int Compare(ParsedBlock? x, ParsedBlock? y)
        {
            if (x is null && y is null)
                return 0;

            if (x is null)
                return -1;

            if (y is null)
                return 1;

            return x.Index.CompareTo(y.Index);
        }
    }
}
