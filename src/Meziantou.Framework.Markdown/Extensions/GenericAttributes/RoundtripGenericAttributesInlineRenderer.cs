using Meziantou.Framework.Markdown.Renderers.Roundtrip;

namespace Meziantou.Framework.Markdown.Extensions.GenericAttributes;

/// <summary>
/// A roundtrip renderer for a <see cref="GenericAttributesInline"/>.
/// </summary>
internal sealed class RoundtripGenericAttributesInlineRenderer : RoundtripObjectRenderer<GenericAttributesInline>
{
    protected override void Write(RoundtripRenderer renderer, GenericAttributesInline obj)
    {
        var text = obj.SourceText.AsSpan();

        // The changed attributes of a heading replace the first ones of its content, and the others are written as empty
        // attributes. Removing them could change the blocks: a setext heading whose content only has attributes would have none.
        if (obj.Heading is { } heading && HeadingAttributesTrivia.GetChanged(heading) is not null)
        {
            string? attributes = null;
            if (renderer.PendingHeadingAttributes is { } pending && pending.Heading == heading)
            {
                attributes = pending.Attributes;
                renderer.PendingHeadingAttributes = null;
            }

            renderer.Write(HeadingAttributesTrivia.ReplaceAttributes(attributes, text));
            text = text[text.TrimEnd("\r\n").Length..];
        }

        // The attributes can be followed by the line break that ends their line
        renderer.WriteLines(text);
    }
}
