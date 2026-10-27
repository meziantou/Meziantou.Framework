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
    public void AvifTimingPlaysAndColorDescriptionInconsistenciesAreReported()
    {
        using var corpus = new CorpusCopy();
        corpus.EditFixture("avif/sequence-alpha-repeat-2", fixture =>
        {
            Frame(fixture, 0)["encodedDelay"]!["timescale"] = 500; // 40/500 s is not the 40/1000 s of the duration
            fixture["expected"]!["animation"]!["totalPlays"] = 2; // a track of 3120 units and an edit of 1040: three plays
        });
        corpus.EditFixture("avif/sequence-no-edit-list", fixture => fixture["expected"]!["animation"]!["totalPlays"] = 1);
        corpus.EditFixture("avif/sequence-10bit-once", fixture =>
        {
            Frame(fixture, 1).Remove("encodedDelay");
            fixture["expected"]!["animation"]!["editList"]!["repeating"] = true; // 2002 / 2002 is still one play
        });
        corpus.EditFixture("avif/sequence-lossless-infinite", fixture => fixture["expected"]!["animation"]!["encodedLoopValue"] = 0);
        corpus.EditFixture("avif/lossless-rgb8", fixture => fixture["expected"]!["cicp"]!["matrixCoefficients"] = 256);
        corpus.EditFixture("avif/irot-1", fixture => fixture["expected"]!["bitsPerComponent"] = 9);
        corpus.EditFixture("png/single-pixel", fixture =>
        {
            fixture["expected"]!["cicp"] = new JsonObject { ["colorPrimaries"] = 1, ["transferCharacteristics"] = 13, ["matrixCoefficients"] = 0, ["fullRange"] = true };
            fixture["expected"]!["bitsPerComponent"] = 10;
        });
        corpus.EditFixture("gif/ffmpeg-animated", fixture => fixture["expected"]!["animation"]!["editList"] = new JsonObject { ["repeating"] = true, ["segmentDuration"] = 1, ["trackDuration"] = 2 });
        corpus.EditFixture("apng/blend-source-over", fixture => Frame(fixture, 0)["encodedDelay"] = new JsonObject { ["sampleDelta"] = 1, ["timescale"] = 10 });

        var errors = corpus.Validate();
        Assert.Contains(errors, error => error.Contains("avif/sequence-alpha-repeat-2' frame 0: encoded delay 40/500 (stts/mdhd) means 2/25 s, not 1/25 s", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("avif/sequence-alpha-repeat-2': the edit list of the sequence means totalPlays 3 ", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("avif/sequence-no-edit-list': the edit list of the sequence means totalPlays null ", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("avif/sequence-10bit-once' frame 1: frames of animated inputs must record the encoded delay field", StringComparison.Ordinal));
        Assert.DoesNotContain(errors, error => error.Contains("avif/sequence-10bit-once': the edit list", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("avif/sequence-lossless-infinite': an AVIF image sequence has no loop field", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("avif/lossless-rgb8': the CICP code points are 8-bit values", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("avif/irot-1': invalid bitsPerComponent 9", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("png/single-pixel': only AVIF, JPEG XL and OpenEXR inputs declare a CICP color description", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("png/single-pixel': invalid bitsPerComponent 10", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("gif/ffmpeg-animated': only AVIF image sequences have an edit list", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("apng/blend-source-over' frame 0: encodedDelay must be", StringComparison.Ordinal));

        // The orientation of an AVIF image needs no EXIF payload (irot and imir); any other format still needs one
        Assert.DoesNotContain(errors, error => error.Contains("avif/irot-1': orientation", StringComparison.Ordinal));
        Assert.Equal(8, GoldenCorpus.Default.Get("avif/irot-1").Expected.Orientation);
        Assert.Null(GoldenCorpus.Default.Get("avif/irot-1").Expected.Profiles.Exif);
    }

    [Fact]
    public void ColorDescriptionMismatchIsReported()
    {
        var fixture = GoldenCorpus.Default.Get("avif/lossless-rgba8");
        var good = GoldenCorpusTests.SnapshotFromReferences(fixture, RawPixelLayout.Rgba8);
        GoldenAssert.ImageMatches(fixture, good, NoPreviews);
        Assert.Equal("1/13/0 full", good.Metadata!.Cicp);
        AssertFailure(fixture, good, With(good.Metadata), "CICP color description: expected 1/13/0 full, actual none");

        // A color description on an image whose format has none
        var png = GoldenCorpus.Default.Get("png/single-pixel");
        var snapshot = GoldenCorpusTests.SnapshotFromReferences(png, RawPixelLayout.Rgba8);
        AssertFailure(png, snapshot, new DecodedMetadataSnapshot { Cicp = "1/13/6 full" }, "CICP color description: expected none, actual 1/13/6 full");
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
