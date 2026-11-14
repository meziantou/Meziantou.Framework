using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Markdown.Parsing;

internal static class MarkdownParser
{
    public static GreenNode Parse(string text, MarkdownExtensions extensions)
    {
        var (document, triviaSpans) = BlockParser.Parse(text, extensions);
        return GreenBuilder.Build(text, document, triviaSpans);
    }
}
