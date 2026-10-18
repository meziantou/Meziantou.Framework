using System.Buffers.Binary;
using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>Helpers of the font tests: fonts built in memory, and outlines written as SVG path data.</summary>
internal static class FontTestSupport
{
    public static Font Load(FontBuilder builder, FontLoadOptions? options = null) => Font.Load(builder.Build(), options);

    /// <summary>
    /// Gives a script of the layout tables of a font another tag: the font is then one made for another script, or
    /// for another version of the shaping model of its script. The new tag must keep the script list sorted.
    /// </summary>
    public static byte[] RenameScriptTag(byte[] font, string from, string to)
    {
        var result = (byte[])font.Clone();
        var tableCount = BinaryPrimitives.ReadUInt16BigEndian(result.AsSpan(4));
        var renamed = 0;
        for (var i = 0; i < tableCount; i++)
        {
            var record = result.AsSpan(12 + (i * 16), 16);
            if (!record[..4].SequenceEqual("GSUB"u8) && !record[..4].SequenceEqual("GPOS"u8))
                continue;

            var table = result.AsSpan((int)BinaryPrimitives.ReadUInt32BigEndian(record[8..]), (int)BinaryPrimitives.ReadUInt32BigEndian(record[12..]));
            var scripts = table[BinaryPrimitives.ReadUInt16BigEndian(table[4..])..];
            for (var j = 0; j < BinaryPrimitives.ReadUInt16BigEndian(scripts); j++)
            {
                var tag = scripts.Slice(2 + (j * 6), 4);
                if (tag.SequenceEqual(Encoding.ASCII.GetBytes(from)))
                {
                    Encoding.ASCII.GetBytes(to).CopyTo(tag);
                    renamed++;
                }
            }
        }

        if (renamed == 0)
            throw new ArgumentException($"The font has no script '{from}'", nameof(from));

        return result;
    }

    /// <summary>
    /// Gets the outline of a glyph as SVG path data in font units, with the y axis up, in the form the corpus generator
    /// records it: absolute M, L, Q, C and Z commands, and numbers with at most two decimals.
    /// </summary>
    /// <summary>Gets an instance of a variable font from the syntax of the reference tools: <c>wght=650,wdth=80</c>.</summary>
    public static Font WithVariations(Font font, string variations)
    {
        var settings = new List<FontVariationSetting>();
        foreach (var item in variations.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = item.IndexOf('=', StringComparison.Ordinal);
            settings.Add(new FontVariationSetting(item[..separator], double.Parse(item[(separator + 1)..], CultureInfo.InvariantCulture)));
        }

        return font.WithVariations([.. settings]);
    }

    /// <summary>
    /// Compares two outlines written as SVG paths: the same commands, with numbers that differ by at most a tolerance.
    /// </summary>
    /// <returns>The largest difference, or <see langword="null"/> when the outlines are not the same commands.</returns>
    public static double? GetOutlineDifference(string expected, string actual)
    {
        var expectedParts = SplitOutline(expected);
        var actualParts = SplitOutline(actual);
        if (expectedParts.Count != actualParts.Count)
            return null;

        var difference = 0.0;
        for (var i = 0; i < expectedParts.Count; i++)
        {
            var isExpectedNumber = double.TryParse(expectedParts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var expectedValue);
            var isActualNumber = double.TryParse(actualParts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var actualValue);
            if (isExpectedNumber != isActualNumber)
                return null;

            if (isExpectedNumber)
            {
                difference = Math.Max(difference, Math.Abs(expectedValue - actualValue));
            }
            else if (!string.Equals(expectedParts[i], actualParts[i], StringComparison.Ordinal))
            {
                return null;
            }
        }

        return difference;

        // Commands are letters; numbers are separated by commands, commas and spaces.
        static List<string> SplitOutline(string outline)
        {
            var parts = new List<string>();
            var start = -1;
            for (var i = 0; i <= outline.Length; i++)
            {
                var isNumberCharacter = i < outline.Length && (char.IsAsciiDigit(outline[i]) || outline[i] is '-' or '.');
                if (isNumberCharacter)
                {
                    start = start < 0 ? i : start;
                    continue;
                }

                if (start >= 0)
                {
                    parts.Add(outline[start..i]);
                    start = -1;
                }

                if (i < outline.Length && char.IsAsciiLetter(outline[i]))
                {
                    parts.Add(outline[i].ToString());
                }
            }

            return parts;
        }
    }

    /// <summary>
    /// Gets the outline of a glyph as an SVG path, with the numbers written to a number of decimals. The errors are the
    /// exceptions of the library, as when the glyph is drawn.
    /// </summary>
    public static string GetOutline(Font font, int glyphId, int decimals, ImageConfiguration? configuration = null)
        => GetOutline(font, glyphId, new SvgPathSink { Format = "0." + new string('#', decimals) }, configuration);

    public static string GetOutline(Font font, int glyphId, ImageConfiguration? configuration = null)
        => GetOutline(font, glyphId, new SvgPathSink(), configuration);

    private static string GetOutline(Font font, int glyphId, SvgPathSink sink, ImageConfiguration? configuration)
    {
        using var scratch = new GlyphScratch(AllocationScope.Create(configuration, "FontTests"));
        var budget = new GlyphBudget(font.Face.Data.Limits);
        try
        {
            font.Face.DecodeOutline(glyphId, sink, scratch, ref budget);
        }
        catch (FontException exception)
        {
            throw FontExceptionTranslation.Translate(exception);
        }

        return sink.ToString();
    }

    private sealed class SvgPathSink : OutlineSink
    {
        private readonly StringBuilder _path = new();

        public string Format { get; init; } = "0.##";

        public override void MoveTo(double x, double y) => _path.Append(CultureInfo.InvariantCulture, $"M{Write(x)},{Write(y)}");

        public override void LineTo(double x, double y) => _path.Append(CultureInfo.InvariantCulture, $"L{Write(x)},{Write(y)}");

        public override void QuadraticTo(double controlX, double controlY, double x, double y)
            => _path.Append(CultureInfo.InvariantCulture, $"Q{Write(controlX)},{Write(controlY)} {Write(x)},{Write(y)}");

        public override void CubicTo(double control1X, double control1Y, double control2X, double control2Y, double x, double y)
            => _path.Append(CultureInfo.InvariantCulture, $"C{Write(control1X)},{Write(control1Y)} {Write(control2X)},{Write(control2Y)} {Write(x)},{Write(y)}");

        public override void Close() => _path.Append('Z');

        public override string ToString() => _path.ToString();

        private string Write(double value) => value.ToString(Format, CultureInfo.InvariantCulture);
    }

    /// <summary>Builds a GSUB table whose default script has one feature per substitution: a single substitution of one glyph.</summary>
    public static byte[] BuildSingleSubstitutions(params (string Feature, int From, int To)[] substitutions)
    {
        var table = new List<byte>();
        var count = substitutions.Length;
        var featureList = 28 + (2 * count);
        var lookupList = featureList + 2 + (12 * count);
        UInt16(table, 1);
        UInt16(table, 0);
        UInt16(table, 10);
        UInt16(table, featureList);
        UInt16(table, lookupList);

        // The script list: the default script, whose default language system has every feature
        UInt16(table, 1);
        Tag(table, "DFLT");
        UInt16(table, 8);
        UInt16(table, 4);
        UInt16(table, 0);
        UInt16(table, 0);
        UInt16(table, 0xFFFF);
        UInt16(table, count);
        for (var i = 0; i < count; i++)
        {
            UInt16(table, i);
        }

        // The feature list: each feature has its own lookup
        UInt16(table, count);
        for (var i = 0; i < count; i++)
        {
            Tag(table, substitutions[i].Feature);
            UInt16(table, 2 + (6 * count) + (6 * i));
        }

        for (var i = 0; i < count; i++)
        {
            UInt16(table, 0);
            UInt16(table, 1);
            UInt16(table, i);
        }

        // The lookup list: a single substitution of the second format, one glyph for another
        UInt16(table, count);
        for (var i = 0; i < count; i++)
        {
            UInt16(table, 2 + (2 * count) + (22 * i));
        }

        foreach (var (_, from, to) in substitutions)
        {
            UInt16(table, 1);
            UInt16(table, 0);
            UInt16(table, 1);
            UInt16(table, 8);
            UInt16(table, 2);
            UInt16(table, 8);
            UInt16(table, 1);
            UInt16(table, to);
            UInt16(table, 1);
            UInt16(table, 1);
            UInt16(table, from);
        }

        return [.. table];
    }

    private static void UInt16(List<byte> table, int value)
    {
        table.Add((byte)(value >> 8));
        table.Add((byte)value);
    }

    private static void Tag(List<byte> table, string tag)
    {
        foreach (var character in tag)
        {
            table.Add((byte)character);
        }
    }
}
