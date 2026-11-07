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

        options ??= HighlightOptions.Default;
        var compiled = LanguageRegistry.Get(language, options.MatchTimeout);
        return Tokenizer.Highlight(text, compiled, options);
    }

    /// <summary>
    /// Highlights <paramref name="text"/> if <paramref name="language"/> is supported.
    /// </summary>
    /// <returns><see langword="true"/> if the language is supported and <paramref name="html"/> was produced; otherwise <see langword="false"/>.</returns>
    public static bool TryHighlight(string text, string language, [NotNullWhen(true)] out string? html, HighlightOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(language);

        options ??= HighlightOptions.Default;
        if (!LanguageRegistry.TryGet(language, options.MatchTimeout, out var compiled))
        {
            html = null;
            return false;
        }

        html = Tokenizer.Highlight(text, compiled, options);
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
