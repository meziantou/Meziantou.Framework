using System.Runtime.InteropServices;

namespace Meziantou.Framework.JavaScriptEngine.Intl.Data;

/// <summary>
/// The language, script, region and variants of a locale identifier (a unicode_language_id of UTS 35), in canonical case
/// (e.g. "zh", "Hant", "TW"). An empty string means the subtag is absent; the language "und" means unknown.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct LanguageId(string Language, string Script, string Region, string Variants = "")
{
    public bool HasScript => Script.Length > 0;

    public bool HasRegion => Region.Length > 0;

    public bool HasVariants => Variants.Length > 0;

    public bool IsUnknownLanguage => Language is "und";

    /// <summary>
    /// Parses the unicode_language_id at the start of a locale identifier ("-" or "_" separated, any case): the parsing
    /// stops at the first extension or private use singleton. Returns false when the subtags are not well-formed.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> value, out LanguageId result)
    {
        result = default;
        Span<Range> ranges = stackalloc Range[32];
        var count = SplitSubtags(value, ranges);
        if (count is 0 or > 32)
            return false;

        var index = 0;
        var language = value[ranges[0]];
        string languageText;
        if (IsAlpha(language) && language.Length is 2 or 3 or (>= 5 and <= 8))
        {
            languageText = ToLowerAscii(language);
            index++;
        }
        else if (language.Length == 4 && IsAlpha(language))
        {
            // unicode_language_id allows a script without a language: "Latn" is "und-Latn"
            languageText = "und";
        }
        else
        {
            return false;
        }

        if (languageText is "root")
        {
            languageText = "und";
        }

        var script = "";
        if (index < count && value[ranges[index]] is { Length: 4 } scriptSpan && IsAlpha(scriptSpan))
        {
            script = ToTitleAscii(scriptSpan);
            index++;
        }

        var region = "";
        if (index < count && value[ranges[index]] is var regionSpan && ((regionSpan.Length == 2 && IsAlpha(regionSpan)) || (regionSpan.Length == 3 && IsDigit(regionSpan))))
        {
            region = ToUpperAscii(regionSpan);
            index++;
        }

        var variants = new List<string>();
        while (index < count)
        {
            var variant = value[ranges[index]];
            if (variant.Length == 1)
                break;

            if (!IsAlphaNumeric(variant) || !(variant.Length is >= 5 and <= 8 || (variant.Length == 4 && char.IsAsciiDigit(variant[0]))))
                return false;

            var text = ToLowerAscii(variant);
            if (variants.Contains(text, StringComparer.Ordinal))
                return false;

            variants.Add(text);
            index++;
        }

        variants.Sort(StringComparer.Ordinal);
        result = new LanguageId(languageText, script, region, string.Join('-', variants));
        return true;
    }

    public static LanguageId Parse(string value) => TryParse(value, out var result) ? result : throw new FormatException("Invalid language identifier: " + value);

    public LanguageId WithLanguage(string language) => this with { Language = language };

    public LanguageId WithScript(string script) => this with { Script = script };

    public LanguageId WithRegion(string region) => this with { Region = region };

    public LanguageId WithoutVariants() => this with { Variants = "" };

    public override string ToString()
    {
        var builder = new StringBuilder(Language);
        if (HasScript)
        {
            builder.Append('-').Append(Script);
        }

        if (HasRegion)
        {
            builder.Append('-').Append(Region);
        }

        if (HasVariants)
        {
            builder.Append('-').Append(Variants);
        }

        return builder.ToString();
    }

    private static int SplitSubtags(ReadOnlySpan<char> value, Span<Range> ranges)
    {
        var count = 0;
        var start = 0;
        for (var i = 0; i <= value.Length; i++)
        {
            if (i == value.Length || value[i] is '-' or '_')
            {
                if (i == start || count == ranges.Length)
                    return 0;

                ranges[count++] = new Range(start, i);
                start = i + 1;
            }
        }

        return count;
    }

    private static bool IsAlpha(ReadOnlySpan<char> value)
    {
        foreach (var c in value)
        {
            if (!char.IsAsciiLetter(c))
                return false;
        }

        return true;
    }

    private static bool IsDigit(ReadOnlySpan<char> value)
    {
        foreach (var c in value)
        {
            if (!char.IsAsciiDigit(c))
                return false;
        }

        return true;
    }

    private static bool IsAlphaNumeric(ReadOnlySpan<char> value)
    {
        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c))
                return false;
        }

        return true;
    }

    internal static string ToLowerAscii(ReadOnlySpan<char> value)
    {
        return string.Create(value.Length, value, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                span[i] = ToLower(source[i]);
            }
        });
    }

    internal static string ToUpperAscii(ReadOnlySpan<char> value)
    {
        return string.Create(value.Length, value, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                span[i] = ToUpper(source[i]);
            }
        });
    }

    private static string ToTitleAscii(ReadOnlySpan<char> value)
    {
        return string.Create(value.Length, value, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                span[i] = i == 0 ? ToUpper(source[i]) : ToLower(source[i]);
            }
        });
    }

    private static char ToLower(char c) => c is >= 'A' and <= 'Z' ? (char)(c + ('a' - 'A')) : c;

    private static char ToUpper(char c) => c is >= 'a' and <= 'z' ? (char)(c - ('a' - 'A')) : c;
}
