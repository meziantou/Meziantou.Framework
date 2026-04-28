using AngleSharp.Dom;
using AngleSharp.Html;
using AngleSharp.Html.Parser;

namespace Meziantou.Framework.HtmlToMarkdownTests;

internal static class HtmlNormalizer
{
    public static void AssertEquivalent(string expectedHtml, string actualHtml)
    {
        var normalizedExpected = Normalize(expectedHtml);
        var normalizedActual = Normalize(actualHtml);
        Assert.Equal(normalizedExpected, normalizedActual);
    }

    public static string Normalize(string html)
    {
        var parser = new HtmlParser();
        var document = parser.ParseDocument("<body>" + html + "</body>");
        Assert.NotNull(document.Body);
        var body = document.Body;
        NormalizeNode(body);

        using var sw = new StringWriter();
        foreach (var child in body.ChildNodes)
        {
            child.ToHtml(sw, new PrettyMarkupFormatter());
        }

        return sw.ToString().Trim();
    }

    private static void NormalizeNode(INode node)
    {
        // Sort attributes alphabetically for deterministic comparison
        if (node is IElement element)
        {
            var attributes = element.Attributes.OrderBy(a => a.Name, StringComparer.Ordinal).ToList();
            foreach (var attr in element.Attributes.ToList())
            {
                element.RemoveAttribute(attr.NamespaceUri, attr.Name);
            }
            foreach (var attr in attributes)
            {
                element.SetAttribute(attr.Name, attr.Value);
            }
        }

        // Recursively normalize children
        foreach (var child in node.ChildNodes)
        {
            NormalizeNode(child);
        }
    }
}
