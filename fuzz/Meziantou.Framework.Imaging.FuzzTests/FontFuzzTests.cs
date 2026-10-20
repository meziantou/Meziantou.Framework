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
        // The bitmaps without colors of a font are read like its other tables.
        UseEmbeddedBitmaps = true,
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

    internal static FuzzFailure? RunWithWatchdog(byte[] input, out CheckResult result)
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
                catch (Exception exception) when (exception is FontException or ImageResourceLimitException)
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
                    hasScript = font.Face.MapCodePoint(Rune.GetRuneAt(text, 0).Value) != 0;
                }
                catch (Exception exception) when (exception is FontException or ImageResourceLimitException)
                {
                    continue;
                }

                var isRightToLeft = script is UnicodeScript.Arabic or UnicodeScript.Hebrew or UnicodeScript.Syriac or UnicodeScript.Nko or UnicodeScript.Adlam;
                if (hasScript && CheckShaping(font, text, script, isRightToLeft, []) is { } scriptFailure)
                    return scriptFailure;
            }
        }

        return null;
    }

    private static FuzzFailure? CheckShaping(Font font, string text, UnicodeScript script, bool isRightToLeft, ShapingFeature[] features)
    {
        var buffer = new GlyphBuffer(Options.Configuration.FontEnvironment.Limits);
        try
        {
            TextShaper.AddText(buffer, text);
            TextShaper.Shape(buffer, font.Face, script, languages: [], isRightToLeft, features);
        }
        catch (Exception exception) when (exception is FontException or ImageResourceLimitException)
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
    // a precomposed letter with a mark, the mark that stretches over a word; the vowel of Thai and Lao that is
    // decomposed, with and without marks above its consonant; the letters and syllables of Hangul, with tone marks; for
    // each Indic script, conjuncts, a reph, a vowel sign drawn before its syllable, joiners after a halant, a nukta, a
    // vowel letter followed by a vowel sign, and signs without a base; for Khmer, subscripts with the ro among them, the
    // vowels with two parts, a register shifter and what has no base; for Myanmar, a kinzi, medials, the vowel drawn
    // before the syllable, marks whose order changes, and what has no base; for scripts of the universal model, stacked
    // and subjoined consonants, a reph, vowels drawn before the cluster, marks in an order a cluster does not have,
    // joiners, what has no base, and letters that join with a variation selector or a mark between them.
    private static readonly (UnicodeScript Script, string Text)[] ScriptTexts =
    [
        (UnicodeScript.Arabic, "\u0628\u064E\u0651\u0633\u0654\u0650\u0645\u0020\u0644\u0627\u0020\u0644\u200D\u0627\u0020\u0628\u200C\u0628\u0640\u0622\u0655\u0020\u06AF\u0654\u064F"),
        (UnicodeScript.Hebrew, "\u05E9\u05BC\u05C1\u05B8\u05DC\u05D5\u05B9\u05DD\u0020\uFB2C\u05B7\u0020\u05D0\u05B7\u05B0\u05BD\u200D\u05B7"),
        (UnicodeScript.Devanagari, "\u0915\u094D\u0937\u093F \u0930\u094D\u0915\u094B\u0902 \u0915\u094D\u200D\u0924\u093F \u0915\u093C\u094D\u092F\u0941 \u0905\u093E \u093F\u094D \u0924\u094D\u0930\u094D\u092F\u093E \u0915\u094D\u200C\u0915\u094D"),
        (UnicodeScript.Bengali, "\u0995\u09CD\u09B7\u09BF \u09B0\u09CD\u0995\u09CB\u0982 \u0995\u09CD\u200D\u09A4\u09BF \u0995\u09BC\u09CD\u09AF\u09C1 \u0985\u09BE \u09BF\u09CD \u09A4\u09CD\u09B0\u09CD\u09AF\u09BE \u0995\u09CD\u200C\u0995\u09CD"),
        (UnicodeScript.Gurmukhi, "\u0A15\u0A4D\u0A37\u0A3F \u0A30\u0A4D\u0A15\u0A4B\u0A02 \u0A15\u0A4D\u200D\u0A24\u0A3F \u0A15\u0A3C\u0A4D\u0A2F\u0A41 \u0A05\u0A3E \u0A3F\u0A4D \u0A24\u0A4D\u0A30\u0A4D\u0A2F\u0A3E \u0A15\u0A4D\u200C\u0A15\u0A4D"),
        (UnicodeScript.Gujarati, "\u0A95\u0ACD\u0AB7\u0ABF \u0AB0\u0ACD\u0A95\u0ACB\u0A82 \u0A95\u0ACD\u200D\u0AA4\u0ABF \u0A95\u0ABC\u0ACD\u0AAF\u0AC1 \u0A85\u0ABE \u0ABF\u0ACD \u0AA4\u0ACD\u0AB0\u0ACD\u0AAF\u0ABE \u0A95\u0ACD\u200C\u0A95\u0ACD"),
        (UnicodeScript.Oriya, "\u0B15\u0B4D\u0B37\u0B3F \u0B30\u0B4D\u0B15\u0B4B\u0B02 \u0B15\u0B4D\u200D\u0B24\u0B3F \u0B15\u0B3C\u0B4D\u0B2F\u0B41 \u0B05\u0B3E \u0B3F\u0B4D \u0B24\u0B4D\u0B30\u0B4D\u0B2F\u0B3E \u0B15\u0B4D\u200C\u0B15\u0B4D"),
        (UnicodeScript.Tamil, "\u0B95\u0BCD\u0BB7\u0BBF \u0BB0\u0BCD\u0B95\u0BCB\u0B82 \u0B95\u0BCD\u200D\u0BA4\u0BBF \u0B95\u0BBC\u0BCD\u0BAF\u0BC1 \u0B85\u0BBE \u0BBF\u0BCD \u0BA4\u0BCD\u0BB0\u0BCD\u0BAF\u0BBE \u0B95\u0BCD\u200C\u0B95\u0BCD"),
        (UnicodeScript.Telugu, "\u0C15\u0C4D\u0C37\u0C3F \u0C30\u0C4D\u0C15\u0C4B\u0C02 \u0C15\u0C4D\u200D\u0C24\u0C3F \u0C15\u0C3C\u0C4D\u0C2F\u0C41 \u0C05\u0C3E \u0C3F\u0C4D \u0C24\u0C4D\u0C30\u0C4D\u0C2F\u0C3E \u0C15\u0C4D\u200C\u0C15\u0C4D"),
        (UnicodeScript.Kannada, "\u0C95\u0CCD\u0CB7\u0CBF \u0CB0\u0CCD\u0C95\u0CCB\u0C82 \u0C95\u0CCD\u200D\u0CA4\u0CBF \u0C95\u0CBC\u0CCD\u0CAF\u0CC1 \u0C85\u0CBE \u0CBF\u0CCD \u0CA4\u0CCD\u0CB0\u0CCD\u0CAF\u0CBE \u0C95\u0CCD\u200C\u0C95\u0CCD"),
        (UnicodeScript.Malayalam, "\u0D15\u0D4D\u0D37\u0D3F \u0D30\u0D4D\u0D15\u0D4B\u0D02 \u0D15\u0D4D\u200D\u0D24\u0D3F \u0D15\u0D3C\u0D4D\u0D2F\u0D41 \u0D05\u0D3E \u0D3F\u0D4D \u0D24\u0D4D\u0D30\u0D4D\u0D2F\u0D3E \u0D15\u0D4D\u200C\u0D15\u0D4D"),
        (UnicodeScript.Khmer, "\u179F\u17D2\u178F\u17D2\u179A\u17B8 \u1780\u17BE\u1784\u17C4 \u179F\u17CA\u17B8 \u1784\u17D2\u179A\u17D2\u1782\u17C6 \u17C1\u17D2 \u17D2\u1780 \u1780\u200C\u17D2\u1780\u17C7"),
        (UnicodeScript.Myanmar, "\u1021\u1004\u103A\u1039\u1002\u101C\u102D\u1015\u103A \u1000\u103C\u103D\u1031\u102C \u1000\u102F\u1036\u1037 \u1015\u1005\u1039\u1005\u100A\u103A\u1038 \u1031\u103A \u1039\u1000 \u1040\u102D\u200D\u1031"),
        (UnicodeScript.Tibetan, "\u0F66\u0F92\u0FB2\u0F74\u0F56\u0F0B \u0F40\u0F72\u0F71\u0F39 \u0F20\u0F3F \u0F72\u0F84 \u0F40\u200D\u0F7A\u0FC6 \u0F43\u0F73"),
        (UnicodeScript.Sinhala, "\u0DC1\u0DCA\u200D\u0DBB\u0DD3 \u0DBB\u0DCA\u200D\u0D9A\u0DDC \u0D9A\u0DD9\u0DCA \u0DD9\u0DCA \u0D85\u0DCF \u0D9A\u200C\u0DCA"),
        (UnicodeScript.Javanese, "\uA98F\uA9C0\uA9A0\uA9BA\uA9B4 \uA98F\uA9BF\uA9BA \uA98F\uA9B6\uA9BA \uA9BA\uA9C0 \uA98F\uA982\u200D\uA983"),
        (UnicodeScript.TaiTham, "\u1A20\u1A75\u1A60\u1A20\u1A6E \u1A20\u1A55\u1A6E\u1A63 \u1A60\u1A6E \u1A20\u200C\u1A60\u1A65"),
        (UnicodeScript.Mongolian, "\u182E\u1823\u1829\u182D\u1823\u182F \u182D\u180B\u1820\u180E\u1820 \u1820\u200D\u180D \u1887\u1885\u18A9 \u180B\u1820"),
        (UnicodeScript.Nko, "\u07D2\u07DE\u07CF \u07D3\u07EC\u07D3\u07F2\u07FA \u07D3\u200D\u07C1\u07D3 \u07EC\u200C\u07D3"),
        (UnicodeScript.Adlam, "\U0001E900\U0001E923\U0001E924\U0001E922\U0001E925 \U0001E922\U0001E944\U0001E94A\U0001E922 \U0001E94B\u0640\U0001E922\u200D \U0001E944"),
        (UnicodeScript.Kaithi, "\U000110A9\U000110B9\U0001108D\U000110B1 \U0001108D\U000110B9\u200D\U000110A9 \U000110BD\u0967 \U000110B1\U000110B9 \U0001108D\U000110BA\U00011081\U000110B3"),
        (UnicodeScript.Thai, "\u0E01\u0E48\u0E33 \u0E19\u0E49\u0E33\u0E33 \u0E1B\u0E38\u0E3A\u0E48 \u0E33"),
        (UnicodeScript.Lao, "\u0E81\u0EC8\u0EB3 \u0E99\u0EC9\u0EB3 \u0EAB\u0EBC\u0EB8\u0EC8 \u0EB3"),
        (UnicodeScript.Hangul, "\u1100\u1161\u11A8 \uAC00\u11A8\u302E \u1113\u1161\u11C3\u302F \u302E \uAC01\u11EB\u115F\u1160"),
        (UnicodeScript.Syriac, "\u0712\u0710\u0712\u0020\u0715\u0710\u0020\u070F\u072B\u0718\u0712\u0730\u0020\u0717\u200D\u0710\u0710"),
    ];

    /// <summary>
    /// Lays out a text with each font and draws it: a layout is refused with a documented exception or is created, and
    /// a layout that was created is drawn, filled, stroked and converted to a path, without any exception.
    /// </summary>
    private static FuzzFailure? CheckText(List<Font> collection, bool withStroke)
    {
        // Each font as it is, then slanted and thickened, in a language, at the sizes the bitmaps of the corpus are made for.
        var styles = new List<TextStyle>();
        foreach (var font in collection)
        {
            styles.Add(new TextStyle(font, 13.5));
            styles.Add(new TextStyle(font, 16) { Synthesis = FontSynthesis.Bold | FontSynthesis.Oblique, Language = "sr" });
            styles.Add(new TextStyle(font, 12) { Synthesis = FontSynthesis.Bold, Language = "zh-Hant" });
        }

        foreach (var style in styles)
        {
            var font = style.Fonts.Primary;
            TextLayout layout;
            try
            {
                layout = TextLayout.Create(SampleText, style);
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
        catch (Exception exception) when (exception is FontException or ImageResourceLimitException)
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

    internal readonly record struct CheckResult(string Outcome, int DecodedGlyphs, int RejectedGlyphs);

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
