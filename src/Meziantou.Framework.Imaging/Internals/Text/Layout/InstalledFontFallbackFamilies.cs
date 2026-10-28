namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The families the platforms usually draw a character with when the font of the text does not have it: the color
/// emoji fonts, and the fonts of each script on Windows, macOS and Linux.
/// </summary>
/// <remarks>
/// The families of the platform the code runs on come first, then those of the other platforms: a machine usually has
/// the fonts of its own platform, and the order stays the same on every machine that has the same fonts. A family a
/// machine does not have is skipped. The scripts without an entry use the Noto family named after them
/// (<c>Noto Sans Old Italic</c>), then the fonts of the symbols.
/// </remarks>
internal static class InstalledFontFallbackFamilies
{
    private static readonly Platform Current = OperatingSystem.IsWindows() ? Platform.Windows : OperatingSystem.IsMacOS() || OperatingSystem.IsIOS() ? Platform.Apple : Platform.Other;

    private static readonly FamilyList Emoji = new(["Segoe UI Emoji"], ["Apple Color Emoji"], ["Noto Color Emoji"]);

    private static readonly FamilyList Symbols = new(
        ["Segoe UI Symbol", "Segoe UI Historic", "Cambria Math"],
        ["Apple Symbols", "STIX Two Math", "Menlo"],
        ["Noto Sans Symbols", "Noto Sans Symbols 2", "Noto Sans Math", "DejaVu Sans", "FreeSerif"]);

    private static readonly FamilyList European = new(["Segoe UI", "Arial"], ["Helvetica Neue", "Helvetica", "Arial"], ["Noto Sans", "DejaVu Sans", "Liberation Sans"]);

    private static readonly FamilyList ChineseSimplified = new(
        ["Microsoft YaHei", "SimSun", "Yu Gothic", "Malgun Gothic"],
        ["PingFang SC", "Hiragino Sans GB", "Hiragino Sans", "Apple SD Gothic Neo"],
        ["Noto Sans CJK SC", "Noto Sans SC", "Source Han Sans SC", "WenQuanYi Micro Hei", "Droid Sans Fallback"]);

    private static readonly FamilyList Japanese = new(
        ["Yu Gothic", "Meiryo", "MS Gothic"],
        ["Hiragino Sans", "Hiragino Kaku Gothic ProN"],
        ["Noto Sans CJK JP", "Noto Sans JP", "Source Han Sans JP", "Droid Sans Fallback"]);

    private static readonly FamilyList Korean = new(["Malgun Gothic"], ["Apple SD Gothic Neo"], ["Noto Sans CJK KR", "Noto Sans KR", "Source Han Sans KR", "UnDotum"]);

    private static readonly FamilyList Arabic = new(["Segoe UI", "Arial", "Tahoma"], ["Geeza Pro", "SF Arabic"], ["Noto Sans Arabic", "Noto Naskh Arabic", "DejaVu Sans"]);

    private static readonly FamilyList Hebrew = new(["Segoe UI", "Arial"], ["Arial Hebrew", "SF Hebrew", "Lucida Grande"], ["Noto Sans Hebrew", "DejaVu Sans"]);

    private static readonly FamilyList Thai = new(["Leelawadee UI", "Tahoma"], ["Thonburi"], ["Noto Sans Thai", "Noto Sans Thai Looped"]);

    private static readonly FamilyList Indic = new(["Nirmala UI"], [], []);

    private enum Platform
    {
        Windows,
        Apple,
        Other,
    }

    /// <summary>Gets the families to look in for a character, in order.</summary>
    /// <param name="character">The character.</param>
    /// <param name="isEmoji">Whether the character starts a cluster presented as an emoji.</param>
    public static IEnumerable<string> Get(Rune character, bool isEmoji)
    {
        if (isEmoji)
        {
            foreach (var family in Emoji.Ordered())
                yield return family;
        }

        var script = UnicodeScripts.GetScript(character);
        var list = script switch
        {
            UnicodeScript.Han or UnicodeScript.Bopomofo => ChineseSimplified,
            UnicodeScript.Hiragana or UnicodeScript.Katakana => Japanese,
            UnicodeScript.Hangul => Korean,
            UnicodeScript.Arabic => Arabic,
            UnicodeScript.Hebrew => Hebrew,
            UnicodeScript.Thai => Thai,
            UnicodeScript.Latin or UnicodeScript.Greek or UnicodeScript.Cyrillic => European,
            UnicodeScript.Devanagari or UnicodeScript.Bengali or UnicodeScript.Gujarati or UnicodeScript.Gurmukhi or UnicodeScript.Kannada
                or UnicodeScript.Malayalam or UnicodeScript.Oriya or UnicodeScript.Tamil or UnicodeScript.Telugu or UnicodeScript.Sinhala => Indic,
            _ => null,
        };

        if (list is not null)
        {
            foreach (var family in list.Ordered())
                yield return family;
        }

        if (script is not (UnicodeScript.Common or UnicodeScript.Inherited or UnicodeScript.Unknown or UnicodeScript.Han or UnicodeScript.Latin))
        {
            // The Noto fonts are named after the scripts: Noto Sans Devanagari, Noto Sans Old Italic.
            yield return "Noto Sans " + GetScriptName(script);
            yield return "Noto Serif " + GetScriptName(script);
        }

        foreach (var family in Symbols.Ordered())
            yield return family;
    }

    /// <summary>Gets the name of a script with spaces between its words: <c>Old Italic</c> for <see cref="UnicodeScript.OldItalic"/>.</summary>
    private static string GetScriptName(UnicodeScript script)
    {
        var name = script.ToString();
        var builder = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsAsciiLetterUpper(name[i]))
            {
                builder.Append(' ');
            }

            builder.Append(name[i]);
        }

        return builder.ToString();
    }

    /// <summary>The families of a kind of characters on each platform.</summary>
    private sealed class FamilyList(string[] windows, string[] apple, string[] other)
    {
        public IEnumerable<string> Ordered()
        {
            var lists = Current switch
            {
                Platform.Windows => new[] { windows, apple, other },
                Platform.Apple => [apple, windows, other],
                _ => [other, windows, apple],
            };

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var list in lists)
            {
                foreach (var family in list)
                {
                    if (seen.Add(family))
                        yield return family;
                }
            }
        }
    }
}
