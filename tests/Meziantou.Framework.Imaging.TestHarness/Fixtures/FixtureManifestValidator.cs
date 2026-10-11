using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.TestHarness.Fixtures;

/// <summary>
/// Validates a manifest against the corpus directory before any pixel comparison: schema version, identifiers, kinds,
/// provenance and licenses, file existence (inside the corpus root only), SHA-256 hashes, orphan files, expected
/// frame/metadata consistency (frame counts, rational durations, loop-count conventions, posters), raw-buffer layouts and
/// exact lengths (with bounded decompression), comparison policies (including proof that each tolerance rejects channel
/// swaps and flips of its reference), reference provenance and expected errors.
/// </summary>
public static partial class FixtureManifestValidator
{
    /// <summary>The SPDX identifiers accepted for fixture content (see tests/Meziantou.Framework.Imaging.Fixtures/README.md).</summary>
    public static IReadOnlySet<string> AllowedLicenses { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "CC0-1.0",
        "MIT",
        "Unlicense",
        "CC-BY-4.0",
        "Libpng",
        "Zlib",
    };

    /// <summary>The accepted values of <see cref="FixtureProvenance.Origin"/>.</summary>
    public static IReadOnlySet<string> AllowedOrigins { get; } = new HashSet<string>(StringComparer.Ordinal) { "hand-authored", "generated", "external" };

    /// <summary>The accepted values of <see cref="FixtureEntry.Kind"/>.</summary>
    public static IReadOnlySet<string> AllowedKinds => FixtureKinds.All;

    /// <summary>The accepted values of <see cref="FixtureEntry.Format"/> (container formats; APNG is <c>png</c>).</summary>
    public static IReadOnlySet<string> AllowedFormats { get; } = new HashSet<string>(StringComparer.Ordinal) { "png", "gif", "jpeg", "webp", "qoi", "bmp", "tga", "pnm" };

    /// <summary>The accepted values of <see cref="FixtureExpectation.PixelFormat"/> (library <c>PixelFormat</c> names).</summary>
    public static IReadOnlySet<string> AllowedPixelFormats { get; } = new HashSet<string>(StringComparer.Ordinal) { "Rgba32", "Bgra32", "Rgb24", "Rgba64", "Gray8", "Gray16" };

    /// <summary>The accepted values of <see cref="FixtureExpectation.ColorModel"/> (library <c>ImageColorModel</c> names).</summary>
    public static IReadOnlySet<string> AllowedColorModels { get; } = new HashSet<string>(StringComparer.Ordinal) { "Grayscale", "GrayscaleAlpha", "Rgb", "Rgba", "Indexed", "YCbCr" };

    /// <summary>The accepted values of <see cref="FixtureExpectation.IccProfile"/>.</summary>
    public static IReadOnlySet<string> AllowedIccProfiles { get; } = new HashSet<string>(StringComparer.Ordinal) { "none", "rgb", "gray" };

    /// <summary>The accepted values of <see cref="FixtureExpectation.TransferFunction"/>.</summary>
    public static IReadOnlySet<string> AllowedTransferFunctions { get; } = new HashSet<string>(StringComparer.Ordinal) { "srgb", "linear" };

    /// <summary>The accepted values of <see cref="EncodedResolution.Unit"/>.</summary>
    public static IReadOnlySet<string> AllowedResolutionUnits { get; } = new HashSet<string>(StringComparer.Ordinal) { ResolutionExpectation.Meter, ResolutionExpectation.Inch, ResolutionExpectation.Centimeter };

    /// <summary>The accepted keys of <see cref="DecodeOptionsDefinition.Limits"/> (library <c>ImageResourceLimits</c> properties).</summary>
    public static IReadOnlySet<string> AllowedLimits { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "MaxWidth", "MaxHeight", "MaxFramePixels", "MaxFrames", "MaxTotalPixels", "MaxEncodedBytes", "MaxMetadataBytes", "MaxLiveAllocationBytes",
    };

    /// <summary>The accepted values of <see cref="FixtureCrossCheck.Result"/>.</summary>
    public static IReadOnlySet<string> AllowedCrossCheckResults { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        FixtureCrossCheck.Exact, FixtureCrossCheck.Equivalent, FixtureCrossCheck.WithinTolerance, FixtureCrossCheck.Differs,
    };

    /// <summary>Files of the corpus root that are not fixtures and need not be referenced by the manifest.</summary>
    public static IReadOnlySet<string> InfrastructureFiles { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        FixtureRoot.ManifestFileName,
        FixtureRoot.SchemaFileName,
        "README.md",
    };

    /// <summary>Validates a manifest.</summary>
    /// <param name="manifest">The manifest.</param>
    /// <param name="rootDirectory">The corpus root directory.</param>
    /// <returns>The list of errors, empty when the manifest is valid. Each error names the fixture and the problem.</returns>
    public static IReadOnlyList<string> Validate(FixtureManifest manifest, FullPath rootDirectory)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var errors = new List<string>();
        if (manifest.SchemaVersion != FixtureManifest.CurrentSchemaVersion)
        {
            errors.Add($"Unsupported schemaVersion {manifest.SchemaVersion}; expected {FixtureManifest.CurrentSchemaVersion}.");
            return errors;
        }

        var root = rootDirectory;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var referencedFiles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in manifest.Fixtures)
        {
            var name = $"Fixture '{entry.Id}'";
            if (!IdRegex().IsMatch(entry.Id))
            {
                errors.Add($"{name}: invalid id (allowed: lowercase letters, digits, '-', '_', '.', '/').");
            }

            if (!ids.Add(entry.Id))
            {
                errors.Add($"{name}: duplicate id.");
            }

            if (!AllowedFormats.Contains(entry.Format))
            {
                errors.Add($"{name}: invalid format '{entry.Format}' (allowed: {string.Join(", ", AllowedFormats)}).");
            }

            if (entry.Features is { } features)
            {
                if (features.Any(string.IsNullOrWhiteSpace) || features.Distinct(StringComparer.Ordinal).Count() != features.Count)
                {
                    errors.Add($"{name}: features must be non-empty and unique.");
                }
            }

            ValidateProvenance(name, entry.Provenance, errors);
            ValidateFile(name, entry.Input, root, referencedFiles, errors);
            ValidateDecodeOptions(name, entry.DecodeOptions, errors);

            if (!AllowedKinds.Contains(entry.Kind))
            {
                errors.Add($"{name}: invalid kind '{entry.Kind}' (allowed: {string.Join(", ", AllowedKinds)}).");
            }
            else if (entry.Kind == FixtureKinds.Valid)
            {
                ValidateValidEntry(name, entry, root, referencedFiles, errors);
            }
            else
            {
                ValidateErrorEntry(name, entry, errors);
            }
        }

        ValidateColorSections(manifest, root, ids, referencedFiles, errors);

        if (Directory.Exists(root))
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var relative = FullPath.FromPath(file).MakePathRelativeTo(root).Replace('\\', '/');
                if (InfrastructureFiles.Contains(relative) || relative.EndsWith("/README.md", StringComparison.Ordinal) || relative.StartsWith("LICENSES/", StringComparison.Ordinal))
                    continue;

                if (!referencedFiles.Contains(relative))
                {
                    errors.Add($"File '{relative}' is not referenced by the manifest. Every committed fixture must have provenance and license information.");
                }
            }
        }
        else
        {
            errors.Add($"The fixture root '{root}' does not exist.");
        }

        return errors;
    }

    /// <summary>Computes the lowercase hexadecimal SHA-256 of a file.</summary>
    /// <param name="path">The file path.</param>
    /// <returns>The hash.</returns>
    public static string ComputeSha256(FullPath path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>
    /// Verifies that a policy rejects channel swaps and flips of a reference, so that it cannot mask them. Defects that
    /// leave the reference unchanged (gray content, symmetric pattern) are not reported since they cannot produce wrong pixels.
    /// </summary>
    /// <param name="reference">The reference buffer.</param>
    /// <param name="policy">The policy.</param>
    /// <returns>The undetectable defects; empty when the policy discriminates them all.</returns>
    public static IReadOnlyList<string> FindUndetectableDefects(RawPixelBuffer reference, ComparisonPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(policy);
        var defects = new List<(string Name, Func<RawPixelBuffer> Transform)>();
        if (reference.Layout.IsColor)
        {
            defects.Add(("R/B channel swap", () => reference.SwapChannels(0, 2)));
            defects.Add(("R/G channel swap", () => reference.SwapChannels(0, 1)));
            defects.Add(("G/B channel swap", () => reference.SwapChannels(1, 2)));
        }

        if (reference.Height > 1)
        {
            defects.Add(("vertical flip", reference.FlipVertical));
        }

        if (reference.Width > 1)
        {
            defects.Add(("horizontal flip", reference.FlipHorizontal));
        }

        var result = new List<string>();
        foreach (var (defectName, transform) in defects)
        {
            // A defect that leaves the reference unchanged (gray content, symmetric pattern) cannot produce wrong pixels
            var defective = transform();
            if (!defective.Span.SequenceEqual(reference.Span) && PixelBufferComparer.Matches(reference, defective, policy))
            {
                result.Add(defectName);
            }
        }

        return result;
    }

    private static void ValidateValidEntry(string name, FixtureEntry entry, FullPath root, HashSet<string> referencedFiles, List<string> errors)
    {
        if (entry.ExpectedError is not null)
        {
            errors.Add($"{name}: valid fixtures must not declare 'expectedError'.");
        }

        if (entry.Reference is null)
        {
            errors.Add($"{name}: valid fixtures must declare 'reference' (how the expected pixels were established).");
        }
        else
        {
            ValidateReference(name, entry.Reference, errors);
        }

        ComparisonPolicy? policy = null;
        if (entry.Comparison is null)
        {
            errors.Add($"{name}: valid fixtures must declare 'comparison'.");
        }
        else
        {
            var policyErrors = ComparisonPolicy.Validate(entry.Comparison);
            errors.AddRange(policyErrors.Select(error => $"{name}: {error}"));
            if (policyErrors.Count == 0)
            {
                policy = ComparisonPolicy.FromDefinition(entry.Comparison);
            }
        }

        if (policy is { IsExact: false })
        {
            ValidateToleranceJustification(name, entry.Reference, policy, errors);
        }

        if (entry.Expected is null)
        {
            errors.Add($"{name}: valid fixtures must declare 'expected'.");
            return;
        }

        ValidateExpectation(name, entry, entry.Expected, policy, root, referencedFiles, errors);
    }

    private static void ValidateToleranceJustification(string name, FixtureReference? reference, ComparisonPolicy policy, List<string> errors)
    {
        var measured = (reference?.CrossChecks ?? [])
            .Where(check => check.ExcludedFromTolerance != true && check.MaxAbsoluteError is not null && check.MeanAbsoluteError is not null)
            .ToList();
        if (measured.Count == 0 || measured.All(check => check.MaxAbsoluteError == 0))
        {
            errors.Add($"{name}: a tolerance policy must be justified by at least one measured cross-check with another independent decoder showing nonzero differences; lossless decoding is compared exactly.");
            return;
        }

        var (maxError, meanError) = ComparisonPolicy.GetMaximumJustifiedTolerance(measured.Max(check => check.MaxAbsoluteError!.Value), measured.Max(check => check.MeanAbsoluteError!.Value));
        if (policy.MaxAbsoluteError > maxError || policy.MaxMeanAbsoluteError > meanError)
        {
            errors.Add(string.Create(CultureInfo.InvariantCulture, $"{name}: the tolerance (max {policy.MaxAbsoluteError}, mean {policy.MaxMeanAbsoluteError}) is looser than the measured disagreement justifies (max {maxError}, mean {meanError}); tolerances stay narrow."));
        }
    }

    private static void ValidateErrorEntry(string name, FixtureEntry entry, List<string> errors)
    {
        if (entry.Expected is not null || entry.Comparison is not null)
        {
            errors.Add($"{name}: {entry.Kind} fixtures must not declare expected pixels or a comparison policy; fabricated expected pixels are not allowed.");
        }

        if (entry.ExpectedError is null || string.IsNullOrWhiteSpace(entry.ExpectedError.Exception))
        {
            errors.Add($"{name}: {entry.Kind} fixtures must declare 'expectedError' with an exception type name; fabricated expected pixels are not allowed.");
            return;
        }

        var expected = entry.Kind switch
        {
            FixtureKinds.Unsupported => "UnsupportedImageFeatureException",
            FixtureKinds.Limit => "ImageResourceLimitException",
            _ => null,
        };
        if (expected is not null && entry.ExpectedError.Exception != expected)
        {
            errors.Add($"{name}: {entry.Kind} fixtures must expect {expected}, not {entry.ExpectedError.Exception}.");
        }

        if (entry.Kind == FixtureKinds.Limit)
        {
            if (entry.DecodeOptions?.Limits is not { Count: > 0 })
            {
                errors.Add($"{name}: limit fixtures must declare decodeOptions.limits.");
            }

            if (string.IsNullOrEmpty(entry.ExpectedError.LimitKind))
            {
                errors.Add($"{name}: limit fixtures must declare expectedError.limitKind.");
            }
        }
        else if (entry.ExpectedError.LimitKind is not null)
        {
            errors.Add($"{name}: only limit fixtures may declare expectedError.limitKind.");
        }
    }

    private static void ValidateDecodeOptions(string name, DecodeOptionsDefinition? options, List<string> errors)
    {
        if (options is null)
            return;

        foreach (var (key, value) in options.Limits ?? new Dictionary<string, long>(StringComparer.Ordinal))
        {
            if (!AllowedLimits.Contains(key))
            {
                errors.Add($"{name}: unknown resource limit '{key}' (allowed: {string.Join(", ", AllowedLimits)}).");
            }

            if (value <= 0)
            {
                errors.Add($"{name}: resource limit '{key}' must be positive.");
            }
        }

        if (options.FrameLimit is <= 0)
        {
            errors.Add($"{name}: decodeOptions.frameLimit must be positive.");
        }
    }

    private static void ValidateReference(string name, FixtureReference reference, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(reference.Description))
        {
            errors.Add($"{name}: reference.description is required.");
        }

        switch (reference.Method)
        {
            case FixtureReference.HandComputed:
                break;
            case FixtureReference.Decoded:
                if (string.IsNullOrWhiteSpace(reference.Decoder) || string.IsNullOrWhiteSpace(reference.Backend) || string.IsNullOrWhiteSpace(reference.Command))
                {
                    errors.Add($"{name}: decoded references must declare the decoder (tool and version), the actual backend and the exact command.");
                }

                break;
            default:
                errors.Add($"{name}: invalid reference method '{reference.Method}' (allowed: {FixtureReference.HandComputed}, {FixtureReference.Decoded}).");
                break;
        }

        foreach (var check in reference.CrossChecks ?? [])
        {
            if (string.IsNullOrWhiteSpace(check.Decoder))
            {
                errors.Add($"{name}: cross-checks must name the decoder.");
            }

            if (!AllowedCrossCheckResults.Contains(check.Result))
            {
                errors.Add($"{name}: invalid cross-check result '{check.Result}' (allowed: {string.Join(", ", AllowedCrossCheckResults)}).");
            }

            if ((check.Result is FixtureCrossCheck.Differs or FixtureCrossCheck.Equivalent || check.ExcludedFromTolerance == true) && string.IsNullOrWhiteSpace(check.Notes))
            {
                errors.Add($"{name}: cross-check with {check.Decoder} must explain its '{check.Result}' result in notes; disagreements are documented, never hidden.");
            }
        }
    }

    private static void ValidateExpectation(string name, FixtureEntry entry, FixtureExpectation expected, ComparisonPolicy? policy, FullPath root, HashSet<string> referencedFiles, List<string> errors)
    {
        if (expected.Width <= 0 || expected.Height <= 0)
        {
            errors.Add($"{name}: expected dimensions must be positive.");
            return;
        }

        if (!AllowedPixelFormats.Contains(expected.PixelFormat))
        {
            errors.Add($"{name}: invalid pixelFormat '{expected.PixelFormat}' (allowed: {string.Join(", ", AllowedPixelFormats)}).");
        }

        if (expected.ColorModel is not null && !AllowedColorModels.Contains(expected.ColorModel))
        {
            errors.Add($"{name}: invalid colorModel '{expected.ColorModel}' (allowed: {string.Join(", ", AllowedColorModels)}).");
        }

        if (expected.BitsPerComponent is not null and not (1 or 2 or 4 or 8 or 16))
        {
            errors.Add($"{name}: invalid bitsPerComponent {expected.BitsPerComponent}.");
        }

        if (expected.Orientation is < 1 or > 8)
        {
            errors.Add($"{name}: orientation must be an EXIF orientation value (1-8).");
        }

        if (!AllowedIccProfiles.Contains(expected.IccProfile))
        {
            errors.Add($"{name}: invalid iccProfile '{expected.IccProfile}' (allowed: {string.Join(", ", AllowedIccProfiles)}).");
        }

        if (expected.TransferFunction is { } transfer && !AllowedTransferFunctions.Contains(transfer))
        {
            errors.Add($"{name}: invalid transferFunction '{transfer}' (allowed: {string.Join(", ", AllowedTransferFunctions)}).");
        }
        else if (expected.EffectiveTransferFunction == "linear" && entry.Format != "qoi")
        {
            errors.Add($"{name}: only QOI inputs declare linear samples (colorspace 1).");
        }

        if (expected.Frames.Count == 0)
        {
            errors.Add($"{name}: an image has at least one displayed frame.");
        }

        if (expected.FrameCount != expected.Frames.Count)
        {
            errors.Add($"{name}: frameCount {expected.FrameCount} does not match the {expected.Frames.Count} expected frames.");
        }

        ValidateAnimation(name, entry, expected, errors);
        ValidateMetadata(name, entry, expected, errors);

        HashSet<string>? layouts = null;
        var buffers = new List<(string Frame, RawPixelBuffer Buffer)>();
        for (var i = 0; i < expected.Frames.Count; i++)
        {
            var frame = expected.Frames[i];
            var frameName = $"{name} frame {i.ToString(CultureInfo.InvariantCulture)}";
            if (!RationalDuration.TryParse(frame.Duration, out var duration))
            {
                errors.Add($"{frameName}: duration '{frame.Duration}' must be an exact non-negative rational 'numerator/denominator' with a positive denominator.");
            }
            else
            {
                ValidateEncodedDelay(frameName, entry, expected, frame, duration, errors);
            }

            ValidateBuffers(frameName, frame, expected, root, referencedFiles, ref layouts, buffers, errors);
        }

        if (expected.Poster is { } poster)
        {
            if (poster.Duration is not null)
            {
                errors.Add($"{name} poster: a poster is not part of the animation and has no duration.");
            }

            ValidateBuffers($"{name} poster", poster, expected, root, referencedFiles, ref layouts, buffers, errors);
        }

        if (layouts is not null && expected.PixelFormat is "Rgba64" or "Gray16" && !layouts.Any(layout => RawPixelLayout.TryParse(layout, out var parsed) && parsed.BytesPerSample == 2))
        {
            errors.Add($"{name}: 16-bit fixtures ({expected.PixelFormat}) must provide a 16-bit reference layout so low bits are compared exactly.");
        }

        if (policy is { IsExact: false })
        {
            foreach (var (frame, buffer) in buffers)
            {
                var undetectable = FindUndetectableDefects(buffer, policy);
                if (undetectable.Count > 0)
                {
                    errors.Add($"{frame} ({buffer.Layout}): the tolerance policy cannot detect {string.Join(", ", undetectable)} of this reference; tighten the policy or use a more discriminating pattern.");
                }
            }
        }
    }

    // Independent timing conventions: GIF hundredths d -> d/100; APNG num/den with den 0 -> num/100;
    // WebP ANMF milliseconds m -> m/1000
    private static void ValidateEncodedDelay(string frameName, FixtureEntry entry, FixtureExpectation expected, FrameExpectation frame, RationalDuration duration, List<string> errors)
    {
        if (frame.EncodedDelay is not { } delay)
        {
            if (expected.Animation is not null && (entry.Format is "png" or "webp" || duration != RationalDuration.Zero))
            {
                errors.Add($"{frameName}: frames of animated inputs must record the encoded delay field (encodedDelay).");
            }

            return;
        }

        RationalDuration expectedDuration;
        if (delay.IsApng && entry.Format == "png")
        {
            if (delay.Numerator is < 0 or > ushort.MaxValue || delay.Denominator is < 0 or > ushort.MaxValue)
            {
                errors.Add($"{frameName}: encoded APNG delay fields are 16-bit values.");
                return;
            }

            expectedDuration = RationalDuration.Create(delay.Numerator!.Value, delay.Denominator == 0 ? 100 : delay.Denominator!.Value);
        }
        else if (delay.IsGif && entry.Format == "gif")
        {
            if (delay.Hundredths is < 0 or > ushort.MaxValue)
            {
                errors.Add($"{frameName}: the encoded GIF delay is a 16-bit value.");
                return;
            }

            expectedDuration = RationalDuration.Create(delay.Hundredths!.Value, 100);
        }
        else if (delay.IsWebP && entry.Format == "webp")
        {
            if (delay.Milliseconds is < 0 or > 0xFFFFFF)
            {
                errors.Add($"{frameName}: the encoded WebP frame duration is a 24-bit value.");
                return;
            }

            expectedDuration = RationalDuration.Create(delay.Milliseconds!.Value, 1000);
        }
        else
        {
            errors.Add($"{frameName}: encodedDelay must be {{numerator, denominator}} for APNG, {{hundredths}} for GIF or {{milliseconds}} for WebP, matching the fixture format '{entry.Format}'.");
            return;
        }

        if (expectedDuration != duration)
        {
            errors.Add($"{frameName}: encoded delay {delay} means {expectedDuration} s, not {duration} s.");
        }
    }

    private static void ValidateMetadata(string name, FixtureEntry entry, FixtureExpectation expected, List<string> errors)
    {
        if (expected.Resolution is { } resolution)
        {
            var encoded = resolution.Encoded;
            if (!AllowedResolutionUnits.Contains(encoded.Unit) || encoded.X <= 0 || encoded.Y <= 0)
            {
                errors.Add($"{name}: resolution.encoded must have a unit in ({string.Join(", ", AllowedResolutionUnits)}) and positive densities.");
            }
            else
            {
                var allowedUnits = entry.Format switch
                {
                    "png" or "bmp" => [ResolutionExpectation.Meter],
                    "jpeg" => new[] { ResolutionExpectation.Inch, ResolutionExpectation.Centimeter },
                    _ => [],
                };
                if (!allowedUnits.Contains(encoded.Unit, StringComparer.Ordinal))
                {
                    errors.Add($"{name}: a {entry.Format} input cannot encode a resolution in '{encoded.Unit}'.");
                }

                // Exact IEEE conversion of the encoded integers: no tolerance
                if (resolution.HorizontalDpi != ResolutionExpectation.ToDpi(encoded.Unit, encoded.X) || resolution.VerticalDpi != ResolutionExpectation.ToDpi(encoded.Unit, encoded.Y))
                {
                    errors.Add(string.Create(CultureInfo.InvariantCulture, $"{name}: resolution {resolution.HorizontalDpi}x{resolution.VerticalDpi} dpi does not match the encoded {encoded.X}x{encoded.Y} per {encoded.Unit}."));
                }
            }

            if (!double.IsFinite(resolution.HorizontalDpi) || !double.IsFinite(resolution.VerticalDpi) || resolution.HorizontalDpi <= 0 || resolution.VerticalDpi <= 0)
            {
                errors.Add($"{name}: resolutions are positive finite dots per inch.");
            }
        }

        var profiles = expected.Profiles;
        foreach (var (profileName, profile) in new[] { ("icc", profiles.Icc), ("exif", profiles.Exif), ("xmp", profiles.Xmp) })
        {
            if (profile is null)
                continue;

            if (!Sha256Regex().IsMatch(profile.Sha256) || profile.Length <= 0)
            {
                errors.Add($"{name}: profiles.{profileName} must have a lowercase SHA-256 and a positive length.");
            }

            if (entry.Format is "gif" or "qoi")
            {
                errors.Add($"{name}: {entry.Format} inputs cannot carry a {profileName} payload the library preserves.");
            }
        }

        if ((expected.IccProfile == "none") != (profiles.Icc is null))
        {
            errors.Add($"{name}: iccProfile '{expected.IccProfile}' is inconsistent with profiles.icc ({(profiles.Icc is null ? "absent" : "present")}).");
        }

        if (expected.Orientation != 1 && profiles.Exif is null)
        {
            errors.Add($"{name}: orientation {expected.Orientation} can only come from an EXIF payload (profiles.exif).");
        }

        foreach (var text in expected.Text)
        {
            if (string.IsNullOrEmpty(text.Keyword))
            {
                errors.Add($"{name}: text entries must have a keyword.");
            }
            else if (entry.Format is "gif" or "jpeg" && (text.Keyword != "Comment" || text.LanguageTag is not null || text.TranslatedKeyword is not null))
            {
                errors.Add($"{name}: {entry.Format} text entries are comments (keyword 'Comment', no language tag or translated keyword).");
            }
            else if (entry.Format == "webp")
            {
                errors.Add($"{name}: WebP has no text chunk.");
            }
            else if (entry.Format == "qoi")
            {
                errors.Add($"{name}: QOI stores no metadata.");
            }
        }
    }

    private static void ValidateAnimation(string name, FixtureEntry entry, FixtureExpectation expected, List<string> errors)
    {
        var animation = expected.Animation;
        if (animation is null)
        {
            if (expected.Frames.Count > 1 || expected.Poster is not null)
            {
                errors.Add($"{name}: images with several frames or a poster are animated and must declare 'animation'.");
            }

            return;
        }

        if (animation.TotalPlays is <= 0)
        {
            errors.Add($"{name}: totalPlays counts all plays (>= 1) or is null for infinite.");
        }

        if (expected.Poster is not null && !entry.HasFeature("apng"))
        {
            errors.Add($"{name}: only APNG inputs can have a separate poster.");
        }

        // Independent metadata conventions: the normalized value must match the encoded field
        if (entry.Format == "png")
        {
            if (animation.EncodedLoopValue is not { } plays)
            {
                errors.Add($"{name}: APNG fixtures must record the encoded acTL num_plays (encodedLoopValue).");
            }
            else if (animation.TotalPlays != (plays == 0 ? null : plays))
            {
                errors.Add($"{name}: APNG num_plays {plays} means totalPlays {(plays == 0 ? "null (infinite)" : plays.ToString(CultureInfo.InvariantCulture))}, not {Format(animation.TotalPlays)}.");
            }
        }
        else if (entry.Format == "gif")
        {
            int? expectedPlays = animation.EncodedLoopValue switch
            {
                null => 1,
                0 => null,
                var loops => loops + 1,
            };
            if (animation.TotalPlays != expectedPlays)
            {
                errors.Add($"{name}: GIF loop field {Format(animation.EncodedLoopValue)} means totalPlays {Format(expectedPlays)} (NETSCAPE2.0 stores repetitions; absent means played once), not {Format(animation.TotalPlays)}.");
            }
        }

        else if (entry.Format == "webp")
        {
            // ANIM loop count: the total number of plays, 0 is infinite (WebP container specification)
            if (animation.EncodedLoopValue is not { } loops)
            {
                errors.Add($"{name}: animated WebP fixtures must record the encoded ANIM loop count (encodedLoopValue).");
            }
            else if (animation.TotalPlays != (loops == 0 ? null : loops))
            {
                errors.Add($"{name}: WebP loop count {loops} means totalPlays {(loops == 0 ? "null (infinite)" : loops.ToString(CultureInfo.InvariantCulture))}, not {Format(animation.TotalPlays)}.");
            }
        }

        static string Format(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "null";
    }

    private static void ValidateBuffers(string frameName, FrameExpectation frame, FixtureExpectation expected, FullPath root, HashSet<string> referencedFiles, ref HashSet<string>? layouts, List<(string Frame, RawPixelBuffer Buffer)> buffers, List<string> errors)
    {
        if (frame.Buffers.Count == 0)
        {
            errors.Add($"{frameName}: at least one reference buffer is required.");
            return;
        }

        var frameLayouts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var descriptor in frame.Buffers)
        {
            if (!frameLayouts.Add(descriptor.Layout))
            {
                errors.Add($"{frameName}: duplicate layout '{descriptor.Layout}'.");
            }

            referencedFiles.Add(descriptor.Path);
            var descriptorErrors = RawBufferReader.ValidateDescriptor(descriptor, expected.Width, expected.Height);
            errors.AddRange(descriptorErrors.Select(error => $"{frameName}: {error}"));
            if (descriptorErrors.Count > 0)
                continue;

            if (!Sha256Regex().IsMatch(descriptor.Sha256))
            {
                errors.Add($"{frameName}: '{descriptor.Path}' has an invalid sha256 (64 lowercase hexadecimal characters expected).");
                continue;
            }

            try
            {
                buffers.Add((frameName, RawBufferReader.Read(root, descriptor)));
            }
            catch (InvalidDataException ex)
            {
                errors.Add($"{frameName}: {ex.Message}");
            }
        }

        if (layouts is null)
        {
            layouts = frameLayouts;
        }
        else if (!layouts.SetEquals(frameLayouts))
        {
            errors.Add($"{frameName}: layouts [{string.Join(", ", frameLayouts.Order(StringComparer.Ordinal))}] differ from the other frames [{string.Join(", ", layouts.Order(StringComparer.Ordinal))}]; every frame and the poster provide the same layouts.");
        }
    }

    /// <summary>
    /// Validates the color management sections: profiles (identity, provenance, license, hash) and reference conversions
    /// (profiles that exist, a defined intent, channel counts, vectors of the declared size, a justified tolerance).
    /// </summary>
    private static void ValidateColorSections(FixtureManifest manifest, FullPath root, HashSet<string> ids, HashSet<string> referencedFiles, List<string> errors)
    {
        var profiles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var profile in manifest.ColorProfiles ?? [])
        {
            var name = $"Color profile '{profile.Id}'";
            if (!IdRegex().IsMatch(profile.Id))
            {
                errors.Add($"{name}: invalid id (allowed: lowercase letters, digits, '-', '_', '.', '/').");
            }

            if (!ids.Add(profile.Id))
            {
                errors.Add($"{name}: duplicate id.");
            }

            profiles.Add(profile.Id);
            if (profile.Features is { } features && (features.Any(string.IsNullOrWhiteSpace) || features.Distinct(StringComparer.Ordinal).Count() != features.Count))
            {
                errors.Add($"{name}: features must be non-empty and unique.");
            }

            ValidateProvenance(name, profile.Provenance, errors);
            ValidateFile(name, profile.File, root, referencedFiles, errors);
        }

        foreach (var transform in manifest.ColorTransforms ?? [])
        {
            var name = $"Color transform '{transform.Id}'";
            if (!IdRegex().IsMatch(transform.Id))
            {
                errors.Add($"{name}: invalid id (allowed: lowercase letters, digits, '-', '_', '.', '/').");
            }

            if (!ids.Add(transform.Id))
            {
                errors.Add($"{name}: duplicate id.");
            }

            foreach (var (role, profile) in new[] { ("source", transform.Source), ("destination", transform.Destination) })
            {
                if (!profiles.Contains(profile))
                {
                    errors.Add($"{name}: the {role} profile '{profile}' is not in 'colorProfiles'.");
                }
            }

            if (!ColorTransformEntry.Intents.Contains(transform.Intent, StringComparer.Ordinal))
            {
                errors.Add($"{name}: invalid intent '{transform.Intent}' (allowed: {string.Join(", ", ColorTransformEntry.Intents)}).");
            }

            if (transform.Intent == ColorTransformEntry.Intents[3] && transform.BlackPointCompensation)
            {
                errors.Add($"{name}: black point compensation does not apply to the absolute colorimetric intent.");
            }

            if (transform.SourceChannels is not (1 or 3 or 4) || transform.DestinationChannels is not (1 or 3 or 4))
            {
                errors.Add($"{name}: channel counts must be 1, 3 or 4 (source {transform.SourceChannels}, destination {transform.DestinationChannels}).");
            }

            if (transform.SampleCount <= 0)
            {
                errors.Add($"{name}: sampleCount must be positive.");
            }

            if (string.IsNullOrWhiteSpace(transform.Reference.Tool) || string.IsNullOrWhiteSpace(transform.Reference.Command) || string.IsNullOrWhiteSpace(transform.Reference.Generator))
            {
                errors.Add($"{name}: the reference must record the tool with its version, the command and the generator.");
            }

            var comparison = transform.Comparison;
            if (comparison.MaxAbsoluteError is < 0 or > ColorTransformComparison.MaximumTolerance || !(comparison.MaxMeanAbsoluteError >= 0 && comparison.MaxMeanAbsoluteError <= comparison.MaxAbsoluteError))
            {
                errors.Add($"{name}: the tolerance must be between 0 and {ColorTransformComparison.MaximumTolerance} 16-bit units, with a mean tolerance that does not exceed it.");
            }

            if (comparison.Justification.Length < 40)
            {
                errors.Add($"{name}: the tolerance needs a justification of at least 40 characters.");
            }

            ValidateFile(name, transform.Vectors, root, referencedFiles, errors);
            if (FixturePaths.TryResolve(root, transform.Vectors.Path, out _) is { } path && File.Exists(path) && transform.SampleCount > 0)
            {
                var expected = (long)transform.SampleCount * (transform.SourceChannels + transform.DestinationChannels) * 2;
                var actual = new FileInfo(path).Length;
                if (actual != expected)
                {
                    errors.Add($"{name}: '{transform.Vectors.Path}' has {actual} bytes but {expected} are declared ({transform.SampleCount} colors).");
                }
            }
        }
    }

    private static void ValidateProvenance(string name, FixtureProvenance provenance, List<string> errors)
    {
        if (!AllowedOrigins.Contains(provenance.Origin))
        {
            errors.Add($"{name}: invalid provenance origin '{provenance.Origin}' (allowed: {string.Join(", ", AllowedOrigins)}).");
        }

        if (!AllowedLicenses.Contains(provenance.License))
        {
            errors.Add($"{name}: license '{provenance.License}' is not in the allowed list ({string.Join(", ", AllowedLicenses)}). See tests/Meziantou.Framework.Imaging.Fixtures/README.md.");
        }

        if (provenance.Origin == "external" && string.IsNullOrEmpty(provenance.Source))
        {
            errors.Add($"{name}: external fixtures must declare 'source'.");
        }

        if (provenance.Origin == "generated" && string.IsNullOrEmpty(provenance.Generator))
        {
            errors.Add($"{name}: generated fixtures must declare 'generator'.");
        }

        if (provenance.Origin == "generated" && provenance.Tools is not { Count: > 0 })
        {
            errors.Add($"{name}: generated fixtures must record the pinned tools and versions ('tools').");
        }
    }

    private static void ValidateFile(string name, FixtureFile file, FullPath root, HashSet<string> referencedFiles, List<string> errors)
    {
        if (FixturePaths.TryResolve(root, file.Path, out var pathError) is not { } fullPath)
        {
            errors.Add($"{name}: {pathError}");
            return;
        }

        referencedFiles.Add(file.Path);
        if (!Sha256Regex().IsMatch(file.Sha256))
        {
            errors.Add($"{name}: '{file.Path}' has an invalid sha256 (64 lowercase hexadecimal characters expected).");
        }

        if (!File.Exists(fullPath))
        {
            errors.Add($"{name}: file '{file.Path}' does not exist.");
            return;
        }

        var actual = ComputeSha256(fullPath);
        if (!string.Equals(actual, file.Sha256, StringComparison.Ordinal))
        {
            errors.Add($"{name}: file '{file.Path}' has sha256 {actual} but the manifest declares {file.Sha256}. Regenerating fixtures is an explicit, reviewed operation.");
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._/-]*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex IdRegex();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Sha256Regex();
}
