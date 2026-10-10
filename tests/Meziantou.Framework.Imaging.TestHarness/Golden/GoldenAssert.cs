using System.Reflection;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.TestHarness.Golden;

/// <summary>
/// Test-framework-agnostic assertions comparing decoded output with golden fixtures. Failures throw
/// <see cref="GoldenAssertionException"/> with fixture, frame, coordinates, channel, expected/actual samples, violation
/// count, maximum error and hints; expected/actual/difference previews are written as artifacts (never as the oracle).
/// </summary>
public static class GoldenAssert
{
    /// <summary>Asserts that a decoded displayed frame matches its reference under the fixture policy.</summary>
    /// <param name="fixture">The fixture.</param>
    /// <param name="frameIndex">The frame index.</param>
    /// <param name="actual">The decoded pixels; the reference is read in the same layout.</param>
    /// <param name="options">The options.</param>
    public static void FrameMatches(GoldenFixture fixture, int frameIndex, RawPixelBuffer actual, GoldenAssertOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(actual);
        var failure = CompareFrame(fixture, $"frame {frameIndex.ToString(CultureInfo.InvariantCulture)}", () => fixture.GetFrame(frameIndex, actual.Layout), actual, options ?? GoldenAssertOptions.Default);
        if (failure is not null)
            throw new GoldenAssertionException(failure);
    }

    /// <summary>Asserts that a decoded poster matches its reference under the fixture policy.</summary>
    /// <param name="fixture">The fixture.</param>
    /// <param name="actual">The decoded poster pixels.</param>
    /// <param name="options">The options.</param>
    public static void PosterMatches(GoldenFixture fixture, RawPixelBuffer actual, GoldenAssertOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(actual);
        var failure = CompareFrame(fixture, "poster", () => fixture.GetPoster(actual.Layout), actual, options ?? GoldenAssertOptions.Default);
        if (failure is not null)
            throw new GoldenAssertionException(failure);
    }

    /// <summary>Asserts that raw decoded bytes (tightly packed in <paramref name="layout"/>) match a frame reference; reports truncated/oversized buffers.</summary>
    /// <param name="fixture">The fixture.</param>
    /// <param name="frameIndex">The frame index.</param>
    /// <param name="layout">The layout of <paramref name="actual"/>.</param>
    /// <param name="actual">The bytes.</param>
    /// <param name="options">The options.</param>
    public static void FrameBytesMatch(GoldenFixture fixture, int frameIndex, RawPixelLayout layout, ReadOnlySpan<byte> actual, GoldenAssertOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(layout);
        var expected = fixture.GetFrame(frameIndex, layout);
        if (actual.Length != expected.ByteLength)
        {
            var result = PixelBufferComparer.CompareBytes(expected, actual, fixture.Policy, Context(fixture, $"frame {frameIndex.ToString(CultureInfo.InvariantCulture)}"));
            throw new GoldenAssertionException(result.Describe());
        }

        FrameMatches(fixture, frameIndex, RawPixelBuffer.Create(expected.Width, expected.Height, layout, actual), options);
    }

    /// <summary>
    /// Asserts that a decoded image matches the fixture: frame count, poster presence, animation settings and total plays,
    /// exact durations, orientation, pixel format, metadata (when <see cref="DecodedImageSnapshot.Metadata"/> is provided),
    /// then every displayed frame and the poster. All failures are reported together.
    /// </summary>
    /// <param name="fixture">The fixture.</param>
    /// <param name="actual">The decoded image snapshot.</param>
    /// <param name="options">The options.</param>
    public static void ImageMatches(GoldenFixture fixture, DecodedImageSnapshot actual, GoldenAssertOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(actual);
        options ??= GoldenAssertOptions.Default;
        var expected = fixture.Expected;
        var failures = new List<string>();
        if (actual.Frames.Count != expected.FrameCount)
        {
            failures.Add(string.Create(CultureInfo.InvariantCulture, $"Frame count: expected {expected.FrameCount} displayed frames, actual {actual.Frames.Count} (a separate poster is not a frame)."));
        }

        if ((expected.Poster is null) != (actual.Poster is null))
        {
            failures.Add(expected.Poster is null ? "Poster: no separate poster expected, but the image has one." : "Poster: a separate poster is expected, but the image has none.");
        }

        if (expected.Animation is null)
        {
            if (actual.HasAnimation)
            {
                failures.Add("Animation: the image is expected to have no animation settings (still image).");
            }
        }
        else if (!actual.HasAnimation)
        {
            failures.Add("Animation: animation settings are expected, but the image has none.");
        }
        else if (actual.TotalPlays != expected.Animation.TotalPlays)
        {
            failures.Add($"Total plays: expected {FormatPlays(expected.Animation.TotalPlays)}, actual {FormatPlays(actual.TotalPlays)} (encoded loop field: {expected.Animation.EncodedLoopValue?.ToString(CultureInfo.InvariantCulture) ?? "absent"}).");
        }

        if (actual.Orientation != expected.Orientation)
        {
            failures.Add(string.Create(CultureInfo.InvariantCulture, $"Orientation: expected {expected.Orientation}, actual {actual.Orientation}."));
        }

        if (actual.PixelFormat is not null && actual.PixelFormat != expected.PixelFormat)
        {
            failures.Add($"Pixel format: expected {expected.PixelFormat}, actual {actual.PixelFormat}.");
        }

        if (actual.Metadata is { } metadata)
        {
            CompareMetadata(expected, metadata, failures);
        }

        var count = Math.Min(actual.Frames.Count, expected.FrameCount);
        for (var i = 0; i < count; i++)
        {
            var expectedDuration = fixture.GetDuration(i);
            if (actual.Frames[i].Duration != expectedDuration)
            {
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"Frame {i} duration: expected {expectedDuration} s, actual {actual.Frames[i].Duration} s (exact rational comparison)."));
            }

            var index = i;
            var failure = CompareFrame(fixture, $"frame {i.ToString(CultureInfo.InvariantCulture)}", () => fixture.GetFrame(index, actual.Frames[index].Pixels.Layout), actual.Frames[i].Pixels, options);
            if (failure is not null)
            {
                failures.Add(failure);
            }
        }

        if (expected.Poster is not null && actual.Poster is not null)
        {
            var failure = CompareFrame(fixture, "poster", () => fixture.GetPoster(actual.Poster.Layout), actual.Poster, options);
            if (failure is not null)
            {
                failures.Add(failure);
            }
        }

        if (failures.Count > 0)
            throw new GoldenAssertionException($"Fixture '{fixture.Id}' does not match its reference:{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
    }

    /// <summary>Asserts that an operation fails as declared by an invalid, unsupported or limit fixture.</summary>
    /// <param name="fixture">The fixture.</param>
    /// <param name="action">The operation (e.g. loading the input with the fixture decode options).</param>
    /// <returns>The exception.</returns>
    public static Exception FailsAsExpected(GoldenFixture fixture, Action action)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(action);
        try
        {
            action();
        }
        catch (Exception ex)
        {
            CheckException(fixture, ex);
            return ex;
        }

        throw new GoldenAssertionException($"Fixture '{fixture.Id}' ({fixture.Entry.Kind}) was expected to fail with {fixture.ExpectedError.Exception}, but the operation succeeded.");
    }

    /// <summary>Asserts that an asynchronous operation fails as declared by an invalid, unsupported or limit fixture.</summary>
    /// <param name="fixture">The fixture.</param>
    /// <param name="action">The operation.</param>
    /// <returns>The exception.</returns>
    public static async Task<Exception> FailsAsExpectedAsync(GoldenFixture fixture, Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(action);
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            CheckException(fixture, ex);
            return ex;
        }

        throw new GoldenAssertionException($"Fixture '{fixture.Id}' ({fixture.Entry.Kind}) was expected to fail with {fixture.ExpectedError.Exception}, but the operation succeeded.");
    }

    private static void CheckException(GoldenFixture fixture, Exception exception)
    {
        var expected = fixture.ExpectedError;
        var type = exception.GetType();
        if (type.Name != expected.Exception && type.FullName != expected.Exception)
            throw new GoldenAssertionException($"Fixture '{fixture.Id}' was expected to fail with {expected.Exception} but failed with {type.FullName}: {exception.Message}", exception);

        CheckProperty(fixture, exception, "Format", expected.Format);
        CheckProperty(fixture, exception, "Feature", expected.Feature);
        CheckProperty(fixture, exception, "Kind", expected.LimitKind);
    }

    private static void CheckProperty(GoldenFixture fixture, Exception exception, string propertyName, string? expected)
    {
        if (expected is null)
            return;

        var property = exception.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new GoldenAssertionException($"Fixture '{fixture.Id}' expects {exception.GetType().Name}.{propertyName} = {expected}, but the exception has no such property.", exception);
        var actual = Convert.ToString(property.GetValue(exception), CultureInfo.InvariantCulture);
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new GoldenAssertionException($"Fixture '{fixture.Id}' expects {exception.GetType().Name}.{propertyName} = {expected}, actual {actual ?? "null"}.", exception);
    }

    private static string? CompareFrame(GoldenFixture fixture, string role, Func<RawPixelBuffer> readExpected, RawPixelBuffer actual, GoldenAssertOptions options)
    {
        RawPixelBuffer expected;
        try
        {
            expected = readExpected();
        }
        catch (ArgumentException ex)
        {
            return $"{Context(fixture, role)}: {ex.Message}";
        }

        var result = PixelBufferComparer.Compare(expected, actual, fixture.Policy, Context(fixture, role), options.MaxReportedMismatches);
        if (result.IsMatch)
            return null;

        var message = result.Describe();
        if (options.WritePreviews)
        {
            try
            {
                var directory = options.PreviewDirectory ?? TestArtifacts.GetDirectory(fixture.Id);
                var files = DiffPreviewWriter.Write(directory, role.Replace(' ', '-'), expected, actual, result);
                message += Environment.NewLine + "Previews (diagnosis only): " + string.Join(", ", files);
            }
            catch (IOException ex)
            {
                message += Environment.NewLine + "Previews could not be written: " + ex.Message;
            }
            catch (UnauthorizedAccessException ex)
            {
                message += Environment.NewLine + "Previews could not be written: " + ex.Message;
            }
        }

        return message;
    }

    private static void CompareMetadata(Fixtures.FixtureExpectation expected, DecodedMetadataSnapshot actual, List<string> failures)
    {
        var expectedResolution = expected.Resolution;
        if (expectedResolution is null)
        {
            if (actual.HorizontalDpi is not null || actual.VerticalDpi is not null)
            {
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"Resolution: none expected, actual {actual.HorizontalDpi}x{actual.VerticalDpi} dpi."));
            }
        }
        else if (actual.HorizontalDpi != expectedResolution.HorizontalDpi || actual.VerticalDpi != expectedResolution.VerticalDpi)
        {
            failures.Add(string.Create(CultureInfo.InvariantCulture, $"Resolution: expected {expectedResolution.HorizontalDpi}x{expectedResolution.VerticalDpi} dpi (encoded {expectedResolution.Encoded.X}x{expectedResolution.Encoded.Y} per {expectedResolution.Encoded.Unit}), actual {actual.HorizontalDpi?.ToString("R", CultureInfo.InvariantCulture) ?? "none"}x{actual.VerticalDpi?.ToString("R", CultureInfo.InvariantCulture) ?? "none"} dpi (exact comparison)."));
        }

        if (!string.Equals(expected.EffectiveTransferFunction, actual.TransferFunction, StringComparison.Ordinal))
        {
            failures.Add($"Transfer function: expected {expected.EffectiveTransferFunction}, actual {actual.TransferFunction} (a label: samples are never converted).");
        }

        CompareProfile("ICC profile", expected.Profiles.Icc, actual.IccProfile, failures);
        CompareProfile("EXIF profile", expected.Profiles.Exif, actual.ExifProfile, failures);
        CompareProfile("XMP packet", expected.Profiles.Xmp, actual.XmpProfile, failures);

        var expectedText = expected.Text.Select(entry => new DecodedTextEntry(entry.Keyword, entry.Value, entry.LanguageTag, entry.TranslatedKeyword)).ToList();
        if (!expectedText.SequenceEqual(actual.TextEntries))
        {
            failures.Add($"Text entries: expected [{string.Join("; ", expectedText)}], actual [{string.Join("; ", actual.TextEntries)}].");
        }

        static void CompareProfile(string name, Fixtures.ProfileExpectation? expected, ReadOnlyMemory<byte>? actual, List<string> failures)
        {
            if (expected is null)
            {
                if (actual is not null)
                {
                    failures.Add(string.Create(CultureInfo.InvariantCulture, $"{name}: none expected, actual {actual.Value.Length} bytes."));
                }
            }
            else if (actual is null)
            {
                failures.Add($"{name}: expected {expected}, actual none.");
            }
            else if (!expected.Matches(actual.Value.Span))
            {
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"{name}: expected {expected}, actual {actual.Value.Length} bytes, sha256 {Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(actual.Value.Span))} (payloads must be preserved byte for byte)."));
            }
        }
    }

    private static string Context(GoldenFixture fixture, string role) => $"Fixture '{fixture.Id}', {role}";

    private static string FormatPlays(int? plays) => plays?.ToString(CultureInfo.InvariantCulture) ?? "infinite";
}
