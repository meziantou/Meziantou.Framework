#nullable disable

namespace Meziantou.Framework.Html;

#if HTML_PUBLIC
public
#else
internal
#endif
static class HtmlNodeExtensions
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
