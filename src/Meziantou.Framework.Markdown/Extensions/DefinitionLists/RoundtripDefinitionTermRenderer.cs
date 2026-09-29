using Meziantou.Framework.Markdown.Renderers.Roundtrip;

namespace Meziantou.Framework.Markdown.Extensions.DefinitionLists;

/// <summary>
/// A roundtrip renderer for a <see cref="DefinitionTerm"/>.
/// </summary>
internal sealed class RoundtripDefinitionTermRenderer : RoundtripObjectRenderer<DefinitionTerm>
{
    protected override void Write(RoundtripRenderer renderer, DefinitionTerm obj)
    {
        renderer.RenderLinesBefore(obj);
        renderer.Write(obj.TriviaBefore);
        renderer.WriteLeafInline(obj);
        renderer.RenderLinesAfter(obj);
    }
}
