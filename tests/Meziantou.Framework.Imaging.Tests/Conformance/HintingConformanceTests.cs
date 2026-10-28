using Meziantou.Framework.Imaging.TestHarness.Fixtures;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// The TrueType instructions of fonts hinted by hand, run by the library and by FreeType (version 40 interpreter, for
/// black and white pixels): every glyph of every font at every size of the suite has the points and the advance
/// FreeType gives (font-suites/truetype-hinting).
/// </summary>
public sealed class HintingConformanceTests
{
    public static TheoryData<string> Fonts() => ["DroidSans", "OpenSans-Regular", "Roboto-Regular"];

    [Theory]
    [MemberData(nameof(Fonts))]
    public void TheHintedPointsAreThoseOfFreeType(string name)
    {
        var root = FixtureRoot.GetDirectory() / "font-suites" / "truetype-hinting";
        var environment = new PooledFontEnvironment(FontResourceLimits.Default, 64 * 1024 * 1024);
        var face = FontLoader.Load(File.ReadAllBytes(root / (name + ".subset.ttf")), environment)[0];
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
                            failures.Add($"{size} px, glyph {glyph}: the instructions failed");
                            continue;
                        }

                        var difference = Compare(hinter, advance, contours, points);
                        if (difference is not null)
                        {
                            failures.Add($"{size} px, glyph {glyph}: {difference}");
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
        Assert.Empty(failures);
    }

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
