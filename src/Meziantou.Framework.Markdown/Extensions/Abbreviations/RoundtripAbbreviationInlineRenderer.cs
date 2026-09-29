using Meziantou.Framework.Markdown.Renderers.Roundtrip;

namespace Meziantou.Framework.Markdown.Extensions.Abbreviations;

/// <summary>
/// A roundtrip renderer for an <see cref="AbbreviationInline"/>, which is written as its label, the text it replaces.
/// </summary>
internal sealed class RoundtripAbbreviationInlineRenderer : RoundtripObjectRenderer<AbbreviationInline>
{
    protected override void Write(RoundtripRenderer renderer, AbbreviationInline obj)
    {
        renderer.Write(obj.Abbreviation.Label);
    }
}
