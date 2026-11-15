using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Markdown.Parsing;

internal static class MarkdownParser
{
    public static GreenNode Parse(string text, MarkdownParseOptions options, CellContext? cell = null)
    {
        var (document, triviaSpans) = BlockParser.Parse(text, options, cell);
        return GreenBuilder.Build(text, document, triviaSpans);
    }
}
