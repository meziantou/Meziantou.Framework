using Meziantou.Framework.Markdown.Renderers.Roundtrip;

namespace Meziantou.Framework.Markdown.Extensions.GenericAttributes;

/// <summary>
/// A roundtrip renderer for a <see cref="GenericAttributesInline"/>.
/// </summary>
internal sealed class RoundtripGenericAttributesInlineRenderer : RoundtripObjectRenderer<GenericAttributesInline>
{
    protected override void Write(RoundtripRenderer renderer, GenericAttributesInline obj)
    {
        // The attributes can be followed by the line break that ends their line
        renderer.WriteLines(obj.SourceText);
    }
}
