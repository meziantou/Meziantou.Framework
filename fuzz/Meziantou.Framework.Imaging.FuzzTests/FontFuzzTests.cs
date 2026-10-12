using System.Buffers.Binary;
using System.Diagnostics;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Pixels;
using Meziantou.Framework.Imaging.Tests;
using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.FuzzTests;

/// <summary>
/// Deterministic, bounded mutation fuzzing of fonts: every font of the corpus (TrueType and CFF outlines, collections,
/// WOFF and WOFF2 containers) is mutated, anywhere (<see cref="FuzzMutator"/>) or inside one of its tables so that the
/// table directory still leads to it, then loaded and read: names, metrics, character map, advances and outlines. A font
/// is untrusted input: loading it and decoding a glyph may only fail with the documented exceptions, a decoded outline
/// is made of well-formed contours with coordinates in range, and nothing hangs. The budget is set by
/// <see cref="FuzzSettings"/>.
/// </summary>
public sealed class FontFuzzTests
{
    // Small enough for a glyph that expands without end to be stopped quickly, large enough for the glyphs of the corpus.
    private static readonly FontLoadOptions Options = new()
    {
        Configuration = new ImageConfiguration
        {
            Limits = new ImageResourceLimits { MaxFontBytes = 16 * 1024 * 1024, MaxGlyphComplexity = 50_000, MaxLiveAllocationBytes = 64 * 1024 * 1024 },
        },
    };

    // Characters of the fonts of the corpus (with pairs that kern, ligatures, marks to compose and to attach, a
    // fraction), a character none of them has, a line break and a variation selector.
    private const string SampleText = "AVo fi\u00C9B\uFE00 e\u0301\u0302 ffl To. 1\u20442\n\u65E5\u306E \U0001F600";

    // The characters of the tests of the suites, each twice, and the feature their lookups belong to.
    private const string SuiteText = "\u0011\u0012\u0013\u0014\u0015\u0016\u0017\u0018\u0019\u001A\u001B\u001C\u0011\u0012\u0013\u0014\u0015\u0016\u0017\u0018\u0019\u001A\u0012\u0013\u0011\u0014";
    private static readonly ShapingFeature[] SuiteFeatures = [new("test")];

    private static readonly SolidBrush TextBrush = new(new Rgba32(10, 20, 30, 200));
    private static readonly LinearGradientBrush ColorGradient = new(new PointD(0, 0), new PointD(60, 40), [new GradientStop(0, new Rgba32(255, 0, 0)), new GradientStop(1, new Rgba32(0, 0, 255, 80))]);
    private static readonly VectorPath ColorPath = new VectorPathBuilder().MoveTo(4, 40).QuadraticBezierTo(30, -10, 60, 30).Build();
    private static readonly Pen TextPen = new(new SolidBrush(new Rgba32(200, 100, 0)), 1.5);

    [Fact]
    public void MutatedFontsFailOnlyWithDocumentedErrors()
    {
        var seeds = GetSeeds();
        var spliceSources = seeds.Select(seed => seed.Data).ToList();
        var random = new FuzzRandom(FuzzSettings.Seed ^ FuzzSeeds.GetGroupSalt("font"));
        var failures = new Dictionary<string, string>(StringComparer.Ordinal);
        var outcomes = new Dictionary<string, int>(StringComparer.Ordinal);
        var stopwatch = Stopwatch.StartNew();
        var slowest = TimeSpan.Zero;
        var executed = 0;
        var decodedGlyphs = 0L;
        var rejectedGlyphs = 0L;

        // The unchanged seeds first, then the mutations.
        for (var iteration = -seeds.Count; iteration < FuzzSettings.Iterations; iteration++)
        {
            if (FuzzSettings.Duration is { } budget && stopwatch.Elapsed > budget)
                break;

            var seed = seeds[(iteration + seeds.Count) % seeds.Count];
            var name = iteration < 0 ? seed.Id : $"{seed.Id} mutation {iteration}";
            var input = iteration < 0 ? seed.Data : random.Chance(60) ? MutateTable(random, seed.Data) : FuzzMutator.Mutate(random, seed.Data, spliceSources);

            var started = stopwatch.Elapsed;
            var failure = RunWithWatchdog(input, out var result);
            var elapsed = stopwatch.Elapsed - started;
            slowest = elapsed > slowest ? elapsed : slowest;
            executed++;
            outcomes[result.Outcome] = outcomes.GetValueOrDefault(result.Outcome) + 1;
            decodedGlyphs += result.DecodedGlyphs;
            rejectedGlyphs += result.RejectedGlyphs;
            // Every font of the corpus is read without error; a few fonts of the suites use formats that are not
            // supported, which is said with the documented exception.
            if (iteration < 0 && (failure is not null || (seed.Id.StartsWith("fonts/", StringComparison.Ordinal) && (result.Outcome != "loaded" || result.RejectedGlyphs != 0))))
            {
                Assert.Fail($"The font {seed.Id} of the corpus is not read without error: {failure?.Message ?? result.Outcome}");
            }

            if (failure is null || failures.ContainsKey(failure.Signature))
                continue;

            failures[failure.Signature] = Report(name, input, failure);
            if (failures.Count >= 4)
                break;
        }

        TestContext.Current.TestOutputHelper?.WriteLine(FormattableString.Invariant(
            $"font: {executed} fonts ({seeds.Count} seeds, base seed {FuzzSettings.Seed}) in {stopwatch.Elapsed.TotalSeconds:F1} s, slowest {slowest.TotalMilliseconds:F0} ms; {decodedGlyphs} glyphs decoded, {rejectedGlyphs} rejected; outcomes: {string.Join(", ", outcomes.OrderByDescending(pair => pair.Value).Select(pair => $"{pair.Key}={pair.Value}"))}"));

        if (failures.Count > 0)
            Assert.Fail($"{failures.Count} distinct fuzz failure(s):{Environment.NewLine}{string.Join(Environment.NewLine + Environment.NewLine, failures.Values)}");

        // The campaign must reach the tables and the glyphs, not only the rejection of the table directory.
        if (FuzzSettings.Duration is null && FuzzSettings.Iterations >= FuzzSettings.DefaultIterations)
        {
            Assert.True(outcomes.GetValueOrDefault("loaded") > seeds.Count + (executed / 10), "Too few mutated fonts were loaded.");
            Assert.True(outcomes.GetValueOrDefault(nameof(InvalidImageContentException)) > 0, "No mutated font was rejected as invalid.");
            Assert.True(rejectedGlyphs > 0, "No glyph of a mutated font was rejected.");
            Assert.True(decodedGlyphs > 10_000, "Too few glyphs of mutated fonts were decoded.");
        }
    }

    [Fact]
    public void OracleReportsUndocumentedExceptionsAndMalformedOutlines()
    {
        // The self-test of the oracle: a font of the corpus passes, and a deliberate defect is reported with its signature.
        var seed = GetSeeds()[0].Data;
        Assert.Null(Check(seed, out var result));
        Assert.Equal("loaded", result.Outcome);
        Assert.True(result.DecodedGlyphs > 0);
        Assert.Null(Check([1, 2, 3], out result));
        Assert.Equal(nameof(UnknownImageFormatException), result.Outcome);

        var sink = new CheckingSink();
        sink.MoveTo(0, 0);
        sink.LineTo(1, 1);
        Assert.Equal("an outline ends inside a contour", sink.Finish());
        sink = new CheckingSink();
        sink.LineTo(1, 1);
        Assert.Equal("a segment is outside a contour", sink.Finish());
        sink = new CheckingSink();
        sink.MoveTo(0, double.NaN);
        sink.Close();
        Assert.Equal("a coordinate is out of range", sink.Finish());
        sink = new CheckingSink();
        sink.MoveTo(0, 0);
        sink.QuadraticTo(1, 1, 2, 0);
        sink.CubicTo(1, 1, 2, 2, 0, 0);
        sink.Close();
        Assert.Null(sink.Finish());
        Assert.StartsWith("exception:load:System.InvalidOperationException:", FuzzFailure.FromException("load", new InvalidOperationException()).Signature, StringComparison.Ordinal);
    }

    private static List<FuzzSeed> GetSeeds()
    {
        var root = FixtureRoot.GetDirectory();
        var manifest = FixtureManifestLoader.Load(root / FixtureRoot.ManifestFileName);
        List<FuzzSeed> seeds = [.. (manifest.Fonts ?? []).OrderBy(font => font.Id, StringComparer.Ordinal).Select(font => new FuzzSeed(font.Id, File.ReadAllBytes(root / font.File.Path)))];

        // The fonts of the suites: one small font per format of the layout tables.
        foreach (var suite in manifest.FontSuites ?? [])
        {
            seeds.AddRange(Directory.EnumerateFiles(root / suite.Directory, "*.otf", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(file => new FuzzSeed(suite.Id + "/" + Path.GetFileName(file), File.ReadAllBytes(file))));
        }

        if (seeds.Count < 200)
            throw new InvalidOperationException("The corpus has no font.");

        // A campaign on one kind of font: the fonts whose identifier contains a text.
        if (FuzzSettings.SeedFilter is { } filter)
        {
            seeds.RemoveAll(seed => !seed.Id.Contains(filter, StringComparison.Ordinal));
            if (seeds.Count == 0)
                throw new InvalidOperationException($"No font of the corpus has an identifier that contains '{filter}'.");
        }

        return seeds;
    }

    /// <summary>
    /// Changes a few bytes inside one table of a font, so that the table directory is intact and the readers of the
    /// table are reached. A font in a compressed container is mutated as a whole.
    /// </summary>
    private static byte[] MutateTable(FuzzRandom random, byte[] seed)
    {
        var data = (byte[])seed.Clone();
        var directory = 0;
        if (data.Length >= 16 && BinaryPrimitives.ReadUInt32BigEndian(data) == FontTags.Collection)
        {
            var faceCount = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8)), 16);
            directory = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(12 + (random.Next(Math.Max(faceCount, 1)) * 4)));
        }

        if (directory > data.Length - 12 || BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(directory)) is not (FontTags.TrueType or FontTags.OpenTypeCff))
            return FuzzMutator.Mutate(random, seed, [seed]);

        var tableCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(directory + 4));
        var record = data.AsSpan(directory + 12 + (random.Next(tableCount) * 16), 16);
        var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(record[8..]);
        var length = (int)BinaryPrimitives.ReadUInt32BigEndian(record[12..]);
        if (length == 0)
            return data;

        var changes = random.Next(1, 5);
        for (var i = 0; i < changes; i++)
        {
            var position = offset + random.Next(length);
            switch (random.Next(4))
            {
                case 0:
                    data[position] = random.NextByte();
                    break;

                case 1:
                    data[position] ^= (byte)(1 << random.Next(8));
                    break;

                case 2:
                    data[position] = random.Chance(50) ? (byte)0xFF : (byte)0x00;
                    break;

                default:
                    data[position] = (byte)(data[position] + (random.Chance(50) ? 1 : -1));
                    break;
            }
        }

        return data;
    }

    private static FuzzFailure? RunWithWatchdog(byte[] input, out CheckResult result)
    {
        CheckResult caseResult = default;
        var task = Task.Run(() =>
        {
            var failure = Check(input, out var checkResult);
            caseResult = checkResult;
            return failure;
        });
        if (!task.Wait(FuzzSettings.CaseTimeout))
        {
            result = new CheckResult("hang", 0, 0);
            return FuzzFailure.Property("hang", $"The font was not processed within {FuzzSettings.CaseTimeout.TotalSeconds} s (infinite loop or unbounded work).");
        }

        result = caseResult;
        return task.Result;
    }

    /// <summary>Checks one font file.</summary>
    private static FuzzFailure? Check(byte[] input, out CheckResult result)
    {
        result = new CheckResult("?", 0, 0);
        FontCollection collection;
        try
        {
            collection = FontCollection.Load(input, Options);
        }
        catch (Exception exception) when (exception is InvalidImageContentException or UnsupportedImageFeatureException or UnknownImageFormatException or ImageResourceLimitException)
        {
            result = new CheckResult(exception.GetType().Name, 0, 0);
            return null;
        }
        catch (Exception exception)
        {
            return FuzzFailure.FromException("load", exception);
        }

        // The instances of a variable font are fonts too: the ends of the axes, and a place between them.
        var fonts = new List<Font>(collection);
        foreach (var font in collection)
        {
            try
            {
                var axes = font.VariationAxes;
                if (axes.Count == 0)
                    continue;

                _ = font.NamedInstances.Count;
                fonts.Add(font.WithVariations([.. axes.Select(axis => new FontVariationSetting(axis.Tag, axis.Minimum))]));
                fonts.Add(font.WithVariations([.. axes.Select(axis => new FontVariationSetting(axis.Tag, axis.Maximum))]));
                fonts.Add(font.WithVariations([.. axes.Select((axis, index) => new FontVariationSetting(axis.Tag, axis.Minimum + ((axis.Maximum - axis.Minimum) * (index % 2 == 0 ? 0.37 : 0.81))))]));
                if (font.NamedInstances.Count > 0)
                {
                    fonts.Add(font.WithVariations(font.NamedInstances[^1]));
                }
            }
            catch (Exception exception) when (exception is InvalidImageContentException or UnsupportedImageFeatureException or ImageResourceLimitException)
            {
                // A malformed variation table: the documented outcomes.
            }
            catch (Exception exception)
            {
                return FuzzFailure.FromException("instance", exception);
            }
        }

        var decoded = 0;
        var rejected = 0;
        using var scratch = new GlyphScratch(AllocationScope.Create(Options.Configuration, "FontFuzz"));
        foreach (var font in fonts)
        {
            var isInstance = font.Face.Instance is not null;
            try
            {
                _ = (font.FamilyName, font.SubfamilyName, font.PostScriptName, font.Weight, font.IsItalic, font.IsMonospaced);
                var metrics = font.GetMetrics(16);
                if (font.UnitsPerEm is < 16 or > 16384 || font.GlyphCount is < 1 or > 65535 || !double.IsFinite(metrics.LineHeight))
                    return FuzzFailure.Property("font-values", $"The font has {font.UnitsPerEm} units per em, {font.GlyphCount} glyphs and a line height of {metrics.LineHeight}.");

                // The character map never gives a glyph the font does not have.
                for (var codePoint = 0; codePoint < 0x3100; codePoint++)
                {
                    if ((uint)font.Face.MapCodePoint(codePoint) >= (uint)font.GlyphCount)
                        return FuzzFailure.Property("cmap-range", $"U+{codePoint:X4} maps to a glyph the font does not have.");
                }

                font.Face.MapVariation('A', 0xFE00, out var variationGlyph);
                if ((uint)variationGlyph >= (uint)font.GlyphCount)
                    return FuzzFailure.Property("cmap-range", "A variation sequence maps to a glyph the font does not have.");
            }
            catch (Exception exception)
            {
                return FuzzFailure.FromException("read", exception);
            }

            // Every glyph of a small font; a sample of the glyphs of a large one.
            var step = Math.Max(1, font.GlyphCount / 400);
            for (var glyph = 0; glyph < font.GlyphCount; glyph += step)
            {
                var sink = new CheckingSink();
                try
                {
                    // The advance of an instance is the one of the font and a delta, which can be anything.
                    if (font.Face.GetAdvanceWidth(glyph) is < 0 or > ushort.MaxValue && !isInstance)
                        return FuzzFailure.Property("advance-range", "An advance is not a 16-bit value.");

                    var budget = new GlyphBudget(font.Face.Data.Limits);
                    font.Face.DecodeOutline(glyph, sink, scratch, ref budget);
                    decoded++;
                }
                catch (Exception exception) when (exception is InvalidImageContentException or UnsupportedImageFeatureException or ImageResourceLimitException)
                {
                    // A malformed glyph: the documented outcomes. What was emitted before the error is still checked,
                    // except for the contour the error interrupted.
                    rejected++;
                    continue;
                }
                catch (Exception exception)
                {
                    return FuzzFailure.FromException("decode", exception);
                }

                if (sink.Finish() is { } defect)
                    return FuzzFailure.Property("outline", $"The outline of the glyph {glyph} is malformed: {defect}.");
            }
        }

        // Stroking is by far the slowest step: one font out of eight is stroked, chosen by the content of the file.
        if (CheckText(fonts, withStroke: (input.Length & 7) == 0) is { } textFailure)
            return textFailure;

        if (CheckShaping(fonts) is { } shapingFailure)
            return shapingFailure;

        result = new CheckResult("loaded", decoded, rejected);
        return null;
    }

    /// <summary>
    /// Shapes the characters the fonts of the suites map, with the feature their lookups are in: the shaper stops with a
    /// documented exception or gives glyphs of the font, with clusters that are characters of the text.
    /// </summary>
    private static FuzzFailure? CheckShaping(List<Font> collection)
    {
        foreach (var font in collection)
        {
            if (CheckShaping(font, SuiteText, UnicodeScript.Common, isRightToLeft: false, SuiteFeatures) is { } failure)
                return failure;

            // The scripts that have a shaping model of their own, for the fonts that have their letters.
            foreach (var (script, text) in ScriptTexts)
            {
                bool hasScript;
                try
                {
                    hasScript = font.Face.MapCodePoint(text[0]) != 0;
                }
                catch (Exception exception) when (exception is InvalidImageContentException or UnsupportedImageFeatureException or ImageResourceLimitException)
                {
                    continue;
                }

                if (hasScript && CheckShaping(font, text, script, isRightToLeft: true, []) is { } scriptFailure)
                    return scriptFailure;
            }
        }

        return null;
    }

    private static FuzzFailure? CheckShaping(Font font, string text, UnicodeScript script, bool isRightToLeft, ShapingFeature[] features)
    {
        var buffer = new GlyphBuffer(Options.Configuration.Limits);
        try
        {
            TextShaper.AddText(buffer, text);
            TextShaper.Shape(buffer, font.Face, script, language: 0, isRightToLeft, features);
        }
        catch (Exception exception) when (exception is InvalidImageContentException or UnsupportedImageFeatureException or ImageResourceLimitException)
        {
            return null;
        }
        catch (Exception exception)
        {
            return FuzzFailure.FromException("shape", exception);
        }

        if (buffer.Positions.Length != buffer.Length)
            return FuzzFailure.Property("shape-positions", "The glyphs of a shaped run have no positions.");

        foreach (ref readonly var info in buffer.Info)
        {
            if ((uint)info.Glyph >= (uint)font.GlyphCount && info.Glyph > ushort.MaxValue)
                return FuzzFailure.Property("shape-glyph", $"The shaper gave the glyph {info.Glyph}.");

            if ((uint)info.Cluster >= (uint)text.Length)
                return FuzzFailure.Property("shape-cluster", $"The shaper gave the cluster {info.Cluster} for a text of {text.Length} characters.");
        }

        return null;
    }

    // A text per script: letters in each form, marks in an order that is not the one of shaping, joiners, a lam-alef,
    // a precomposed letter with a mark, and the mark that stretches over a word.
    private static readonly (UnicodeScript Script, string Text)[] ScriptTexts =
    [
        (UnicodeScript.Arabic, "\u0628\u064E\u0651\u0633\u0654\u0650\u0645\u0020\u0644\u0627\u0020\u0644\u200D\u0627\u0020\u0628\u200C\u0628\u0640\u0622\u0655\u0020\u06AF\u0654\u064F"),
        (UnicodeScript.Hebrew, "\u05E9\u05BC\u05C1\u05B8\u05DC\u05D5\u05B9\u05DD\u0020\uFB2C\u05B7\u0020\u05D0\u05B7\u05B0\u05BD\u200D\u05B7"),
        (UnicodeScript.Syriac, "\u0712\u0710\u0712\u0020\u0715\u0710\u0020\u070F\u072B\u0718\u0712\u0730\u0020\u0717\u200D\u0710\u0710"),
    ];

    /// <summary>
    /// Lays out a text with each font and draws it: a layout is refused with a documented exception or is created, and
    /// a layout that was created is drawn, filled, stroked and converted to a path, without any exception.
    /// </summary>
    private static FuzzFailure? CheckText(List<Font> collection, bool withStroke)
    {
        foreach (var font in collection)
        {
            TextLayout layout;
            try
            {
                layout = TextLayout.Create(SampleText, font, 13.5);
            }
            catch (Exception exception) when (exception is InvalidImageContentException or UnsupportedImageFeatureException or ImageResourceLimitException)
            {
                continue;
            }
            catch (Exception exception)
            {
                return FuzzFailure.FromException("layout", exception);
            }

            if (!double.IsFinite(layout.Width) || layout.Width < 0 || layout.LineCount != 2 || !double.IsFinite(layout.InkBounds.Width) || !double.IsFinite(layout.InkBounds.Height))
                return FuzzFailure.Property("layout-values", $"The layout is {layout.Width} wide, has {layout.LineCount} lines and the ink bounds {layout.InkBounds}.");

            try
            {
                using var image = new Image<Rgba32>(40, 28);
                image.Draw((layout, TextBrush, TextPen, withStroke), static (canvas, state) =>
                {
                    canvas.FillText(state.TextBrush, state.layout, new PointD(1.5, 2));
                    if (state.withStroke)
                    {
                        canvas.StrokeText(state.TextPen, state.layout, new PointD(-3, 1.25));
                    }
                });
                // A color glyph can draw something without having an outline of its own.
                var path = layout.ToPath(new PointD(1.5, 2));
                if (path.IsEmpty != layout.InkBounds.IsEmpty && path.IsEmpty && !font.HasColorGlyphs)
                    return FuzzFailure.Property("layout-path", "The layout has ink bounds and an empty path.");
            }
            catch (Exception exception)
            {
                return FuzzFailure.FromException("draw", exception);
            }

            if (font.HasColorGlyphs && CheckColorGlyphs(font) is { } failure)
                return failure;
        }

        return null;
    }

    /// <summary>
    /// Lays out and draws characters a font draws with its own colors, at a point and along a path, with a solid brush
    /// and with a gradient as the color of the text: a layout is refused with a documented exception or is drawn
    /// without any.
    /// </summary>
    private static FuzzFailure? CheckColorGlyphs(Font font)
    {
        var text = new StringBuilder();
        var count = 0;
        try
        {
            _ = font.ColorPalettes.Count;
            foreach (var (first, last) in ((int, int)[])[(0x21, 0x7E), (0x263A, 0x263A), (0x1F600, 0x1F64F), (0x1F970, 0x1F970), (0xF0100, 0xF1500)])
            {
                for (var codePoint = first; codePoint <= last && count < 14; codePoint += first == 0xF0100 ? 0x51 : 1)
                {
                    var glyph = font.Face.MapCodePoint(codePoint);
                    if (font.Face.IsColorGlyph(glyph) || font.Face.TryGetBitmapGlyph(glyph, 11.5, out _))
                    {
                        text.Append(char.ConvertFromUtf32(codePoint));
                        count++;
                    }
                }
            }
        }
        catch (Exception exception) when (exception is InvalidImageContentException or UnsupportedImageFeatureException or ImageResourceLimitException)
        {
            return null;
        }
        catch (Exception exception)
        {
            return FuzzFailure.FromException("color-read", exception);
        }

        if (count == 0)
            return null;

        TextLayout layout;
        try
        {
            layout = TextLayout.Create(text.ToString(), new TextStyle(font, 11.5) { ColorPaletteIndex = count & 1 }, new TextLayoutOptions { MaxWidth = 60 });
        }
        catch (Exception exception) when (exception is InvalidImageContentException or UnsupportedImageFeatureException or ImageResourceLimitException)
        {
            return null;
        }
        catch (Exception exception)
        {
            return FuzzFailure.FromException("color-layout", exception);
        }

        if (!double.IsFinite(layout.InkBounds.Width) || !double.IsFinite(layout.InkBounds.Height))
            return FuzzFailure.Property("color-bounds", $"The layout of color glyphs has the ink bounds {layout.InkBounds}.");

        try
        {
            using var image = new Image<Rgba32>(64, 48);
            image.Draw((layout, TextBrush, Gradient: ColorGradient, Path: ColorPath), static (canvas, state) =>
            {
                canvas.FillText(state.TextBrush, state.layout, new PointD(0.5, 1.25));
                canvas.FillText(state.Gradient, state.layout, state.Path);
            });
        }
        catch (Exception exception)
        {
            return FuzzFailure.FromException("color-draw", exception);
        }

        return null;
    }

    private static string Report(string name, byte[] input, FuzzFailure failure)
    {
        var minimized = failure.Signature == "property:hang" ? input : FuzzMinimizer.Minimize(input, failure.Signature, candidate => RunWithWatchdog(candidate, out _));
        var minimizedFailure = RunWithWatchdog(minimized, out _) ?? failure;
        var directory = TestArtifacts.GetDirectory("fuzz-font");
        var fileName = TestArtifacts.SanitizeFileName(failure.Signature);
        if (fileName.Length > 80)
        {
            fileName = fileName[..80];
        }

        var path = directory / (fileName + ".font");
        File.WriteAllBytes(path, minimized);
        File.WriteAllBytes(directory / (fileName + ".original.font"), input);
        return string.Create(CultureInfo.InvariantCulture, $"""
            [{failure.Signature}] {name} ({input.Length} bytes, minimized to {minimized.Length} bytes: {path})
            {minimizedFailure.Message}
            minimized (base64): {(minimized.Length <= 4096 ? Convert.ToBase64String(minimized) : "(see the file)")}
            """);
    }

    private readonly record struct CheckResult(string Outcome, int DecodedGlyphs, int RejectedGlyphs);

    /// <summary>Checks the structure of an outline: contours that are opened, drawn and closed, with coordinates in range.</summary>
    private sealed class CheckingSink : OutlineSink
    {
        private bool _isOpen;
        private string? _defect;

        public override void MoveTo(double x, double y)
        {
            if (_isOpen)
            {
                _defect ??= "a contour starts inside another one";
            }

            _isOpen = true;
            Check(x, y);
        }

        public override void LineTo(double x, double y)
        {
            EnsureOpen();
            Check(x, y);
        }

        public override void QuadraticTo(double controlX, double controlY, double x, double y)
        {
            EnsureOpen();
            Check(controlX, controlY);
            Check(x, y);
        }

        public override void CubicTo(double control1X, double control1Y, double control2X, double control2Y, double x, double y)
        {
            EnsureOpen();
            Check(control1X, control1Y);
            Check(control2X, control2Y);
            Check(x, y);
        }

        public override void Close()
        {
            EnsureOpen();
            _isOpen = false;
        }

        /// <summary>Gets the first defect of the outline, or <see langword="null"/>.</summary>
        public string? Finish() => _defect ?? (_isOpen ? "an outline ends inside a contour" : null);

        private void EnsureOpen()
        {
            if (!_isOpen)
            {
                _defect ??= "a segment is outside a contour";
            }
        }

        private void Check(double x, double y)
        {
            if (!(Math.Abs(x) <= FontErrors.MaxCoordinate && Math.Abs(y) <= FontErrors.MaxCoordinate))
            {
                _defect ??= "a coordinate is out of range";
            }
        }
    }
}
