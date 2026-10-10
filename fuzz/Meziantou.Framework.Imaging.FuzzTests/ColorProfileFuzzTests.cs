using System.Buffers.Binary;
using System.Diagnostics;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Pixels;
using Meziantou.Framework.Imaging.Tests;

namespace Meziantou.Framework.Imaging.FuzzTests;

/// <summary>
/// Deterministic, bounded mutation fuzzing of ICC profiles: the profiles of the golden corpus, the profiles provided by the
/// library and the synthetic profiles of the unit tests (every supported tag type) are mutated (<see cref="FuzzMutator"/>,
/// with the size field of the header fixed up most of the time so that the tags are reached), then used as the source and
/// as the destination of a color conversion. A profile is untrusted input: creating the transform may only fail with
/// <see cref="InvalidImageContentException"/> or <see cref="UnsupportedImageFeatureException"/>, a created transform converts
/// every sample type without exception and gives samples in range, and nothing hangs. The budget is set by
/// <see cref="FuzzSettings"/>.
/// </summary>
public sealed class ColorProfileFuzzTests
{
    private static readonly IccRenderingIntent[] Intents = [IccRenderingIntent.Perceptual, IccRenderingIntent.RelativeColorimetric, IccRenderingIntent.Saturation, IccRenderingIntent.AbsoluteColorimetric];

    [Fact]
    public void MutatedProfilesFailOnlyWithDocumentedErrors()
    {
        var seeds = GetSeeds();
        var spliceSources = seeds.Select(seed => seed.Data).ToList();
        var random = new FuzzRandom(FuzzSettings.Seed ^ FuzzSeeds.GetGroupSalt("icc"));
        var failures = new Dictionary<string, string>(StringComparer.Ordinal);
        var outcomes = new Dictionary<string, int>(StringComparer.Ordinal);
        var stopwatch = Stopwatch.StartNew();
        var slowest = TimeSpan.Zero;
        var executed = 0;

        // The unchanged seeds first (every intent, with and without black point compensation), then the mutations
        var cases = seeds.SelectMany(seed => Enumerable.Range(0, 8).Select(variant => (Name: seed.Id, Data: (byte[]?)seed.Data, Variant: variant)))
            .Concat(Enumerable.Range(0, FuzzSettings.Iterations).Select(i => (Name: $"{seeds[i % seeds.Count].Id} mutation {i}", Data: (byte[]?)null, Variant: 0)));
        var iteration = 0;
        foreach (var (name, original, fixedVariant) in cases)
        {
            if (FuzzSettings.Duration is { } budget && stopwatch.Elapsed > budget)
                break;

            byte[] input;
            int variant;
            if (original is null)
            {
                input = FuzzMutator.Mutate(random, seeds[iteration % seeds.Count].Data, spliceSources);
                if (input.Length >= 4 && random.Chance(90))
                {
                    // The header declares the profile size: without this fix-up every length-changing mutation stops there
                    BinaryPrimitives.WriteUInt32BigEndian(input, (uint)input.Length);
                }

                variant = random.Next(256);
                iteration++;
            }
            else
            {
                input = original;
                variant = fixedVariant;
            }

            var started = stopwatch.Elapsed;
            var failure = RunWithWatchdog(input, variant, out var outcome);
            var elapsed = stopwatch.Elapsed - started;
            slowest = elapsed > slowest ? elapsed : slowest;
            executed++;
            outcomes[outcome] = outcomes.GetValueOrDefault(outcome) + 1;
            if (failure is null || failures.ContainsKey(failure.Signature))
                continue;

            failures[failure.Signature] = Report(name, input, variant, failure);
            if (failures.Count >= 4)
                break;
        }

        TestContext.Current.TestOutputHelper?.WriteLine(FormattableString.Invariant(
            $"icc: {executed} profiles ({seeds.Count} seeds, base seed {FuzzSettings.Seed}) in {stopwatch.Elapsed.TotalSeconds:F1} s, slowest {slowest.TotalMilliseconds:F0} ms; outcomes: {string.Join(", ", outcomes.OrderByDescending(pair => pair.Value).Select(pair => $"{pair.Key}={pair.Value}"))}"));
        if (failures.Count > 0)
            Assert.Fail($"{failures.Count} distinct fuzz failure(s):{Environment.NewLine}{string.Join(Environment.NewLine + Environment.NewLine, failures.Values)}");

        // The campaign must exercise the conversions, not only the rejection of the header: every seed converts in both
        // directions or is an input profile, and a share of the mutations still converts
        Assert.True(outcomes.GetValueOrDefault("converted") > seeds.Count * 8, "Too few mutated or seed profiles were converted.");
        Assert.True(outcomes.GetValueOrDefault(nameof(InvalidImageContentException)) > 0, "No mutated profile was rejected as invalid.");
    }

    [Fact]
    public void OracleReportsUndocumentedExceptionsAndSamplesOutOfRange()
    {
        // The self-test of the oracle: a valid profile passes, and a deliberate defect is reported with its signature
        Assert.Null(Check(IccProfile.Srgb.Data.ToArray(), variant: 3, out var outcome));
        Assert.Equal("converted", outcome);
        Assert.Null(Check([1, 2, 3], variant: 0, out outcome));
        Assert.Equal(nameof(InvalidImageContentException), outcome);

        Assert.Equal("property:float-range", CheckSamples(IccProfile.Srgb, IccProfile.Srgb, static (_, destination) => destination.Fill(float.NaN))?.Signature);
        Assert.StartsWith("exception:convert-single:System.InvalidOperationException:", CheckSamples(IccProfile.Srgb, IccProfile.Srgb, static (_, _) => throw new InvalidOperationException())!.Signature, StringComparison.Ordinal);
    }

    private static List<FuzzSeed> GetSeeds()
    {
        var root = FixtureRoot.GetDirectory();
        var manifest = FixtureManifestLoader.Load(root / FixtureRoot.ManifestFileName);
        List<FuzzSeed> seeds = [.. (manifest.ColorProfiles ?? []).Select(profile => new FuzzSeed(profile.Id, File.ReadAllBytes(root / profile.File.Path)))];
        seeds.AddRange(new (string Id, IccProfile Profile)[]
        {
            ("builtin/srgb", IccProfile.Srgb),
            ("builtin/srgb-gray", IccProfile.SrgbGray),
            ("synthetic/rgb-lab-lut8", IccTestProfiles.RgbLabLut8()),
            ("synthetic/rgb-xyz-lut16", IccTestProfiles.RgbXyzLut16()),
            ("synthetic/cmyk-lab-lut16", IccTestProfiles.CmykLabLut16()),
            ("synthetic/gray-lab-lut8", IccTestProfiles.GrayLabLut8()),
            ("synthetic/rgb-lab-mab", IccTestProfiles.RgbLabLutAToB()),
            ("synthetic/rgb-xyz-mab", IccTestProfiles.RgbXyzMatrixLutAToB()),
            ("synthetic/cmyk-lab-mab", IccTestProfiles.CmykLabLutAToB()),
            ("synthetic/gray-paper", IccTestProfiles.GrayPaper()),
            ("synthetic/rgb-scanner", IccTestProfiles.RgbScanner()),
        }.Select(seed => new FuzzSeed(seed.Id, seed.Profile.Data.ToArray())));
        if (seeds.Count < 15)
            throw new InvalidOperationException("The corpus has no color profile.");

        return seeds;
    }

    private static FuzzFailure? RunWithWatchdog(byte[] input, int variant, out string outcome)
    {
        string? caseOutcome = null;
        var task = Task.Run(() =>
        {
            var failure = Check(input, variant, out var result);
            caseOutcome = result;
            return failure;
        });
        if (!task.Wait(FuzzSettings.CaseTimeout))
        {
            outcome = "hang";
            return FuzzFailure.Property("hang", $"The profile was not processed within {FuzzSettings.CaseTimeout.TotalSeconds} s (infinite loop or unbounded work).");
        }

        outcome = caseOutcome ?? "?";
        return task.Result;
    }

    /// <summary>Checks one profile.</summary>
    /// <param name="input">The profile bytes.</param>
    /// <param name="variant">Selects the rendering intent (bits 0-1) and black point compensation (bit 2); part of the case identity.</param>
    /// <param name="outcome">"converted" when a transform was created in at least one direction, otherwise the exception type.</param>
    private static FuzzFailure? Check(byte[] input, int variant, out string outcome)
    {
        outcome = "?";
        IccProfile profile;
        try
        {
            profile = new IccProfile(new MetadataBlob(input));
            _ = (profile.ColorSpace, profile.ProfileClass, profile.Version, profile.RenderingIntent);
        }
        catch (Exception exception)
        {
            return FuzzFailure.FromException("construct", exception);
        }

        var options = new IccColorTransformOptions { Intent = Intents[variant & 3], BlackPointCompensation = (variant & 4) != 0 };
        var other = profile.ColorSpace == IccProfileColorSpace.Gray ? IccProfile.SrgbGray : IccProfile.Srgb;
        foreach (var (source, destination) in new[] { (profile, other), (other, profile), (profile, profile) })
        {
            IccColorTransform transform;
            try
            {
                transform = IccColorTransform.Create(source, destination, options);
            }
            catch (Exception exception) when (exception is InvalidImageContentException or UnsupportedImageFeatureException)
            {
                // A malformed or unsupported profile: the documented outcomes
                outcome = outcome == "converted" ? outcome : exception.GetType().Name;
                continue;
            }
            catch (Exception exception)
            {
                return FuzzFailure.FromException("create", exception);
            }

            outcome = "converted";
            if (transform.SourceChannelCount is not (1 or 3 or 4) || transform.DestinationChannelCount is not (1 or 3 or 4))
                return FuzzFailure.Property("channel-count", $"The transform has {transform.SourceChannelCount} source and {transform.DestinationChannelCount} destination channels.");

            if (CheckSamples(source, destination, transform.Convert, transform) is { } failure)
                return failure;
        }

        return null;
    }

    private delegate void ConvertSingle(ReadOnlySpan<float> source, Span<float> destination);

    private static FuzzFailure? CheckSamples(IccProfile source, IccProfile destination, ConvertSingle convert, IccColorTransform? transform = null)
    {
        transform ??= IccColorTransform.Create(source, destination);
        var sourceChannels = transform.SourceChannelCount;
        var destinationChannels = transform.DestinationChannelCount;

        // Both ends, a mid value and a few arbitrary colors, for every sample type
        const int Colors = 24;
        var floats = new float[Colors * sourceChannels];
        var bytes = new byte[floats.Length];
        var words = new ushort[floats.Length];
        var state = 0x9E3779B9u;
        for (var i = 0; i < floats.Length; i++)
        {
            state = (state * 1664525) + 1013904223;
            var value = (i / sourceChannels) switch
            {
                0 => 0f,
                1 => 1f,
                2 => 0.5f,
                _ => (state >> 8) / (float)(1 << 24),
            };
            floats[i] = value;
            bytes[i] = (byte)(value * 255);
            words[i] = (ushort)(value * 65535);
        }

        var converted = new float[Colors * destinationChannels];
        try
        {
            convert(floats, converted);
        }
        catch (Exception exception)
        {
            return FuzzFailure.FromException("convert-single", exception);
        }

        if (converted.Any(static value => !(value >= 0f && value <= 1f)))
            return FuzzFailure.Property("float-range", "A converted floating-point sample is not in [0, 1].");

        try
        {
            transform.Convert(bytes, new byte[Colors * destinationChannels]);
            transform.Convert(words, new ushort[Colors * destinationChannels]);
        }
        catch (Exception exception)
        {
            return FuzzFailure.FromException("convert-integer", exception);
        }

        return null;
    }

    private static string Report(string name, byte[] input, int variant, FuzzFailure failure)
    {
        var minimized = failure.Signature == "property:hang" ? input : FuzzMinimizer.Minimize(input, failure.Signature, candidate => RunWithWatchdog(candidate, variant, out _));
        var minimizedFailure = RunWithWatchdog(minimized, variant, out _) ?? failure;
        var directory = TestArtifacts.GetDirectory("fuzz-icc");
        var fileName = TestArtifacts.SanitizeFileName(failure.Signature);
        if (fileName.Length > 80)
        {
            fileName = fileName[..80];
        }

        var path = directory / (fileName + ".icc");
        File.WriteAllBytes(path, minimized);
        File.WriteAllBytes(directory / (fileName + ".original.icc"), input);
        return string.Create(CultureInfo.InvariantCulture, $"""
            [{failure.Signature}] {name} (variant {variant}, {input.Length} bytes, minimized to {minimized.Length} bytes: {path})
            {minimizedFailure.Message}
            minimized (base64): {Convert.ToBase64String(minimized)}
            """);
    }
}
