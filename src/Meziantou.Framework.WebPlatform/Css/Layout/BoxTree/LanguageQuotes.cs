using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The quotation marks of the languages for <c>quotes: auto</c> (https://drafts.csswg.org/css-content-3/#valdef-quotes-auto:
/// "A typographically appropriate used value for quotes is automatically chosen by the UA based on the content language"),
/// from the delimiters of the Unicode CLDR (the generated table).
/// </summary>
internal static partial class LanguageQuotes
{
    // The marks of the root locale: the English quotation marks
    private static readonly string[] Default = ["“", "”", "‘", "’"];

    /// <summary>
    /// Returns the quotation marks of a language tag (the opening and closing marks, then the ones of the nested quotations):
    /// the ones of the longest prefix of its subtags that has some, else the default marks.
    /// </summary>
    public static string[] Get(string? language)
    {
        if (string.IsNullOrEmpty(language))
            return Default;

        var tag = InfraAscii.ToAsciiLowercase(language);
        while (true)
        {
            if (Data.TryGetValue(tag, out var quotes))
                return quotes;

            var index = tag.LastIndexOf('-', StringComparison.Ordinal);
            if (index <= 0)
                return Default;

            tag = tag[..index];
        }
    }
}
