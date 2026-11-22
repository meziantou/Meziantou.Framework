using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Fonts;

/// <summary>
/// Gives the characters of the glyph names of PostScript fonts (Type 1 and CFF fonts, AFM files, the simple fonts of PDF
/// documents): the Adobe Glyph List, the list of ITC Zapf Dingbats (vendored from adobe-type-tools/agl-aglfn, see
/// <c>THIRD-PARTY-NOTICES.md</c>), and the naming conventions of the Adobe Glyph List Specification (<c>uniXXXX</c>,
/// <c>uXXXX</c> to <c>uXXXXXX</c>, ligatures written with <c>_</c>, suffixes after a period).
/// </summary>
/// <remarks>The lists are loaded on first use. The members are thread-safe.</remarks>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Imaging.Tests/FontTests.cs" ref:region="AdobeGlyphList_GlyphNames" ref:dedent="true">
/// // The text of the glyph names of PostScript fonts, such as the names of the characters of an AFM file
/// Assert.Equal("\u00E9", AdobeGlyphList.ToUnicode("eacute"));
/// Assert.Equal("ffi", AdobeGlyphList.ToUnicode("f_f_i")); // a ligature names its components
/// Assert.Equal("a", AdobeGlyphList.ToUnicode("a.sc")); // a variant has the characters of its base name
/// Assert.Equal("\u2701", AdobeGlyphList.ToUnicode("a1", zapfDingbats: true)); // the names of ITC Zapf Dingbats
///
/// // A character map maps one character to a glyph: a ligature or a variant has no character
/// Assert.True(AdobeGlyphList.TryGetCodePoint("uni20AC", out var euro));
/// Assert.Equal(0x20AC, euro);
/// Assert.False(AdobeGlyphList.TryGetCodePoint("f_f_i", out _));
/// </code>
/// </example>
public static class AdobeGlyphList
{
    private static readonly Lazy<Dictionary<string, string>> Standard = new(() => Load("glyphlist.txt"));
    private static readonly Lazy<Dictionary<string, string>> ZapfDingbats = new(() => Load("zapfdingbats.txt"));

    /// <summary>Gets the text of a glyph name, following the Adobe Glyph List Specification.</summary>
    /// <param name="name">The glyph name, such as <c>eacute</c>, <c>uni20AC</c>, <c>f_f_i</c> or <c>a.sc</c>.</param>
    /// <param name="zapfDingbats">Whether the font is ITC Zapf Dingbats, whose names (<c>a1</c>, ...) have their own list.</param>
    /// <returns>
    /// The text of the name, which can be several characters (a ligature, or a name of the list that maps to a sequence),
    /// or <see langword="null"/> when the name has no character.
    /// </returns>
    public static string? ToUnicode(string name, bool zapfDingbats = false)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (zapfDingbats && ZapfDingbats.Value.TryGetValue(name, out var dingbat))
            return dingbat;

        if (Standard.Value.TryGetValue(name, out var text))
            return text;

        // A suffix after a period is a variant of the same characters ("a.sc")
        var period = name.IndexOf('.', StringComparison.Ordinal);
        if (period > 0)
            return ToUnicode(name[..period], zapfDingbats);

        // A ligature names its components ("f_f_i")
        if (name.Contains('_', StringComparison.Ordinal))
        {
            var builder = new StringBuilder();
            foreach (var component in name.Split('_'))
            {
                var value = ToUnicode(component, zapfDingbats);
                if (value is null)
                    return null;

                builder.Append(value);
            }

            return builder.ToString();
        }

        if (name.StartsWith("uni", StringComparison.Ordinal) && name.Length >= 7 && (name.Length - 3) % 4 == 0)
        {
            var builder = new StringBuilder();
            for (var i = 3; i < name.Length; i += 4)
            {
                if (!int.TryParse(name.AsSpan(i, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value) || IsSurrogate(value))
                    return null;

                builder.Append((char)value);
            }

            return builder.ToString();
        }

        if (name is ['u', ..] && name.Length is >= 5 and <= 7 && int.TryParse(name.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var codePoint) && IsScalarValue(codePoint))
            return char.ConvertFromUtf32(codePoint);

        return null;
    }

    /// <summary>Gets the character of a glyph name that names a single character.</summary>
    /// <param name="name">The glyph name, such as <c>eacute</c>, <c>uni20AC</c> or <c>u1F600</c>.</param>
    /// <param name="codePoint">The code point of the character.</param>
    /// <returns>
    /// <see langword="true"/> when the name is a name of the Adobe Glyph List that maps to one character, or a
    /// <c>uniXXXX</c> or <c>uXXXX</c> to <c>uXXXXXX</c> name (upper-case hexadecimal digits) of a Unicode scalar value.
    /// A ligature (<c>f_i</c>), a sequence of the list and a variant (<c>a.sc</c>) have no character, so that the character
    /// map of a font gives the glyph of the character itself.
    /// </returns>
    public static bool TryGetCodePoint(string name, out int codePoint)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (Standard.Value.TryGetValue(name, out var text))
        {
            if (text.Length is 1 || (text.Length is 2 && char.IsSurrogatePair(text[0], text[1])))
            {
                codePoint = char.ConvertToUtf32(text, 0);
                return true;
            }
        }
        else if (name.Length == 7 && name.StartsWith("uni", StringComparison.Ordinal) && TryParseUpperHex(name.AsSpan(3), out codePoint))
        {
            return !IsSurrogate(codePoint);
        }
        else if (name.Length is >= 5 and <= 7 && name[0] == 'u' && TryParseUpperHex(name.AsSpan(1), out codePoint))
        {
            return IsScalarValue(codePoint);
        }

        codePoint = 0;
        return false;
    }

    private static bool IsSurrogate(int value) => value is >= 0xD800 and <= 0xDFFF;

    private static bool IsScalarValue(int value) => value is >= 0 and <= 0x10FFFF && !IsSurrogate(value);

    /// <summary>Parses upper-case hexadecimal digits, the only ones the specification allows.</summary>
    private static bool TryParseUpperHex(ReadOnlySpan<char> text, out int value)
    {
        value = 0;
        foreach (var c in text)
        {
            if (c is not ((>= '0' and <= '9') or (>= 'A' and <= 'F')))
                return false;
        }

        return int.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Reads a list: lines of a name and code points separated by a semicolon.</summary>
    private static Dictionary<string, string> Load(string fileName)
    {
        var result = new Dictionary<string, string>(4400, StringComparer.Ordinal);
        using var stream = CompressedResource.Open(typeof(AdobeGlyphList).Assembly, "Meziantou.Framework.Fonts.Type1." + fileName);
        using var reader = new StreamReader(stream, Encoding.ASCII);
        var builder = new StringBuilder();
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#')
                continue;

            var separator = line.IndexOf(';', StringComparison.Ordinal);
            if (separator <= 0)
                continue;

            builder.Clear();
            foreach (var part in line[(separator + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                builder.Append(char.ConvertFromUtf32(int.Parse(part, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)));
            }

            result.TryAdd(line[..separator], builder.ToString());
        }

        return result;
    }
}
