using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Renderers.Roundtrip;

namespace Meziantou.Framework.Markdown.Extensions.CustomContainers;

/// <summary>
/// A roundtrip renderer for a <see cref="CustomContainer"/>.
/// </summary>
internal sealed class RoundtripCustomContainerRenderer : RoundtripObjectRenderer<CustomContainer>
{
    protected override void Write(RoundtripRenderer renderer, CustomContainer obj)
    {
        renderer.RenderLinesBefore(obj);
        if (obj.OpeningFencedCharCount == 0)
        {
            // A container that was not parsed has no fences: it is written with the default ones
            renderer.Write(":::").Write(obj.Info);
            if (!string.IsNullOrEmpty(obj.Arguments))
            {
                renderer.Write(' ').Write(obj.Arguments);
            }

            renderer.WriteLine(NewLine.LineFeed);
            renderer.WriteChildren(obj);
            renderer.Write(":::").WriteLine(NewLine.LineFeed);
            renderer.RenderLinesAfter(obj);
            return;
        }

        renderer.Write(obj.TriviaBefore);
        renderer.Write(obj.FencedChar, obj.OpeningFencedCharCount);
        renderer.Write(obj.TriviaAfterFencedChar);
        renderer.Write(obj.UnescapedInfo);
        renderer.Write(obj.TriviaAfterInfo);
        renderer.Write(obj.UnescapedArguments);
        renderer.Write(obj.TriviaAfterArguments);
        renderer.WriteLine(obj.InfoNewLine);

        renderer.WriteChildren(obj);

        renderer.Write(obj.TriviaBeforeClosingFence);
        renderer.Write(obj.FencedChar, obj.ClosingFencedCharCount);
        renderer.Write(obj.TriviaAfter);
        if (obj.ClosingFencedCharCount > 0)
        {
            renderer.WriteLine(obj.NewLine);
        }

        renderer.RenderLinesAfter(obj);
    }
}
