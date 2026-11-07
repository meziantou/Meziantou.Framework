using System.Globalization;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Formatting;

namespace Meziantou.Framework.WebPlatform.Html;

/// <summary>
/// The HTML fragment serialization algorithm (https://html.spec.whatwg.org/multipage/parsing.html#serialising-html-fragments),
/// implemented by <see cref="HtmlMarkupWriter"/>.
/// </summary>
internal static class HtmlSerializer
{
    /// <summary>Serializes the children of <paramref name="node"/>.</summary>
    public static string SerializeChildren(Node node, bool serializableShadowRoots = false, IReadOnlyCollection<ShadowRoot>? shadowRoots = null)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        SerializeChildren(node, writer, serializableShadowRoots, shadowRoots);
        return writer.ToString();
    }

    /// <summary>Serializes the node itself, as for the outerHTML getter.</summary>
    public static string SerializeNode(Node node) => HtmlFormatter.Default.Format(node);

    public static void SerializeChildren(Node node, TextWriter output, bool serializableShadowRoots, IReadOnlyCollection<ShadowRoot>? shadowRoots)
    {
        var writer = new HtmlMarkupWriter(output) { SerializableShadowRoots = serializableShadowRoots, ShadowRoots = shadowRoots };
        writer.WriteChildren(node);
    }
}
