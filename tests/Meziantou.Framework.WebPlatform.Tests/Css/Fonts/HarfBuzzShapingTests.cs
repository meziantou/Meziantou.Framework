using System.Globalization;
using System.Text;
using Meziantou.Framework.WebPlatform.Css.Fonts;
using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Fonts;

/// <summary>
/// The shaping tests of HarfBuzz (test/shape/data/in-house, MIT license) of the complex-script shapers whose fonts are under a
/// free license: <c>Resources/HarfBuzz/harfbuzz.tests</c>, extracted by <c>Resources/HarfBuzz/make-harfbuzz-tests.py</c>
/// (the expected glyph names are replaced by glyph ids). Each test shapes code points with a font, as <c>hb-shape</c> does
/// (the script and the direction of the first character that has a script, the positions in font units), and compares the
/// glyphs, the clusters and the positions with the expected output of HarfBuzz.
/// </summary>
public sealed class HarfBuzzShapingTests
{
    private static readonly string Directory = Path.Combine(TestPaths.Root, "tests", "Meziantou.Framework.WebPlatform.Tests", "Css", "Fonts", "Resources", "HarfBuzz");

    /// <summary>The tests whose result differs from HarfBuzz on purpose.</summary>
    private static readonly Dictionary<string, string> Deviations = new(StringComparer.Ordinal)
    {
        // HarfBuzz computes the fallback positions of the marks in integers (here in font units: the halves are truncated);
        // the positions are not rounded here, as with the fine scale of Chrome's fonts (1/65536 px)
        ["arabic-fallback-shaping.tests:2"] = "The fallback mark positioning is not rounded to integers",
    };

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
        if (Deviations.TryGetValue(test, out var reason))
        {
            Assert.Skip(reason);
        }

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
            var code = char.ToUpperInvariant(scriptCode[0]) + scriptCode[1..].ToLowerInvariant();
            script = Enum.GetValues<UnicodeScript>().FirstOrDefault(value => string.Equals(UnicodeScripts.GetIso15924Code(value), code, StringComparison.Ordinal));
        }
        else
        {
            foreach (var codePoint in codePoints)
            {
                var value = UnicodeScripts.GetScript(codePoint);
                if (value is not (UnicodeScript.Common or UnicodeScript.Inherited or UnicodeScript.Unknown))
                {
                    script = value;
                    break;
                }
            }
        }

        var direction = GetOption("--direction") switch
        {
            "rtl" => ShapingDirection.RightToLeft,
            "ttb" => ShapingDirection.TopToBottom,
            "ltr" => ShapingDirection.LeftToRight,
            _ => OpenTypeShapingEngine.IsRightToLeftScript(script) ? ShapingDirection.RightToLeft : ShapingDirection.LeftToRight,
        };

        FontFeatureList? features = null;
        if (GetOption("--features") is { } featureList)
        {
            var items = new List<FontFeature>();
            foreach (var feature in featureList.Split(','))
            {
                var value = 1u;
                var name = feature;
                if (name[0] is '-' or '+')
                {
                    value = name[0] == '-' ? 0u : 1u;
                    name = name[1..];
                }

                if (name.IndexOf('=', StringComparison.Ordinal) is var equal and >= 0)
                {
                    value = uint.Parse(name[(equal + 1)..], CultureInfo.InvariantCulture);
                    name = name[..equal];
                }

                items.Add(new FontFeature(ShapePlanBuilder.Tag(name.PadRight(4)), value));
            }

            features = new FontFeatureList([.. items]);
        }

        using var buffer = new ShapingBuffer();
        buffer.Reset(codePoints.Length);
        buffer.Direction = direction;
        buffer.Script = script;
        for (var i = 0; i < codePoints.Length; i++)
        {
            buffer.Add(codePoints[i], i);
        }

        var key = new ShapePlanKey(script, direction, GetOption("--language"), LanguageOverride: 0, FaceFeatures: null, features, font.LayoutGsub?.FindFeatureVariations(null) ?? -1, font.LayoutGpos?.FindFeatureVariations(null) ?? -1, DisableKerning: false);
        OpenTypeShapingEngine.Shape(font.GetShapePlan(key), buffer, new ShapingFont(font, null));

        // HarfBuzz outputs the glyphs in the visual order, and replaces the default ignorable characters by the space glyph
        // (or removes them when the font has no space)
        int space = font.GetGlyphId(' ');
        var hasSpace = space != 0;
        var glyphs = new List<string>();
        var noPositions = options.Contains("--no-positions", StringComparer.Ordinal);
        var noClusters = options.Contains("--no-clusters", StringComparer.Ordinal);
        for (var i = 0; i < buffer.Length; i++)
        {
            ref readonly var info = ref buffer.Info[i];
            ref readonly var position = ref buffer.Positions[i];
            var glyph = info.Codepoint;
            if (info.IsDefaultIgnorable)
            {
                if (!hasSpace)
                    continue;

                glyph = space;
            }

            var text = new StringBuilder();
            text.Append(glyph.ToString(CultureInfo.InvariantCulture));
            if (!noClusters)
            {
                text.Append('=').Append(info.Cluster.ToString(CultureInfo.InvariantCulture));
            }

            if (!noPositions)
            {
                var xOffset = (int)Math.Round(position.XOffset, MidpointRounding.ToEven);
                var yOffset = (int)Math.Round(position.YOffset, MidpointRounding.ToEven);
                if (xOffset != 0 || yOffset != 0)
                {
                    text.Append(CultureInfo.InvariantCulture, $"@{xOffset},{yOffset}");
                }

                text.Append('+').Append(((int)Math.Round(position.XAdvance, MidpointRounding.ToEven)).ToString(CultureInfo.InvariantCulture));
                var yAdvance = (int)Math.Round(position.YAdvance, MidpointRounding.ToEven);
                if (yAdvance != 0)
                {
                    text.Append(',').Append(yAdvance.ToString(CultureInfo.InvariantCulture));
                }
            }

            glyphs.Add(text.ToString());
        }

        if (buffer.Direction == ShapingDirection.RightToLeft)
        {
            glyphs.Reverse();
        }

        return "[" + string.Join('|', glyphs) + "]";
    }
}
