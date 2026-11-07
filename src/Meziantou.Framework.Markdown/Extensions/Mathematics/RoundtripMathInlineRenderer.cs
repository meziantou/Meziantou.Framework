using Meziantou.Framework.Markdown.Renderers.Roundtrip;

namespace Meziantou.Framework.Markdown.Extensions.Mathematics;

/// <summary>
/// A roundtrip renderer for a <see cref="MathInline"/>.
/// </summary>
internal sealed class RoundtripMathInlineRenderer : RoundtripObjectRenderer<MathInline>
{
    protected override void Write(RoundtripRenderer renderer, MathInline obj)
    {
        if (obj.SourceText.Text is not null)
        {
            renderer.WriteLines(obj.SourceText);
            return;
        }

        // An inline that was not parsed has no source: it is written with its delimiters
        var delimiter = obj.Delimiter == '\0' ? '$' : obj.Delimiter;
        var delimiterCount = Math.Max(obj.DelimiterCount, 1);
        renderer.Write(delimiter, delimiterCount);
        renderer.Write(obj.Content);
        renderer.Write(delimiter, delimiterCount);
    }
}
