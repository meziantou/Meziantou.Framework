using Meziantou.Framework.SyntaxHighlighting.Engine;
using Meziantou.Framework.SyntaxHighlighting.Languages;

namespace Meziantou.Framework.SyntaxHighlighting;

public static class SyntaxHighlighter
{
    /// <summary>
    /// Highlights <paramref name="text"/> and returns HTML markup.
    /// </summary>
    /// <exception cref="NotSupportedException"><paramref name="language"/> is not supported. Use <see cref="IsSupported"/> or <see cref="TryHighlight"/> to handle unknown languages without an exception.</exception>
    public static string Highlight(string text, string language, HighlightOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(language);

        var compiled = LanguageRegistry.Get(language);
        return Tokenizer.Highlight(text, compiled, options ?? HighlightOptions.Default);
    }

    /// <summary>
    /// Highlights <paramref name="text"/> if <paramref name="language"/> is supported.
    /// </summary>
    /// <returns><see langword="true"/> if the language is supported and <paramref name="html"/> was produced; otherwise <see langword="false"/>.</returns>
    public static bool TryHighlight(string text, string language, [NotNullWhen(true)] out string? html, HighlightOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(language);

        if (!LanguageRegistry.TryGet(language, out var compiled))
        {
            html = null;
            return false;
        }

        html = Tokenizer.Highlight(text, compiled, options ?? HighlightOptions.Default);
        return true;
    }

    /// <summary>
    /// Indicates whether <paramref name="language"/> is a supported language identifier or alias. The comparison is case-insensitive.
    /// </summary>
    public static bool IsSupported(string language)
    {
        ArgumentNullException.ThrowIfNull(language);

        return LanguageRegistry.IsSupported(language);
    }

    /// <summary>
    /// Returns every supported language identifier and alias, in ordinal order.
    /// </summary>
    public static IEnumerable<string> GetSupportedLanguages() => LanguageRegistry.GetSupportedLanguages();
}
