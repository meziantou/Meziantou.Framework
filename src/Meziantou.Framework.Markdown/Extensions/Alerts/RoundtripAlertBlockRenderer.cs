using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Renderers.Roundtrip;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Extensions.Alerts;

/// <summary>
/// A roundtrip renderer for an <see cref="AlertBlock"/>: a quote whose first line is the kind of the alert.
/// </summary>
internal sealed class RoundtripAlertBlockRenderer : RoundtripObjectRenderer<AlertBlock>
{
    protected override void Write(RoundtripRenderer renderer, AlertBlock obj)
    {
        if (obj.QuoteLines.Count == 0)
        {
            // An alert that was not parsed has no quote lines: each line of its content is quoted
            renderer.PushIndent("> ");
            WriteKind(renderer, obj);
            renderer.WriteLine(NewLine.LineFeed);
            renderer.WriteChildren(obj);
            renderer.PopIndent();
            renderer.RenderLinesAfter(obj);
            return;
        }

        QuoteBlockRenderer.WriteQuote(renderer, obj, WriteKind);
    }

    private static void WriteKind(RoundtripRenderer renderer, QuoteBlock quoteBlock)
    {
        var alert = (AlertBlock)quoteBlock;
        renderer.Write("[!").Write(alert.Kind).Write(']').Write(alert.TriviaSpaceAfterKind);
        if (alert.NewLineAfterKind != NewLine.None)
        {
            renderer.WriteLine(alert.NewLineAfterKind);
        }
    }
}
