using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Fonts;

/// <summary>
/// The characters of the glyph names of a Type 1 font: the Adobe Glyph List (vendored from adobe-type-tools/agl-aglfn,
/// see <c>THIRD-PARTY-NOTICES.md</c>) and the names <c>uniXXXX</c> and <c>uXXXX</c> to <c>uXXXXXX</c> of the Adobe Glyph
/// List Specification.
/// </summary>
/// <remarks>
/// Only the names of one character are mapped: a ligature (<c>f_i</c>), a sequence of the list and a variant
/// (<c>a.sc</c>) have no character, so that the character map gives the glyph of the character itself.
/// </remarks>
internal static class Type1GlyphNames
{
    private static readonly Lazy<Dictionary<string, int>> List = new(Load);

    /// <summary>Gets the character of a glyph name.</summary>
    public static bool TryGetCodePoint(string name, out int codePoint)
    {
        if (List.Value.TryGetValue(name, out codePoint))
            return true;

        if (name.Length == 7 && name.StartsWith("uni", StringComparison.Ordinal) && TryParseHex(name.AsSpan(3), out codePoint))
            return !IsSurrogate(codePoint);

        if (name.Length is >= 5 and <= 7 && name[0] == 'u' && TryParseHex(name.AsSpan(1), out codePoint))
            return codePoint <= 0x10FFFF && !IsSurrogate(codePoint);

        codePoint = 0;
        return false;
    }

    private static bool IsSurrogate(int value) => value is >= 0xD800 and <= 0xDFFF;

    /// <summary>Parses upper-case hexadecimal digits, the only ones the specification allows.</summary>
    private static bool TryParseHex(ReadOnlySpan<char> text, out int value)
    {
        value = 0;
        foreach (var c in text)
        {
            if (c is not ((>= '0' and <= '9') or (>= 'A' and <= 'F')))
                return false;
        }

        return int.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Reads the list: lines of a name and code points separated by a semicolon; the names of a sequence are skipped.</summary>
    private static Dictionary<string, int> Load()
    {
        var result = new Dictionary<string, int>(4400, StringComparer.Ordinal);
        using var stream = typeof(Type1GlyphNames).Assembly.GetManifestResourceStream("Meziantou.Framework.Fonts.Type1.glyphlist.txt") ?? throw new InvalidOperationException("The resource glyphlist.txt is missing");
        using var reader = new StreamReader(stream, Encoding.ASCII);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#')
                continue;

            var separator = line.IndexOf(';', StringComparison.Ordinal);
            if (separator <= 0 || line.AsSpan(separator).Contains(' '))
                continue;

            if (int.TryParse(line.AsSpan(separator + 1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var codePoint))
            {
                result.TryAdd(line[..separator], codePoint);
            }
        }

        return result;
    }
}
