using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Golden;

namespace Meziantou.Framework.Imaging.Tests.Conformance.Hardening;

/// <summary>
/// Every configured resource limit at the boundary and one over, for every valid corpus
/// input of every codec (PNG, APNG, GIF, baseline and progressive JPEG) and every input-consuming entry point (eager load,
/// sequential reader, full and header identification). The requirement of the counters is computed from the manifest
/// (canvas, displayed frames, poster, file length), never from the code under test: the input must decode with the limit
/// equal to the requirement, and fail with <see cref="ImageResourceLimitException"/> reporting the right <c>Kind</c>,
/// <c>Limit</c> and <c>Requested</c> with the limit one below. Metadata and live-allocation requirements have no independent
/// value: they are measured by bisection, and the measured value must be accepted while one byte less fails with the right
/// kind (live bytes are charged at actual rented capacity).
/// </summary>
public sealed class ResourceLimitBoundaryTests
{
    private static readonly PixelConversionOptions Conversion = new() { DiscardIncompatibleColorProfile = true };
    private static readonly string[] Contexts = ["load", "reader", "identify-full", "identify-header"];

    public static TheoryData<string, string> Cases
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var id in GoldenCorpus.Default.GetIds(kind: FixtureKinds.Valid))
            {
                if (GoldenCorpus.Default.Get(id).Entry.DecodeOptions is not null)
                    continue;

                foreach (var context in Contexts)
                {
                    data.Add(id, context);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryLimitIsInclusiveAndOneOverFails(string id, string context)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var data = fixture.ReadInput();
        var expected = fixture.Expected;
        var displayed = expected.FrameCount;
        var poster = expected.Poster is null ? 0 : 1;
        var framePixels = (long)expected.Width * expected.Height;
        var full = context != "identify-header";
        var decodes = context is "load" or "reader";
        Assert.Null(Run(context, data, ImageResourceLimits.Default));

        AssertExactBoundary(context, data, ImageResourceLimitKind.Width, expected.Width);
        AssertExactBoundary(context, data, ImageResourceLimitKind.Height, expected.Height);
        AssertExactBoundary(context, data, ImageResourceLimitKind.FramePixels, framePixels);
        if (full)
        {
            AssertExactBoundary(context, data, ImageResourceLimitKind.Frames, displayed + poster);
            AssertExactBoundary(context, data, ImageResourceLimitKind.EncodedBytes, data.Length);
        }
        else
        {
            // Header identification never counts frames and stops before the image data
            Assert.Null(Run(context, data, Limits(ImageResourceLimitKind.Frames, 1)));
            Assert.Null(Run(context, data, Limits(ImageResourceLimitKind.TotalPixels, 1)));
            var header = Measure(context, data, ImageResourceLimitKind.EncodedBytes, data.Length);
            Assert.True(header < data.Length, $"Header identification consumed the whole input ({header} bytes)");
        }

        if (decodes)
        {
            AssertExactBoundary(context, data, ImageResourceLimitKind.TotalPixels, framePixels * displayed);
        }

        // Measured requirements: accepted at the measured value, one byte less fails with the right kind
        Measure(context, data, ImageResourceLimitKind.MetadataBytes, ImageResourceLimits.DefaultMaxMetadataBytes);
        Measure(context, data, ImageResourceLimitKind.LiveAllocationBytes, ImageResourceLimits.DefaultMaxLiveAllocationBytes);
    }

    [Theory]
    [MemberData(nameof(AnimationIds))]
    public void FrameLimitSelectsAPrefixWhileMaxFramesIsAnError(string id)
    {
        // FrameLimit is a deliberate prefix selection (never an error, the rest is not examined); MaxFrames is a safety bound
        // that fails as soon as the frames processed (displayed frames and the poster) exceed it, even within the prefix
        var fixture = GoldenCorpus.Default.Get(id);
        var data = fixture.ReadInput();
        var poster = fixture.Expected.Poster is null ? 0 : 1;
        for (var frameLimit = 1; frameLimit <= fixture.Expected.FrameCount + 1; frameLimit++)
        {
            var selected = Math.Min(frameLimit, fixture.Expected.FrameCount);
            var limits = Limits(ImageResourceLimitKind.Frames, selected + poster);
            using (var image = Image.Load(data, new ImageDecodeOptions { FrameLimit = frameLimit, Configuration = new ImageConfiguration { Limits = limits } }))
            {
                Assert.Equal(selected, image.Frames.Count);
            }

            using (var reader = Image.OpenReader<Rgba64>(new MemoryStream(data), new ImageReaderOptions { Conversion = Conversion, FrameLimit = frameLimit, Configuration = new ImageConfiguration { Limits = limits } }))
            {
                Assert.Equal(selected, ReadFrames(reader));
            }

            var tight = Limits(ImageResourceLimitKind.Frames, selected + poster - 1 is var value && value > 0 ? value : 1);
            if (selected + poster - 1 > 0)
            {
                var exception = Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, new ImageDecodeOptions { FrameLimit = frameLimit, Configuration = new ImageConfiguration { Limits = tight } }).Dispose());
                AssertLimitException(exception, ImageResourceLimitKind.Frames, selected + poster - 1, selected + poster);
                var readerException = Assert.Throws<ImageResourceLimitException>(() =>
                {
                    using var reader = Image.OpenReader<Rgba64>(new MemoryStream(data), new ImageReaderOptions { Conversion = Conversion, FrameLimit = frameLimit, Configuration = new ImageConfiguration { Limits = tight } });
                    ReadFrames(reader);
                });
                AssertLimitException(readerException, ImageResourceLimitKind.Frames, selected + poster - 1, selected + poster);
            }
        }
    }

    public static TheoryData<string> AnimationIds => [.. GoldenCorpus.Default.GetIds(kind: FixtureKinds.Valid).Where(id => GoldenCorpus.Default.Get(id) is { Entry.DecodeOptions: null, Expected.FrameCount: > 1 })];

    private static int ReadFrames(ImageReader<Rgba64> reader)
    {
        if (reader.Info.HasPosterFrame == true)
        {
            reader.ReadPosterFrame()?.Dispose();
        }

        var count = 0;
        while (reader.ReadFrame() is { } frame)
        {
            frame.Dispose();
            count++;
        }

        return count;
    }

    private static void AssertExactBoundary(string context, byte[] data, ImageResourceLimitKind kind, long requirement)
    {
        var accepted = Run(context, data, Limits(kind, requirement));
        Assert.Null(accepted, $"{context}: {kind} = {requirement} (the requirement) must be accepted: {accepted}");
        if (requirement <= 1)
            return; // no positive limit below the requirement

        var rejected = Run(context, data, Limits(kind, requirement - 1));
        Assert.NotNull(rejected, $"{context}: {kind} = {requirement - 1} (one below the requirement) must fail");
        AssertLimitException(rejected, kind, requirement - 1, requirement);
    }

    /// <summary>Bisects the smallest accepted limit, checks the boundary and returns it.</summary>
    private static long Measure(string context, byte[] data, ImageResourceLimitKind kind, long upper)
    {
        Assert.Null(Run(context, data, Limits(kind, upper)));
        var low = 1L; // smallest candidate
        var high = upper; // accepted
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (Run(context, data, Limits(kind, middle)) is null)
            {
                high = middle;
            }
            else
            {
                low = middle + 1;
            }
        }

        if (high > 1)
        {
            var rejected = Run(context, data, Limits(kind, high - 1));
            Assert.NotNull(rejected, $"{context}: {kind} = {high - 1} must fail");
            AssertLimitException(rejected, kind, high - 1, requested: null);
        }

        return high;
    }

    private static void AssertLimitException(ImageResourceLimitException exception, ImageResourceLimitKind kind, long limit, long? requested)
    {
        Assert.Equal(kind, exception.Kind);
        Assert.Equal(limit, exception.Limit);
        if (requested is { } exact)
        {
            Assert.Equal(exact, exception.Requested);
        }
        else
        {
            Assert.True(exception.Requested is null || exception.Requested > limit, $"Requested {exception.Requested} must exceed the limit {limit}");
        }
    }

    private static ImageResourceLimitException? Run(string context, byte[] data, ImageResourceLimits limits)
    {
        var configuration = new ImageConfiguration { Limits = limits };
        try
        {
            switch (context)
            {
                case "load":
                    Image.Load(data, new ImageDecodeOptions { Configuration = configuration }).Dispose();
                    break;
                case "reader":
                    using (var reader = Image.OpenReader<Rgba64>(new MemoryStream(data), new ImageReaderOptions { Configuration = configuration, Conversion = Conversion }))
                    {
                        ReadFrames(reader);
                    }

                    break;
                default:
                    Image.Identify(data, new ImageIdentifyOptions { Configuration = configuration, Mode = context == "identify-full" ? ImageIdentifyMode.FullScan : ImageIdentifyMode.Header });
                    break;
            }

            return null;
        }
        catch (ImageResourceLimitException exception)
        {
            return exception;
        }
    }

    private static ImageResourceLimits Limits(ImageResourceLimitKind kind, long value) => kind switch
    {
        ImageResourceLimitKind.Width => new ImageResourceLimits { MaxWidth = checked((int)value) },
        ImageResourceLimitKind.Height => new ImageResourceLimits { MaxHeight = checked((int)value) },
        ImageResourceLimitKind.FramePixels => new ImageResourceLimits { MaxFramePixels = value },
        ImageResourceLimitKind.Frames => new ImageResourceLimits { MaxFrames = checked((int)value) },
        ImageResourceLimitKind.TotalPixels => new ImageResourceLimits { MaxTotalPixels = value },
        ImageResourceLimitKind.EncodedBytes => new ImageResourceLimits { MaxEncodedBytes = value },
        ImageResourceLimitKind.MetadataBytes => new ImageResourceLimits { MaxMetadataBytes = value },
        ImageResourceLimitKind.LiveAllocationBytes => new ImageResourceLimits { MaxLiveAllocationBytes = value },
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
