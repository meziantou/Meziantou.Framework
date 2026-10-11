using System.Buffers.Binary;
using System.Diagnostics;
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
            if (iteration < 0 && (failure is not null || result.Outcome != "loaded" || result.RejectedGlyphs != 0))
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
        if (seeds.Count < 20)
            throw new InvalidOperationException("The corpus has no font.");

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

        var decoded = 0;
        var rejected = 0;
        using var scratch = new GlyphScratch(AllocationScope.Create(Options.Configuration, "FontFuzz"));
        foreach (var font in collection)
        {
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
                    if (font.Face.GetAdvanceWidth(glyph) is < 0 or > ushort.MaxValue)
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

        result = new CheckResult("loaded", decoded, rejected);
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
