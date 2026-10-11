using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// The Annotated OpenType Specification tests of Adobe (AOTS): small fonts that each exercise one format of the
/// character map, of the class and coverage tables, or of a lookup of the GSUB and GPOS tables, with the glyphs and
/// the positions the specification gives for a text.
/// </summary>
public sealed class ShapingSuiteTests
{
    public static TheoryData<string> AotsFiles()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(GetRoot() / "tests", "*.tests").Order(StringComparer.Ordinal))
        {
            // Some files only have cases that are commented out.
            if (File.ReadLines(file).Any(IsTest))
            {
                data.Add(Path.GetFileNameWithoutExtension(file));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AotsFiles))]
    public void TheGlyphsAndPositionsOfAotsAreProduced(string name)
    {
        var root = GetRoot();
        var failures = new List<string>();
        var count = 0;
        foreach (var line in File.ReadLines(root / "tests" / (name + ".tests")))
        {
            // A test: the font, the options of the shaping tool, the characters, the expected glyphs.
            if (!IsTest(line))
                continue;

            var fields = line.Split(';');
            if (KnownDivergences.ContainsKey(fields[0] + ";" + fields[2]))
                continue;

            var font = Font.Load(root / "tests" / fields[0]);
            var text = string.Concat(fields[2].Split(',').Select(codePoint => char.ConvertFromUtf32(Convert.ToInt32(codePoint[2..], 16))));
            var withPositions = !fields[1].Contains("--no-positions", StringComparison.Ordinal);
            count++;

            string actual;
            try
            {
                var buffer = new GlyphBuffer(ImageResourceLimits.Default);
                TextShaper.AddText(buffer, text);
                TextShaper.Shape(buffer, font.Face, UnicodeScript.Common, language: 0, isRightToLeft: false, ParseFeatures(fields[1]));
                actual = Format(buffer, withPositions);
            }
            catch (Exception exception) when (exception is InvalidImageContentException or UnsupportedImageFeatureException)
            {
                actual = exception.GetType().Name + ": " + exception.Message;
            }

            if (!string.Equals(actual, fields[3], StringComparison.Ordinal))
            {
                failures.Add($"{fields[0]} {fields[2]}\n    expected {fields[3]}\n    actual   {actual}");
            }
        }

        Assert.True(count > 0, "The file has no test");
        Assert.Empty(failures, $"{failures.Count} of {count} tests fail:\n  " + string.Join("\n  ", failures));
    }

    /// <summary>
    /// The cases the library answers differently, on purpose. A character map can give a glyph identifier beyond the
    /// number of glyphs of the font: such a glyph has no outline and no metrics, and the library reports the character
    /// as missing (glyph 0), where the tests expect the identifier as it is stored.
    /// </summary>
    private static readonly Dictionary<string, string> KnownDivergences = new(StringComparer.Ordinal)
    {
        ["../fonts/cmap4_font1.otf;U+0000,U+0001,U+0010,U+0011,U+0012,U+001E,U+001F,U+00C7,U+00C8,U+00CD,U+00D2,U+00D3,U+FFFF"] = "glyphs 256, 261 and 266 of a font of fewer glyphs",
        ["../fonts/cmap4_font3.otf;U+0000,U+0001,U+0010,U+0011,U+0012,U+001E,U+001F,U+00C7,U+00C8,U+00CD,U+00D2,U+00D3,U+FFFF"] = "glyph 65534 of a font of fewer glyphs",
        ["../fonts/cmap4_font4.otf;U+0000,U+AFC7,U+AFC8,U+AFC9,U+B02B,U+B02C,U+B02D"] = "glyphs above 44500 of a font of fewer glyphs",
        ["../fonts/cmap4_font4.otf;U+0000,U+0063,U+0064,U+01F3,U+01F4,U+01F5,U+03E8,U+03E9"] = "glyphs above 500 of a font of fewer glyphs",
    };

    /// <summary>
    /// Determines whether a line of a file is a test. The lines that start with '#' are the cases the tool the files were
    /// written for does not pass, the ones that start with '@' set its options, and the cases that ask for the flags
    /// of the glyphs are about that tool.
    /// </summary>
    private static bool IsTest(string line) => line.Length > 0 && line[0] is not ('#' or '@') && !line.Contains("--show-flags", StringComparison.Ordinal);

    /// <summary>
    /// Reads the features of a test: <c>test</c> turns a feature on, <c>-test</c> turns it off, <c>test=2</c> gives it a
    /// value, and <c>[3]</c> or <c>[3:5]</c> after the tag restricts the setting to characters.
    /// </summary>
    private static ShapingFeature[] ParseFeatures(string options)
    {
        var start = options.IndexOf("--features=\"", StringComparison.Ordinal) + "--features=\"".Length;
        var features = new List<ShapingFeature>();
        foreach (var item in options[start..options.IndexOf("\"", start, StringComparison.Ordinal)].Split(','))
        {
            var text = item.AsSpan();
            var value = 1;
            if (text[0] == '-')
            {
                value = 0;
                text = text[1..];
            }

            var equals = text.IndexOf('=');
            if (equals >= 0)
            {
                value = int.Parse(text[(equals + 1)..], CultureInfo.InvariantCulture);
                text = text[..equals];
            }

            var first = 0;
            var end = int.MaxValue;
            var bracket = text.IndexOf('[');
            if (bracket >= 0)
            {
                var range = text[(bracket + 1)..^1];
                var colon = range.IndexOf(':');
                first = int.Parse(colon >= 0 ? range[..colon] : range, CultureInfo.InvariantCulture);
                end = colon >= 0 ? int.Parse(range[(colon + 1)..], CultureInfo.InvariantCulture) : first + 1;
                text = text[..bracket];
            }

            features.Add(new ShapingFeature(ShapingFeature.ToTag(text.ToString()), value, first, end));
        }

        return [.. features];
    }

    /// <summary>
    /// Formats glyphs as the tests do: the glyphs separated by bars; with positions, each glyph that is not at the
    /// origin is followed by the place it is drawn at, from the start of the text.
    /// </summary>
    private static string Format(GlyphBuffer buffer, bool withPositions)
    {
        var result = new StringBuilder("[");
        var x = 0;
        var y = 0;
        for (var i = 0; i < buffer.Length; i++)
        {
            if (i > 0)
            {
                result.Append('|');
            }

            result.Append(CultureInfo.InvariantCulture, $"{buffer.Info[i].Glyph}");
            if (withPositions)
            {
                var position = buffer.Positions[i];
                if (x + position.XOffset != 0 || y + position.YOffset != 0)
                {
                    result.Append(CultureInfo.InvariantCulture, $"@{x + position.XOffset},{y + position.YOffset}");
                }

                x += position.XAdvance;
                y += position.YAdvance;
            }
        }

        return result.Append(']').ToString();
    }

    private static FullPath GetRoot() => FixtureRoot.GetDirectory() / "font-suites" / "aots";
}
