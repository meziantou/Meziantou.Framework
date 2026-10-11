using System.Text.Json.Nodes;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// Proves that the manifest validator and <see cref="GoldenAssert.ImageMatches"/> detect wrong timing and metadata
/// expectations: encoded delays that disagree with exact durations, resolutions that disagree with their encoded fields,
/// inconsistent profile labels, orientations without EXIF, non-comment text in GIF/JPEG, and decoded metadata that does
/// not preserve payloads byte for byte.
/// </summary>
public sealed class MetadataExpectationValidationTests
{
    private static readonly GoldenAssertOptions NoPreviews = new() { WritePreviews = false };

    [Fact]
    public void EncodedDelayInconsistenciesAreReported()
    {
        using var corpus = new CorpusCopy();
        corpus.EditFixture("gif/disposal-transparency", fixture => Frame(fixture, 2)["encodedDelay"]!["hundredths"] = 8);
        corpus.EditFixture("apng/blend-source-over", fixture =>
        {
            Frame(fixture, 0)["encodedDelay"]!["denominator"] = 0; // 1/0 means 1/100, not 1/10
            Frame(fixture, 1).Remove("encodedDelay");
        });
        corpus.EditFixture("gif/ffmpeg-animated", fixture => Frame(fixture, 0)["encodedDelay"] = new JsonObject { ["numerator"] = 1, ["denominator"] = 5 });

        var errors = corpus.Validate();
        Assert.Contains(errors, error => error.Contains("gif/disposal-transparency' frame 2: encoded delay 8 hundredths means 2/25 s, not 7/100 s", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("apng/blend-source-over' frame 0: encoded delay 1/0 (fcTL) means 1/100 s, not 1/10 s", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("apng/blend-source-over' frame 1: frames of animated inputs must record the encoded delay field", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("gif/ffmpeg-animated' frame 0: encodedDelay must be", StringComparison.Ordinal));
    }

    [Fact]
    public void ResolutionInconsistenciesAreReported()
    {
        using var corpus = new CorpusCopy();
        corpus.EditFixture("png/metadata-rgb8-profiles", fixture => fixture["expected"]!["resolution"]!["horizontalDpi"] = 96.0);
        corpus.EditFixture("jpeg/metadata-density-xmp-comment", fixture => fixture["expected"]!["resolution"]!["encoded"]!["unit"] = "meter");
        corpus.EditFixture("png/single-pixel", fixture => fixture["expected"]!["resolution"] = new JsonObject
        {
            ["horizontalDpi"] = 72.0,
            ["verticalDpi"] = 72.0,
            ["encoded"] = new JsonObject { ["unit"] = "inch", ["x"] = 72, ["y"] = 72 },
        });

        var errors = corpus.Validate();
        Assert.Contains(errors, error => error.Contains("png/metadata-rgb8-profiles", StringComparison.Ordinal) && error.Contains("does not match the encoded 3780x2835 per meter", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("jpeg/metadata-density-xmp-comment", StringComparison.Ordinal) && error.Contains("cannot encode a resolution in 'meter'", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("png/single-pixel", StringComparison.Ordinal) && error.Contains("cannot encode a resolution in 'inch'", StringComparison.Ordinal));
    }

    [Fact]
    public void ProfileOrientationAndTextInconsistenciesAreReported()
    {
        using var corpus = new CorpusCopy();
        corpus.EditFixture("png/metadata-gray8-icc", fixture => fixture["expected"]!["iccProfile"] = "none");
        corpus.EditFixture("jpeg/exif-orientation-6", fixture => fixture["expected"]!["profiles"]!["exif"] = null);
        corpus.EditFixture("jpeg/metadata-density-xmp-comment", fixture => fixture["expected"]!["text"]![0]!["keyword"] = "Title");
        corpus.EditFixture("png/metadata-rgb8-profiles", fixture => fixture["expected"]!["profiles"]!["xmp"]!["sha256"] = "ABC");

        var errors = corpus.Validate();
        Assert.Contains(errors, error => error.Contains("png/metadata-gray8-icc", StringComparison.Ordinal) && error.Contains("iccProfile 'none' is inconsistent with profiles.icc (present)", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("jpeg/exif-orientation-6", StringComparison.Ordinal) && error.Contains("orientation 6 can only come from an EXIF payload", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("jpeg/metadata-density-xmp-comment", StringComparison.Ordinal) && error.Contains("text entries are comments", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("png/metadata-rgb8-profiles", StringComparison.Ordinal) && error.Contains("profiles.xmp must have a lowercase SHA-256", StringComparison.Ordinal));
    }

    [Fact]
    public void DecodedMetadataMismatchesAreReported()
    {
        var fixture = GoldenCorpus.Default.Get("png/metadata-rgb8-profiles");
        var good = GoldenCorpusTests.SnapshotFromReferences(fixture, RawPixelLayout.Rgba8);
        GoldenAssert.ImageMatches(fixture, good, NoPreviews);
        var metadata = good.Metadata!;

        AssertFailure(fixture, good, With(metadata, horizontalDpi: 96.0), "Resolution: expected 96.012x72.009 dpi (encoded 3780x2835 per meter), actual 96x72.009 dpi");
        AssertFailure(fixture, good, With(metadata, dropXmp: true), "XMP packet: expected 371 bytes");
        var exif = metadata.ExifProfile!.Value.ToArray();
        exif[^1] ^= 0x01;
        AssertFailure(fixture, good, With(metadata, exif: exif), "EXIF profile: expected 156 bytes, sha256");
        AssertFailure(fixture, good, With(metadata, text: [.. metadata.TextEntries.Reverse()]), "Text entries: expected [");
        AssertFailure(fixture, good, With(metadata, text: [.. metadata.TextEntries.Select(entry => entry with { LanguageTag = null })]), "Text entries");

        var still = GoldenCorpus.Default.Get("png/single-pixel");
        var stillSnapshot = GoldenCorpusTests.SnapshotFromReferences(still, RawPixelLayout.Rgba8);
        AssertFailure(still, stillSnapshot, With(stillSnapshot.Metadata!, icc: new byte[200]), "ICC profile: none expected, actual 200 bytes");
        AssertFailure(still, stillSnapshot, With(stillSnapshot.Metadata!, horizontalDpi: 72, verticalDpi: 72), "Resolution: none expected");
    }

    private static JsonObject Frame(JsonObject fixture, int index) => fixture["expected"]!["frames"]![index]!.AsObject();

    private static void AssertFailure(GoldenFixture fixture, DecodedImageSnapshot good, DecodedMetadataSnapshot metadata, string expectedMessage)
    {
        var snapshot = new DecodedImageSnapshot
        {
            Frames = good.Frames,
            Poster = good.Poster,
            HasAnimation = good.HasAnimation,
            TotalPlays = good.TotalPlays,
            Orientation = good.Orientation,
            PixelFormat = good.PixelFormat,
            Metadata = metadata,
        };
        var exception = Assert.Throws<GoldenAssertionException>(() => GoldenAssert.ImageMatches(fixture, snapshot, NoPreviews));
        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
    }

    private static DecodedMetadataSnapshot With(DecodedMetadataSnapshot snapshot, double? horizontalDpi = null, double? verticalDpi = null, bool dropXmp = false, byte[]? exif = null, byte[]? icc = null, DecodedTextEntry[]? text = null) => new()
    {
        HorizontalDpi = horizontalDpi ?? snapshot.HorizontalDpi,
        VerticalDpi = verticalDpi ?? snapshot.VerticalDpi,
        IccProfile = icc ?? snapshot.IccProfile,
        ExifProfile = exif ?? snapshot.ExifProfile,
        XmpProfile = dropXmp ? null : snapshot.XmpProfile,
        TextEntries = text ?? snapshot.TextEntries,
    };
}
