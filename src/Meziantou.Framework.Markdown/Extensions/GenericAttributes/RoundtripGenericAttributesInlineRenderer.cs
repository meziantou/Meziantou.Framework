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

        // The heading renderer writes the changed attributes of the heading in place of the ones of the text
        if (obj.Heading is { } heading && HeadingAttributesTrivia.GetChanged(heading) is not null)
        {
            text = text[text.TrimEnd("\r\n").Length..];
        }

        // The attributes can be followed by the line break that ends their line
        renderer.WriteLines(text);
    }
}
