using System.Globalization;
using System.Text;
using Meziantou.Framework.Fonts;
using Meziantou.Framework.WebPlatform.Css.Fonts;
using Meziantou.Framework.WebPlatform.Unicode;
using SharedShapingFeature = Meziantou.Framework.Fonts.ShapingFeature;
using SharedTextShaper = Meziantou.Framework.Fonts.TextShaper;
using UnicodeScript = Meziantou.Framework.WebPlatform.Unicode.UnicodeScript;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Fonts;

/// <summary>
/// The shaping tests of HarfBuzz (test/shape/data/in-house, MIT license) of the complex-script shapers whose fonts are under a
/// free license: <c>Resources/HarfBuzz/harfbuzz.tests</c>, extracted by <c>Resources/HarfBuzz/make-harfbuzz-tests.py</c>
/// (the expected glyph names are replaced by glyph ids). Each test shapes code points with a font with the shaper of
/// Meziantou.Framework.Fonts, as <c>hb-shape</c> does (the script and the direction of the first character that has a script,
/// the positions in font units), and compares the glyphs, the clusters and the positions with the expected output of HarfBuzz.
/// </summary>
public sealed class HarfBuzzShapingTests
{
    private static readonly string Directory = Path.Combine(TestPaths.Root, "tests", "Meziantou.Framework.WebPlatform.Tests", "Css", "Fonts", "Resources", "HarfBuzz");

    public static TheoryData<string> Tests
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var line in File.ReadLines(Path.Combine(Directory, "harfbuzz.tests")))
            {
                if (line.Length > 0 && line[0] != '#')
                {
                    data.Add(line[..line.IndexOf(';', StringComparison.Ordinal)]);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Tests))]
    public void ShapeAsHarfBuzz(string test)
    {
        var line = File.ReadLines(Path.Combine(Directory, "harfbuzz.tests")).Single(line => line.StartsWith(test + ";", StringComparison.Ordinal));
        var fields = line.Split(';');
        var options = fields[2].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var codePoints = fields[3].Split(',').Select(value => int.Parse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToArray();
        var font = FontFile.Load(File.ReadAllBytes(Path.Combine(Directory, "fonts", fields[1])))!.Faces[0];

        var actual = ShapeAndSerialize(font, codePoints, options);
        Assert.Equal(fields[4], actual);
    }

    /// <summary>Shapes code points as hb-shape and serializes the glyphs as HarfBuzz (hb_buffer_serialize_glyphs, text format).</summary>
    private static string ShapeAndSerialize(OpenTypeFont font, int[] codePoints, string[] options)
    {
        string? GetOption(string name) => options.FirstOrDefault(option => option.StartsWith(name + "=", StringComparison.Ordinal))?[(name.Length + 1)..];

        // The script and the direction of the text (hb_buffer_guess_segment_properties)
        var script = UnicodeScript.Common;
        if (GetOption("--script") is { } scriptCode)
        {
            var tag = ((uint)char.ToUpperInvariant(scriptCode[0]) << 24) | ((uint)char.ToLowerInvariant(scriptCode[1]) << 16) | ((uint)char.ToLowerInvariant(scriptCode[2]) << 8) | char.ToLowerInvariant(scriptCode[3]);
            script = (UnicodeScript)ScriptCodes.Codes.IndexOf(tag);
        }
        else
        {
            foreach (var codePoint in codePoints)
            {
                var value = UnicodeProperties.GetScript(codePoint);
                if (value is not (UnicodeScript.Common or UnicodeScript.Inherited or UnicodeScript.Unknown))
                {
                    script = value;
                    break;
                }
            }
        }

        var direction = GetOption("--direction") switch
        {
            "rtl" => Direction.RightToLeft,
            "ttb" => Direction.TopToBottom,
            "ltr" => Direction.LeftToRight,
            _ => IsRightToLeftScript(script) ? Direction.RightToLeft : Direction.LeftToRight,
        };

        var features = new List<SharedShapingFeature>();
        if (GetOption("--features") is { } featureList)
        {
            foreach (var feature in featureList.Split(','))
            {
                var value = 1;
                var name = feature;
                if (name[0] is '-' or '+')
                {
                    value = name[0] == '-' ? 0 : 1;
                    name = name[1..];
                }

                if (name.IndexOf('=', StringComparison.Ordinal) is var equal and >= 0)
                {
                    value = int.Parse(name[(equal + 1)..], CultureInfo.InvariantCulture);
                    name = name[..equal];
                }

                features.Add(new SharedShapingFeature(name.PadRight(4), value));
            }
        }

        var buffer = new GlyphBuffer(FontResourceLimits.Default);
        for (var i = 0; i < codePoints.Length; i++)
        {
            buffer.Add(codePoints[i], i);
        }

        var languages = GetOption("--language") is { } language ? OpenTypeLanguage.GetTags(language) : [];
        SharedTextShaper.Shape(buffer, font.Face, OpenTypeScript.FromIso15924(ScriptCodes.Codes[(int)script]), languages, direction == Direction.RightToLeft, [.. features], direction == Direction.TopToBottom);

        // HarfBuzz outputs the glyphs in the visual order, and replaces the default ignorable characters by the space glyph
        // (or removes them when the font has no space), as the shaper
        var glyphs = new List<string>();
        var noPositions = options.Contains("--no-positions", StringComparer.Ordinal);
        var noClusters = options.Contains("--no-clusters", StringComparer.Ordinal);
        for (var i = 0; i < buffer.Length; i++)
        {
            var info = buffer.Info[i];
            var position = buffer.Positions[i];
            var text = new StringBuilder();
            text.Append(info.Glyph.ToString(CultureInfo.InvariantCulture));
            if (!noClusters)
            {
                text.Append('=').Append(info.Cluster.ToString(CultureInfo.InvariantCulture));
            }

            if (!noPositions)
            {
                if (position.XOffset != 0 || position.YOffset != 0)
                {
                    text.Append(CultureInfo.InvariantCulture, $"@{position.XOffset},{position.YOffset}");
                }

                text.Append('+').Append(position.XAdvance.ToString(CultureInfo.InvariantCulture));
                if (position.YAdvance != 0)
                {
                    text.Append(',').Append(position.YAdvance.ToString(CultureInfo.InvariantCulture));
                }
            }

            glyphs.Add(text.ToString());
        }

        return "[" + string.Join('|', glyphs) + "]";
    }

    /// <summary>The scripts written from right to left (hb_script_get_horizontal_direction).</summary>
    private static bool IsRightToLeftScript(UnicodeScript script) => script is UnicodeScript.Arabic or UnicodeScript.Hebrew or UnicodeScript.Syriac or UnicodeScript.Thaana
        or UnicodeScript.Cypriot or UnicodeScript.Kharoshthi or UnicodeScript.Phoenician or UnicodeScript.Nko or UnicodeScript.Lydian or UnicodeScript.Avestan
        or UnicodeScript.ImperialAramaic or UnicodeScript.InscriptionalPahlavi or UnicodeScript.InscriptionalParthian or UnicodeScript.OldSouthArabian
        or UnicodeScript.OldTurkic or UnicodeScript.Samaritan or UnicodeScript.Mandaic or UnicodeScript.MeroiticCursive or UnicodeScript.MeroiticHieroglyphs
        or UnicodeScript.Manichaean or UnicodeScript.MendeKikakui or UnicodeScript.Nabataean or UnicodeScript.OldNorthArabian or UnicodeScript.Palmyrene
        or UnicodeScript.PsalterPahlavi or UnicodeScript.Hatran or UnicodeScript.Adlam or UnicodeScript.HanifiRohingya or UnicodeScript.OldSogdian
        or UnicodeScript.Sogdian or UnicodeScript.Elymaic or UnicodeScript.Chorasmian or UnicodeScript.Yezidi or UnicodeScript.OldUyghur
        or UnicodeScript.Garay or UnicodeScript.Sidetic;

    private enum Direction
    {
        LeftToRight,
        RightToLeft,
        TopToBottom,
    }
}
