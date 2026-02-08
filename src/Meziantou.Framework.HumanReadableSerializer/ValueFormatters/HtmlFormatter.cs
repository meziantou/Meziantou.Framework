using Meziantou.Framework.Html;

namespace Meziantou.Framework.HumanReadable.ValueFormatters;

internal sealed class HtmlFormatter : ValueFormatter
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
            var doc = new HtmlDocument();
            doc.LoadHtml(value);

            if (_options.OrderAttributes)
            {
                OrderAttributes(doc);
            }

            if (_options.AttributeQuote is not null)
            {
                NormalizeQuotes(doc, _options.AttributeQuote.Value);
            }

            if (_options.RedactContentSecurityPolicyNonce)
            {
                RedactNonceAttributes(doc);
            }

            using var stringWriter = new StringWriter();
            doc.WriteTo(stringWriter);
            writer.WriteValue(stringWriter.ToString());
        }
        catch
        {
            writer.WriteValue(value);
        }
    }

    private static void OrderAttributes(HtmlDocument document)
    {
        var queue = new Queue<HtmlNode>();
        queue.Enqueue(document);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();

            if (node.HasAttributes)
            {
                var orderedAttributes = node.Attributes
                            .OrderBy(a => a.NamespaceURI, StringComparer.Ordinal)
                            .ThenBy(a => a.Name, StringComparer.Ordinal)
                            .ToArray();

                node.Attributes.RemoveAll();
                foreach (var attribute in orderedAttributes)
                {
                    node.Attributes.AddNoCheck(attribute);
                }
            }

            foreach (var child in node.ChildNodes)
            {
                queue.Enqueue(child);
            }
        }
    }

    private static void RedactNonceAttributes(HtmlDocument document)
    {
        var queue = new Queue<HtmlNode>();
        queue.Enqueue(document);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();

            if (node.HasAttribute("nonce"))
                node.SetAttribute("nonce", "[redacted]");

            foreach (var child in node.ChildNodes)
            {
                queue.Enqueue(child);
            }
        }
    }

    private static void NormalizeQuotes(HtmlDocument document, HtmlAttributeQuote quote)
    {
        var quoteChar = quote switch
        {
            HtmlAttributeQuote.SimpleQuote => '\'',
            _ => '"',
        };

        var queue = new Queue<HtmlNode>();
        queue.Enqueue(document);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();

            if (node.HasAttributes)
            {
                foreach (var attribute in node.Attributes)
                {
                    attribute.QuoteChar = quoteChar;
                }
            }

            foreach (var child in node.ChildNodes)
            {
                queue.Enqueue(child);
            }
        }
    }
}
