#nullable disable

namespace Meziantou.Framework.Html
{
    public sealed class HtmlNodeDepthComparer : IComparer<HtmlNode>
    {
        public ListSortDirection Direction { get; set; }

        public int Compare(HtmlNode x!!, HtmlNode y!!)
        {
            if (ReferenceEquals(x, y))
                return 0;

            var comp = x.Depth.CompareTo(y.Depth);
            return Direction == ListSortDirection.Ascending ? comp : -comp;
        }
    }
}
