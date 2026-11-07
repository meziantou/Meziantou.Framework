using Meziantou.Framework.WebPlatform;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Formatting;

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
        var document = HtmlDocumentParser.ParseDocument("<body>" + html + "</body>");
        Assert.NotNull(document.Body);
        var body = document.Body;
        NormalizeNode(body);

        var formatter = new HtmlPrettyFormatter();
        using var sw = new StringWriter();
        foreach (var child in body.ChildNodes)
        {
            child.ToHtml(sw, formatter);
        }

        return sw.ToString().Trim();
    }

    private static void NormalizeNode(Node node)
    {
        // Sort attributes alphabetically for deterministic comparison
        if (node is Element element)
        {
            var attributes = element.Attributes.OrderBy(a => a.Name, StringComparer.Ordinal).Select(a => (a.Name, a.Value)).ToList();
            foreach (var attr in element.Attributes.ToList())
            {
                element.RemoveAttributeNode(attr);
            }
            foreach (var (name, value) in attributes)
            {
                element.SetAttribute(name, value);
            }
        }

        // Recursively normalize children
        foreach (var child in node.ChildNodes)
        {
            NormalizeNode(child);
        }
    }
}
