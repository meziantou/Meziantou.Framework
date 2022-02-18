#nullable disable

namespace Meziantou.Framework.Html
{
    public static class HtmlNodeExtensions
    {
        public static IEnumerable<HtmlNode> Descendants(this HtmlNode node)
        {
            foreach (var child in node.ChildNodes)
            {
                yield return child;
                foreach (var n in child.Descendants())
                    yield return n;
            }
        }
    }
}
