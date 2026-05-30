namespace Meziantou.Framework.Templating;

internal static class TextBlockComparer
{
    public static IComparer<TextBlock> IndexComparer { get; } = new TextBlockIndexComparer();

    private sealed class TextBlockIndexComparer : IComparer<TextBlock>
    {
        public int Compare(TextBlock? x, TextBlock? y)
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
