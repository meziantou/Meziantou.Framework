using System.Buffers;
using System.Text.RegularExpressions;
using WebElement = Meziantou.Framework.WebPlatform.Dom.Element;
using WebHtmlDocumentParser = Meziantou.Framework.WebPlatform.HtmlDocumentParser;
using WebHtmlFormatter = Meziantou.Framework.WebPlatform.Formatting.HtmlFormatter;
using WebHtmlMarkupWriter = Meziantou.Framework.WebPlatform.Formatting.HtmlMarkupWriter;
using WebHtmlTemplateElement = Meziantou.Framework.WebPlatform.Dom.HtmlTemplateElement;
using WebNode = Meziantou.Framework.WebPlatform.Dom.Node;

namespace Meziantou.Framework.HumanReadable.ValueFormatters;

internal sealed partial class HtmlFormatter : ValueFormatter
{
    private readonly HtmlFormatterOptions _options;

    public HtmlFormatter(HtmlFormatterOptions options)
    {
        _options = options;
    }

    public override void Format(HumanReadableTextWriter writer, string? value, HumanReadableSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        try
        {
            var root = Parse(value);

            if (_options.RedactContentSecurityPolicyNonce)
            {
                foreach (var element in EnumerateElements(root))
                {
                    if (element.HasAttribute("nonce"))
                    {
                        element.SetAttribute("nonce", "[redacted]");
                    }
                }
            }

            if (_options.OrderAttributes)
            {
                foreach (var element in EnumerateElements(root))
                {
                    OrderAttributes(element);
                }
            }

            var formatter = _options.AttributeQuote switch
            {
                HtmlAttributeQuote.SimpleQuote => SimpleQuoteHtmlFormatter.Instance,
                _ => WebHtmlFormatter.Default,
            };

            writer.WriteValue(formatter.Format(root));
        }
        catch (Exception ex) when (ex is not HumanReadableSerializerException)
        {
            // The value could not be parsed or reformatted: fall back to the raw text.
            // HumanReadableSerializerException is this library's own signal (for example
            // MaxDepth) and must not be turned into a silent formatting fallback.
            writer.WriteValue(value);
        }
    }

    // A value with a doctype or one of the html, head and body tags is a document; without them, parsing it as a
    // document would wrap it in <html><head></head><body>…</body></html>.
    private static WebNode Parse(string value)
    {
        if (DocumentTagRegex().IsMatch(value))
            return WebHtmlDocumentParser.ParseDocument(value);

        var context = WebHtmlDocumentParser.ParseDocument("").Body!;
        return new WebHtmlDocumentParser().ParseFragment(value, context);
    }

    private static IEnumerable<WebElement> EnumerateElements(WebNode root)
    {
        var stack = new Stack<WebNode>();
        stack.Push(root);
        while (stack.TryPop(out var node))
        {
            if (node is WebElement element)
            {
                yield return element;
            }

            if (node is WebHtmlTemplateElement template)
            {
                stack.Push(template.Content);
            }

            for (var child = node.FirstChild; child is not null; child = child.NextSibling)
            {
                stack.Push(child);
            }
        }
    }

    private static void OrderAttributes(WebElement element)
    {
        var attributes = element.Attributes;
        if (attributes.Count < 2)
            return;

        var orderedAttributes = attributes
            .OrderBy(attribute => attribute.NamespaceUri, StringComparer.Ordinal)
            .ThenBy(attribute => attribute.Name, StringComparer.Ordinal)
            .ToArray();

        // Setting an attribute that is not on the element appends it, so removing and re-adding them in order sorts them
        foreach (var attribute in orderedAttributes)
        {
            attributes.RemoveNamedItemNS(attribute.NamespaceUri, attribute.LocalName);
            attributes.SetNamedItemNS(attribute);
        }
    }

    [GeneratedRegex("<(?:!doctype|html|head|body)[\t\n\f\r />]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DocumentTagRegex();

    private sealed class SimpleQuoteHtmlFormatter : WebHtmlFormatter
    {
        public static SimpleQuoteHtmlFormatter Instance { get; } = new();

        protected override WebHtmlMarkupWriter CreateWriter(TextWriter output) => new SimpleQuoteHtmlMarkupWriter(output);
    }

    private sealed class SimpleQuoteHtmlMarkupWriter(TextWriter output) : WebHtmlMarkupWriter(output)
    {
        private static readonly SearchValues<char> AttributeEscapeChars = SearchValues.Create("&<>' ");

        protected override void WriteAttribute(WebElement element, string serializedName, string value)
        {
            Output.Write(' ');
            Output.Write(serializedName);
            Output.Write("='");

            var remaining = value.AsSpan();
            while (true)
            {
                var index = remaining.IndexOfAny(AttributeEscapeChars);
                if (index < 0)
                {
                    Output.Write(remaining);
                    break;
                }

                Output.Write(remaining[..index]);
                Output.Write(remaining[index] switch
                {
                    '&' => "&amp;",
                    '<' => "&lt;",
                    '>' => "&gt;",
                    '\'' => "&#39;",
                    _ => "&nbsp;",
                });
                remaining = remaining[(index + 1)..];
            }

            Output.Write('\'');
        }
    }
}
