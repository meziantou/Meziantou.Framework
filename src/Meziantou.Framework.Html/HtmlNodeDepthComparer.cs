#nullable disable
using System;
using System.Collections.Generic;

namespace Meziantou.Framework.Html
{
    public sealed class HtmlNodeDepthComparer : IComparer<HtmlNode>
    {
        public ListSortDirection Direction { get; set; }

        public int Compare(HtmlNode x, HtmlNode y)
        {
            if (x == null)
                throw new ArgumentNullException(nameof(x));

            if (y == null)
                throw new ArgumentNullException(nameof(y));

            if (ReferenceEquals(x, y))
                return 0;

            var comp = x.Depth.CompareTo(y.Depth);
            return Direction == ListSortDirection.Ascending ? comp : -comp;
        }
    }
}
