using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Renderers.Roundtrip;

namespace Meziantou.Framework.Markdown.Extensions.Figures;

/// <summary>
/// A roundtrip renderer for a <see cref="Figure"/>. The captions are written on the line of their fence.
/// </summary>
internal sealed class RoundtripFigureRenderer : RoundtripObjectRenderer<Figure>
{
    protected override void Write(RoundtripRenderer renderer, Figure obj)
    {
        renderer.RenderLinesBefore(obj);

        // A figure that was not parsed has no source: it is written with the default fences, and its captions as blocks
        var trivia = obj.SourceTrivia;
        var fenceChar = obj.OpeningCharacter == '\0' ? '^' : obj.OpeningCharacter;
        var fenceCount = Math.Max(obj.OpeningCharacterCount, 3);
        var openingCaption = trivia?.OpeningCaption is { } c1 && c1.Parent == obj ? c1 : null;
        var closingCaption = trivia?.ClosingCaption is { } c2 && c2.Parent == obj ? c2 : null;

        renderer.Write(obj.TriviaBefore);
        renderer.Write(fenceChar, fenceCount);
        WriteFenceEnd(renderer, trivia?.TriviaAfterOpeningFence ?? StringSlice.Empty, openingCaption, trivia?.OpeningNewLine ?? NewLine.LineFeed);

        foreach (var block in obj)
        {
            if (block != openingCaption && block != closingCaption)
            {
                renderer.Write(block);
            }
        }

        if (trivia is null || trivia.ClosingCharacterCount > 0)
        {
            renderer.Write(trivia?.TriviaBeforeClosingFence ?? StringSlice.Empty);
            renderer.Write(fenceChar, trivia?.ClosingCharacterCount ?? fenceCount);
            WriteFenceEnd(renderer, trivia?.TriviaAfterClosingFence ?? StringSlice.Empty, closingCaption, trivia is null ? NewLine.LineFeed : obj.NewLine);
        }

        renderer.RenderLinesAfter(obj);
    }

    private static void WriteFenceEnd(RoundtripRenderer renderer, StringSlice triviaAfterFence, FigureCaption? caption, NewLine newLine)
    {
        renderer.Write(triviaAfterFence);
        if (caption is not null)
        {
            // The inlines of the caption end with the line break of its line
            renderer.Write(caption);
        }
        else
        {
            renderer.WriteLine(newLine);
        }
    }
}
