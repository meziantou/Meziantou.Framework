using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Markdown.Parsing;

internal static class MarkdownParser
{
    public static GreenNode Parse(string text, MarkdownParseOptions options, CellContext? cell = null)
    {
        using var elements = new TreeBuffer(Math.Max(256, text.Length / 8));
        var triviaSpans = BlockParser.Parse(text, options, elements, cell);
        return GreenBuilder.Build(text, elements, triviaSpans, options.SyntaxExtensionSet?.Extensions);
    }
}
