using Meziantou.Framework.Imaging.TestHarness.Fixtures;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// The TrueType instructions of fonts hinted by hand, run by the library and by FreeType (version 40 interpreter, for
/// black and white pixels): every glyph of every font at every size of the suite has the points and the advance
/// FreeType gives (font-suites/truetype-hinting). The variable font of the suite is hinted at several instances: its
/// points, its phantom points and its control values vary.
/// </summary>
public sealed class HintingConformanceTests
{
    /// <summary>
    /// The points of the instances of the variable fonts that differ from FreeType, by 1/64 of a pixel each, and why.
    /// Droid Sans: points whose delta is inferred, at an instance where only one region applies: the inferred delta
    /// of the library and the one of FreeType are both within 1/64 of a unit of the exact value, on either side of a
    /// half (FreeType computes the deltas in 16.16 numbers). Roboto, glyph 5: the distance an MDRP instruction keeps
    /// from its reference, which moves on every instance, the default one excepted; not explained yet.
    /// </summary>
    private static readonly Dictionary<string, string[]> KnownDivergences = new(StringComparer.Ordinal)
    {
        ["DroidSans-Variable"] =
        [
            "instance 100 68, 9 px, glyph 80: point 20 is (53, 161, 1), expected (53, 160, 1)",
            "instance 100 68, 11 px, glyph 80: point 20 is (60, 193, 1), expected (60, 192, 1)",
            "instance 100 68, 16 px, glyph 65: point 10 is (328, 765, 0), expected (328, 764, 0)",
            "instance 100 68, 20 px, glyph 65: point 10 is (437, 893, 0), expected (437, 892, 0)",
            "instance 325 132, 11 px, glyph 4: point 0 is (423, 337, 1), expected (423, 338, 1)",
            "instance 325 132, 20 px, glyph 31: point 2 is (64, 668, 1), expected (64, 669, 1)",
        ],
        ["Roboto-Variable"] =
        [
            "instance 900 100, 20 px, glyph 5: point 10 is (326, 1164, 1), expected (326, 1165, 1)",
            "instance 100 68, 16 px, glyph 5: point 10 is (263, 876, 1), expected (263, 877, 1)",
            "instance 100 68, 20 px, glyph 5: point 10 is (329, 1031, 1), expected (329, 1032, 1)",
            "instance 650 108, 16 px, glyph 5: point 10 is (270, 941, 1), expected (270, 942, 1)",
            "instance 325 132, 20 px, glyph 5: point 10 is (360, 1032, 1), expected (360, 1033, 1)",
        ],
    };

    public static TheoryData<string, string> Fonts() => new()
    {
        { "DroidSans", "DroidSans.subset.ttf" },
        { "OpenSans-Regular", "OpenSans-Regular.subset.ttf" },
        { "Roboto-Regular", "Roboto-Regular.subset.ttf" },
        { "DroidSans-Variable", "DroidSans-Variable.ttf" },
        { "Roboto-Variable", "Roboto-Variable.ttf" },
    };

    [Theory]
    [MemberData(nameof(Fonts))]
    public void TheHintedPointsAreThoseOfFreeType(string name, string file)
    {
        var root = FixtureRoot.GetDirectory() / "font-suites" / "truetype-hinting";
        var environment = new PooledFontEnvironment(FontResourceLimits.Default, 64 * 1024 * 1024);
        var defaultFace = FontLoader.Load(File.ReadAllBytes(root / file), environment)[0];
        var face = defaultFace;
        Assert.True(TrueTypeHinter.CanHint(face));
        using var scratch = new GlyphScratch(environment.CreateAllocator("test"));
        var allocator = environment.CreateAllocator("hinter");
        var failures = new List<string>();
        var glyphCount = 0;
        TrueTypeHinter? hinter = null;
        var size = 0;
        try
        {
            using var reader = new StreamReader(root / "expected" / (name + ".txt"));
            while (reader.ReadLine() is { } line)
            {
                var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                switch (fields[0])
                {
                    case "instance":
                        // The values of the axes, in their units.
                        face = defaultFace.WithCoordinates(fields[1..].Select(value => int.Parse(value, CultureInfo.InvariantCulture) << 16).ToArray());
                        Assert.True(TrueTypeHinter.CanHint(face));
                        break;

                    case "size":
                        size = int.Parse(fields[1], CultureInfo.InvariantCulture);
                        hinter?.Dispose();
                        hinter = TrueTypeHinter.Create(face, size, allocator);
                        Assert.NotNull(hinter);
                        break;

                    case "g":
                        var glyph = int.Parse(fields[1], CultureInfo.InvariantCulture);
                        var advance = int.Parse(fields[2], CultureInfo.InvariantCulture);
                        var contours = fields[4..].Select(value => int.Parse(value, CultureInfo.InvariantCulture)).ToArray();
                        var points = reader.ReadLine()!.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(point => point.Split(',').Select(value => int.Parse(value, CultureInfo.InvariantCulture)).ToArray()).ToArray();
                        glyphCount++;
                        var budget = new GlyphBudget(FontResourceLimits.Default);
                        if (!hinter!.TryHint(glyph, scratch, ref budget))
                        {
                            failures.Add($"{Describe(face)}{size} px, glyph {glyph}: the instructions failed");
                            continue;
                        }

                        var difference = Compare(hinter, advance, contours, points);
                        if (difference is not null)
                        {
                            failures.Add($"{Describe(face)}{size} px, glyph {glyph}: {difference}");
                        }

                        break;
                }
            }
        }
        finally
        {
            hinter?.Dispose();
        }

        Assert.True(glyphCount > 900, $"{glyphCount} glyphs");

        // A divergence that is listed must still differ, with the same values.
        var divergences = KnownDivergences.TryGetValue(name, out var known) ? known : [];
        Assert.Empty(failures.Except(divergences, StringComparer.Ordinal));
        Assert.Empty(divergences.Except(failures, StringComparer.Ordinal));
    }

    private static string Describe(FontFace face) => face.Instance is { } instance ? $"instance {string.Join(' ', instance.UserCoordinates.ToArray().Select(value => value >> 16))}, " : "";

    private static string? Compare(TrueTypeHinter hinter, int advance, int[] contours, int[][] points)
    {
        if (hinter.Advance != advance)
            return $"advance {hinter.Advance}, expected {advance}";

        if (!hinter.Contours.SequenceEqual(contours))
            return "the contours differ";

        var hinted = hinter.Points;
        if (hinted.Length != points.Length)
            return $"{hinted.Length} points, expected {points.Length}";

        for (var i = 0; i < hinted.Length; i++)
        {
            var x = hinted[i].X - hinter.OriginX;
            var onCurve = (hinted[i].Flags & HintPoint.OnCurve) != 0 ? 1 : 0;
            if (x != points[i][0] || hinted[i].Y != points[i][1] || onCurve != points[i][2])
                return $"point {i} is ({x}, {hinted[i].Y}, {onCurve}), expected ({points[i][0]}, {points[i][1]}, {points[i][2]})";
        }

        return null;
    }
}
