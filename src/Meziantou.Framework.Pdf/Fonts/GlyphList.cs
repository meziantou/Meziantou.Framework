using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Pdf.Fonts;

/// <summary>
/// The characters of the glyph names: the Adobe Glyph List, the list of ITC Zapf Dingbats (vendored from
/// adobe-type-tools/agl-aglfn), and the naming conventions of the Adobe Glyph List Specification (<c>uniXXXX</c>,
/// <c>uXXXX</c>, ligatures written with <c>_</c>, suffixes after a period).
/// </summary>
internal static class GlyphList
{
    private static readonly Lazy<Dictionary<string, string>> Standard = new(() => Load("glyphlist.txt"));
    private static readonly Lazy<Dictionary<string, string>> ZapfDingbats = new(() => Load("zapfdingbats.txt"));

    /// <summary>Gets the text of a glyph name, or <see langword="null"/> when the name has no character.</summary>
    /// <param name="name">The glyph name.</param>
    /// <param name="zapfDingbats">Whether the font is ITC Zapf Dingbats, whose names (<c>a1</c>, ...) have their own list.</param>
    public static string? ToUnicode(string name, bool zapfDingbats = false)
    {
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
                if (!int.TryParse(name.AsSpan(i, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value) || value is >= 0xD800 and <= 0xDFFF)
                    return null;

                builder.Append((char)value);
            }

            return builder.ToString();
        }

        if (name is ['u', ..] && name.Length is >= 5 and <= 7 && int.TryParse(name.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var codePoint) && IsScalarValue(codePoint))
            return char.ConvertFromUtf32(codePoint);

        return null;
    }

    private static bool IsScalarValue(int value) => value is >= 0 and <= 0x10FFFF and not (>= 0xD800 and <= 0xDFFF);

    private static Dictionary<string, string> Load(string fileName)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        using var stream = typeof(GlyphList).Assembly.GetManifestResourceStream("Meziantou.Framework.Pdf.GlyphList." + fileName) ?? throw new InvalidOperationException("The resource " + fileName + " is missing");
        using var reader = new StreamReader(stream, Encoding.ASCII);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#')
                continue;

            var separator = line.IndexOf(';', StringComparison.Ordinal);
            if (separator <= 0)
                continue;

            var builder = new StringBuilder();
            foreach (var part in line[(separator + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                builder.Append(char.ConvertFromUtf32(int.Parse(part, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)));
            }

            result.TryAdd(line[..separator], builder.ToString());
        }

        return result;
    }
}
