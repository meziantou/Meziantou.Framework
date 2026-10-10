using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Golden;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// Sequential reading of every corpus input through every reader variant: every <c>ReadFrame</c> and
/// <c>ReadFrameInto</c> result (and the separate poster) is compared with the manifest's full displayed frames — exact
/// count, sequence, durations, poster — through <see cref="ReaderSnapshots"/>, which also overwrites and disposes returned
/// images to prove that the reader's compositor state is independent of the caller. Error fixtures must fail with their
/// declared exception: a defect is never reported as a clean end of input.
/// </summary>
public sealed class SequentialReadConformanceTests
{
    public static TheoryData<string, InputVariant> ValidCases => CreateCases(valid: true);

    public static TheoryData<string, InputVariant> ErrorCases => CreateCases(valid: false);

    [Theory]
    [MemberData(nameof(ValidCases))]
    public async Task ReadFrameMatchesReferences(string id, InputVariant variant)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var format = FixtureOptions.GetFormat(fixture);
        var data = fixture.ReadInput();
        var snapshot = await ReadAsync(fixture, variant, data, format, into: false);
        GoldenAssert.ImageMatches(fixture, snapshot);
    }

    [Theory]
    [MemberData(nameof(ValidCases))]
    public async Task ReadFrameIntoMatchesReferences(string id, InputVariant variant)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var format = FixtureOptions.GetFormat(fixture);
        var data = fixture.ReadInput();
        var snapshot = await ReadAsync(fixture, variant, data, format, into: true);
        GoldenAssert.ImageMatches(fixture, snapshot);
    }

    [Theory]
    [MemberData(nameof(ErrorCases))]
    public async Task ErrorFixturesFailExplicitly(string id, InputVariant variant)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var format = FixtureOptions.GetFormat(fixture);
        var options = CreateReaderOptions(fixture);
        var data = fixture.ReadInput();
        // Header defects fail when the reader is opened; later defects fail a read: never a clean end (null)
        await GoldenAssert.FailsAsExpectedAsync(fixture, () => InputVariants.ReadAsync<Rgba64, int>(variant, data, format, options, ReadAllAsync, XunitCancellationToken));
    }

    private static Task<DecodedImageSnapshot> ReadAsync(GoldenFixture fixture, InputVariant variant, byte[] data, ImageFormat format, bool into)
    {
        var options = CreateReaderOptions(fixture);
        var lateLoopPlays = GetLateGifLoopPlays(fixture);
        return Enum.Parse<PixelFormat>(fixture.Expected.PixelFormat) switch
        {
            PixelFormat.Rgba32 => ReadAsync<Rgba32>(variant, data, format, options, into, lateLoopPlays),
            PixelFormat.Rgb24 => ReadAsync<Rgb24>(variant, data, format, options, into, lateLoopPlays),
            PixelFormat.Rgba64 => ReadAsync<Rgba64>(variant, data, format, options, into, lateLoopPlays),
            PixelFormat.Gray8 => ReadAsync<Gray8>(variant, data, format, options, into, lateLoopPlays),
            PixelFormat.Gray16 => ReadAsync<Gray16>(variant, data, format, options, into, lateLoopPlays),
            _ => ReadAsync<Bgra32>(variant, data, format, options, into, lateLoopPlays),
        };
    }

    /// <summary>
    /// A GIF loop extension that only follows the first image is not in the reader's header snapshot (<c>Info</c>): readers
    /// never report it (eager loads and full scans do, see <see cref="EagerLoadConformanceTests"/> and
    /// <see cref="IdentifyConformanceTests"/>). Returns the expected plays of such a fixture, otherwise <see langword="null"/>.
    /// </summary>
    private static int? GetLateGifLoopPlays(GoldenFixture fixture)
    {
        if (fixture.Entry.Format != "gif" || fixture.InspectInput().LoopValue is null)
            return null;

        var header = Image.Identify(fixture.ReadInput());
        return header.Animation is null ? fixture.Expected.Animation?.TotalPlays : null;
    }

    private static Task<DecodedImageSnapshot> ReadAsync<TPixel>(InputVariant variant, byte[] data, ImageFormat format, ImageReaderOptions options, bool into, int? lateLoopPlays)
        where TPixel : unmanaged
    {
        return InputVariants.ReadAsync<TPixel, DecodedImageSnapshot>(variant, data, format, options, async (reader, asynchronous) =>
        {
            var snapshot = into
                ? await ReaderSnapshots.CaptureIntoAsync(reader, asynchronous, includeMetadata: true, XunitCancellationToken)
                : await ReaderSnapshots.CaptureAsync(reader, asynchronous, includeMetadata: true, XunitCancellationToken);
            return WithAnimationKnownAfterReading(reader.Info, snapshot, lateLoopPlays);
        }, XunitCancellationToken);
    }

    /// <summary>
    /// The animation settings of <c>reader.Info</c> come from the header. When the header cannot tell (a GIF without a loop
    /// extension before its first image: <c>IsAnimated</c> is unknown), an animation is still recognized from the frames read.
    /// </summary>
    private static DecodedImageSnapshot WithAnimationKnownAfterReading(ImageInfo info, DecodedImageSnapshot snapshot, int? lateLoopPlays)
    {
        if (info.Animation is not null || info.IsAnimated is not null || (snapshot.Frames.Count < 2 && snapshot.Poster is null))
            return snapshot;

        return new DecodedImageSnapshot
        {
            Frames = snapshot.Frames,
            Poster = snapshot.Poster,
            HasAnimation = true,
            // No loop extension before the first image: played once, unless a later one exists
            TotalPlays = info.Format == ImageFormat.Gif ? lateLoopPlays ?? 1 : null,
            Orientation = snapshot.Orientation,
            Metadata = snapshot.Metadata,
            PixelFormat = snapshot.PixelFormat,
        };
    }

    private static async Task<int> ReadAllAsync(ImageReader<Rgba64> reader, bool asynchronous)
    {
        var count = 0;
        using var poster = asynchronous ? await reader.ReadPosterFrameAsync(XunitCancellationToken) : reader.ReadPosterFrame();
        while (true)
        {
            using var frame = asynchronous ? await reader.ReadFrameAsync(XunitCancellationToken) : reader.ReadFrame();
            if (frame is null)
                return count;

            count++;
        }
    }

    private static ImageReaderOptions CreateReaderOptions(GoldenFixture fixture)
    {
        var decode = FixtureOptions.CreateDecodeOptions(fixture);
        return new ImageReaderOptions { Configuration = decode.Configuration, FrameLimit = decode.FrameLimit };
    }

    private static TheoryData<string, InputVariant> CreateCases(bool valid)
    {
        var data = new TheoryData<string, InputVariant>();
        foreach (var fixture in GoldenCorpus.Default.Fixtures.Where(fixture => fixture.IsValid == valid))
        {
            foreach (var variant in InputVariants.ReaderVariants)
            {
                data.Add(fixture.Id, variant);
            }
        }

        return data;
    }
}
