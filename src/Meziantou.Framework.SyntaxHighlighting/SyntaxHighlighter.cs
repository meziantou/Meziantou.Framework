using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages;

namespace Meziantou.Framework.SyntaxHighlighting;

public static class SyntaxHighlighter
{
    public static string Highlight(string text, string language, HighlightOptions? options = null)
    {
        var compiled = LanguageRegistry.Get(language);
        return Tokenizer.Highlight(text, compiled, options ?? HighlightOptions.Default);
    }
}
