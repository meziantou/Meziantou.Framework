using Meziantou.Framework.Markdown.Renderers.Roundtrip;

namespace Meziantou.Framework.Markdown.Extensions.Abbreviations;

/// <summary>
/// A roundtrip renderer for the definition of an <see cref="Abbreviation"/>.
/// </summary>
internal sealed class RoundtripAbbreviationRenderer : RoundtripObjectRenderer<Abbreviation>
{
    protected override void Write(RoundtripRenderer renderer, Abbreviation obj)
    {
        renderer.RenderLinesBefore(obj);
        renderer.Write(obj.TriviaBefore);
        if (obj.SourceText.Text is not null)
        {
            renderer.Write(obj.SourceText);
        }
        else
        {
            // A definition created in code is written with the default syntax
            renderer.Write("*[").Write(obj.Label).Write("]: ").Write(obj.Text);
        }

        renderer.WriteLine(obj.NewLine);
        renderer.RenderLinesAfter(obj);
    }
}
