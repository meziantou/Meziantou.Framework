namespace Meziantou.Framework.Templating;

internal static class TemplateBlockComparer
{
    public static IComparer<TemplateBlock> IndexComparer { get; } = new TemplateBlockIndexComparer();

    private sealed class TemplateBlockIndexComparer : IComparer<TemplateBlock>
    {
        public int Compare(TemplateBlock? x, TemplateBlock? y)
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
