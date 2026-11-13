using System.Security.Cryptography;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Hevc;
using Meziantou.Framework.Imaging.Tests.Conformance.Hardening;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// The HEVC decoder on the conformance vectors of the corpus (<c>hevc/*.hevc</c>, the <c>hevcVectors</c> section of the
/// manifest), below the HEIF container and the color conversion: every output picture, in output order, must have the planes
/// on which at least two independent decoders agree (their SHA-256, cropped to the conformance window), and the hash that
/// its decoded picture hash SEI message carries (the whole picture). HEVC decoding is normative: there is no tolerance.
/// The vectors that use what the decoder does not implement are rejected as unsupported, never decoded to something else.
/// </summary>
public sealed class HevcDecoderConformanceTests
{
    public static TheoryData<string> SupportedVectors => [.. GoldenCorpus.Default.HevcVectors.Where(vector => !vector.HasFeatureKey("hevc.unsupported")).Select(vector => vector.Id)];

    public static TheoryData<string> SubstreamVectors => [.. GoldenCorpus.Default.HevcVectors.Where(vector => !vector.HasFeatureKey("hevc.unsupported") && (vector.HasFeatureKey("hevc.tiles") || vector.HasFeatureKey("hevc.wpp"))).Select(vector => vector.Id)];

    public static TheoryData<string> UnsupportedVectors => [.. GoldenCorpus.Default.HevcVectors.Where(vector => vector.HasFeatureKey("hevc.unsupported")).Select(vector => vector.Id)];

    [Theory]
    [MemberData(nameof(SupportedVectors))]
    public void VectorsDecodeToThePlanesOfTheReferenceDecoders(string id)
    {
        var corpus = GoldenCorpus.Default;
        var vector = corpus.GetHevcVector(id);
        Assert.True(vector.HasDigests, $"{id}: a vector that the decoder supports has the digests of two decoders that agree.");
        var pictures = Decode(corpus, vector, out _);
        Assert.HasCount(vector.Frames.Count, pictures, $"{id}: the number of output pictures is not the one of the reference decoders.");
        for (var i = 0; i < pictures.Count; i++)
        {
            var picture = pictures[i];
            var frame = vector.Frames[i];
            var sps = picture.Sps;
            Assert.True((frame.Width, frame.Height) == (sps.OutputWidth, sps.OutputHeight), $"{id}: the output picture {i} is {sps.OutputWidth}x{sps.OutputHeight}, {frame.Width}x{frame.Height} expected.");
            Assert.HasCount(frame.Planes.Count, picture.Planes, $"{id}: the output picture {i} does not have the planes of the reference decoders.");
            for (var plane = 0; plane < picture.Planes.Length; plane++)
            {
                var digest = GetPlaneDigest(picture, plane);
                Assert.Equal(frame.Planes[plane], digest, ignoreCase: true, message: $"{id}: the plane {plane} of the output picture {i} (picture order count {picture.PicOrderCntVal}) is not the plane of the reference decoders ({string.Join(" and ", vector.Decoders.Agree)}).");
            }

            // The hash of the stream is the one of the whole picture: it also covers the samples outside the conformance
            // window. A hash that no reference decoder verified is not an expectation
            if (vector.PictureHash.VerifiedBy.Count > 0)
                HevcTestDecoder.AssertMatchesHash(picture, id);
        }
    }

    /// <summary>
    /// The vectors with tiles or rows of the wavefront, by several workers: the substreams of each slice segment that start
    /// at an entry point are decoded concurrently whatever the size of the picture
    /// (<see cref="HevcDecoder.TestIgnoreMinimumSize"/>). The planes must still have the digests of the reference decoders.
    /// </summary>
    [Theory]
    [MemberData(nameof(SubstreamVectors))]
    public void VectorsDecodeToTheSamePlanesWithSeveralWorkers(string id)
    {
        var corpus = GoldenCorpus.Default;
        var vector = corpus.GetHevcVector(id);
        var expected = Decode(corpus, vector, out var expectedTools);
        HevcDecoder.TestIgnoreMinimumSize = true;
        try
        {
            foreach (var workers in (int[])[2, 3, 16])
            {
                var pictures = Decode(corpus, vector, out var tools, workers);
                Assert.Equal(expectedTools, tools, $"{id}: {workers} workers do not report the tools of one worker.");
                Assert.HasCount(vector.Frames.Count, pictures, $"{id}: {workers} workers do not output the pictures of the reference decoders.");
                for (var i = 0; i < pictures.Count; i++)
                {
                    for (var plane = 0; plane < pictures[i].Planes.Length; plane++)
                    {
                        Assert.Equal(vector.Frames[i].Planes[plane], GetPlaneDigest(pictures[i], plane), ignoreCase: true, message: $"{id}: the plane {plane} of the output picture {i} decoded by {workers} workers is not the plane of the reference decoders.");
                        Assert.True(expected[i].Planes[plane].AsSpan().SequenceEqual(pictures[i].Planes[plane]), $"{id}: the plane {plane} of the output picture {i} decoded by {workers} workers is not the plane of one worker.");
                    }
                }
            }
        }
        finally
        {
            HevcDecoder.TestIgnoreMinimumSize = false;
        }
    }

    /// <summary>
    /// Every vector by 3 workers, whatever the size of its pictures (<see cref="HevcDecoder.TestIgnoreMinimumSize"/>): the
    /// deblocking filter and the sample adaptive offset of each picture process bands of rows at the same time. The planes
    /// must still have the digests of the reference decoders.
    /// </summary>
    [Theory]
    [MemberData(nameof(SupportedVectors))]
    public void VectorsDecodeToThePlanesOfTheReferenceDecodersWithBandsOfInLoopFilters(string id)
    {
        var corpus = GoldenCorpus.Default;
        var vector = corpus.GetHevcVector(id);
        HevcDecoder.TestIgnoreMinimumSize = true;
        try
        {
            var pictures = Decode(corpus, vector, out _, maxDegreeOfParallelism: 3);
            Assert.HasCount(vector.Frames.Count, pictures, $"{id}: 3 workers do not output the pictures of the reference decoders.");
            for (var i = 0; i < pictures.Count; i++)
            {
                for (var plane = 0; plane < pictures[i].Planes.Length; plane++)
                {
                    Assert.Equal(vector.Frames[i].Planes[plane], GetPlaneDigest(pictures[i], plane), ignoreCase: true, message: $"{id}: the plane {plane} of the output picture {i} filtered by bands is not the plane of the reference decoders.");
                }
            }
        }
        finally
        {
            HevcDecoder.TestIgnoreMinimumSize = false;
        }
    }

    /// <summary>
    /// The decode window (<see cref="HevcDecoder.DecodeWindow"/>): the first picture of every vector, decoded for areas of
    /// it, by one worker and by bands of in-loop filters, has in each area the samples of the whole picture. A first picture
    /// that is not made of I slices is not decoded for a window (<see cref="DecodeWindowNotApplicableException"/>).
    /// </summary>
    [Theory]
    [MemberData(nameof(SupportedVectors))]
    public void ADecodeWindowHasTheSamplesOfTheWholePicture(string id)
    {
        var input = GetFirstAccessUnit(id);
        var whole = DecodeFirstPicture(input, window: null, out var sps, out _);
        HevcDecoder.TestIgnoreMinimumSize = true;
        try
        {
            foreach (var window in GetDecodeWindows(sps.OutputWidth, sps.OutputHeight))
            {
                foreach (var workers in (int[])[1, 3])
                {
                    ushort[][] planes;
                    int decodedHeight;
                    try
                    {
                        planes = DecodeFirstPicture(input, window, out _, out decodedHeight, workers);
                    }
                    catch (DecodeWindowNotApplicableException)
                    {
                        continue;
                    }

                    Assert.True(decodedHeight >= Math.Min(sps.PicHeightInLumaSamples, sps.OutputTop + window.Bottom), $"{id}: the window {window} decodes {decodedHeight} rows.");
                    for (var plane = 0; plane < planes.Length; plane++)
                    {
                        var subWidth = plane is 0 ? 1 : sps.SubWidthC;
                        var subHeight = plane is 0 ? 1 : sps.SubHeightC;
                        var width = sps.PicWidthInLumaSamples / subWidth;
                        for (var y = (sps.OutputTop + window.Top) / subHeight; y <= (sps.OutputTop + window.Bottom - 1) / subHeight; y++)
                        {
                            for (var x = (sps.OutputLeft + window.Left) / subWidth; x <= (sps.OutputLeft + window.Right - 1) / subWidth; x++)
                            {
                                if (whole[plane][(y * width) + x] != planes[plane][(y * width) + x])
                                    Assert.Fail($"{id}: the sample ({x}, {y}) of the plane {plane} decoded for the window {window} by {workers} worker(s) differs from the whole picture.");
                            }
                        }
                    }
                }
            }
        }
        finally
        {
            HevcDecoder.TestIgnoreMinimumSize = false;
        }
    }

    /// <summary>The vectors exercise the decode window: many first pictures stop before their last rows, with slices and rows of the wavefront.</summary>
    [Fact]
    public void ADecodeWindowStopsTheDecodingOfManyVectors()
    {
        var windowed = 0;
        var substreams = 0;
        foreach (var id in GoldenCorpus.Default.HevcVectors.Where(vector => !vector.HasFeatureKey("hevc.unsupported")).Select(vector => vector.Id))
        {
            try
            {
                var input = GetFirstAccessUnit(id);
                DecodeFirstPicture(input, new Rectangle(0, 0, 1, 1), out var sps, out var decodedHeight);
                if (decodedHeight < sps.PicHeightInLumaSamples)
                {
                    windowed++;
                    var vector = GoldenCorpus.Default.GetHevcVector(id);
                    substreams += vector.HasFeatureKey("hevc.wpp") || vector.HasFeatureKey("hevc.tiles") ? 1 : 0;
                }
            }
            catch (DecodeWindowNotApplicableException)
            {
            }
        }

        Assert.True(windowed >= 50, $"Only {windowed} vectors are decoded for a window.");
        Assert.True(substreams >= 3, $"Only {substreams} vectors with substreams are decoded for a window.");
    }

    // Areas of an output picture: single pixels, rows and columns, areas across coding tree blocks, the whole picture
    private static Rectangle[] GetDecodeWindows(int width, int height) =>
    [
        new(0, 0, 1, 1),
        new(width - 1, height - 1, 1, 1),
        new(width / 3, height / 4, 1, 1),
        new(width / 2, 0, width - (width / 2), Math.Max(1, height / 3)),
        new(0, height / 2, width, 1),
        new(width * 3 / 4, height / 3, Math.Min(9, width - (width * 3 / 4)), Math.Min(9, height - (height / 3))),
        new(0, 0, width, height),
    ];

    private static byte[] GetFirstAccessUnit(string id)
    {
        var corpus = GoldenCorpus.Default;
        var data = File.ReadAllBytes(corpus.GetHevcVectorPath(corpus.GetHevcVector(id)));
        return ReferenceHevcStream.ToLengthPrefixed(ReferenceHevcStream.GroupAccessUnits(ReferenceHevcStream.SplitAnnexB(data))[0]);
    }

    // The planes of the first picture of an access unit, decoded whole or for a window: the rows that are not decoded are zero
    private static ushort[][] DecodeFirstPicture(byte[] input, Rectangle? window, out HevcSequenceParameterSet sps, out int decodedHeight, int maxDegreeOfParallelism = 1)
    {
        using var decoder = new HevcDecoder(AllocationScope.Create(ImageConfiguration.Default, "window")) { DecodeWindow = window, MaxDegreeOfParallelism = maxDegreeOfParallelism };
        decoder.SetInput(input, 0, input.Length, 4);
        var picture = decoder.DecodeNextOutputPicture(TestContext.Current.CancellationToken);
        if (picture is null)
        {
            decoder.EndOfSequence();
            picture = decoder.DecodeNextOutputPicture(TestContext.Current.CancellationToken);
        }

        Assert.NotNull(picture);
        try
        {
            sps = picture.Sps;
            decodedHeight = picture.DecodedHeight;
            var planes = new ushort[picture.PlaneCount][];
            for (var i = 0; i < planes.Length; i++)
            {
                var plane = picture.GetPlane(i);
                var fullHeight = i is 0 ? sps.PicHeightInLumaSamples : sps.PicHeightInLumaSamples / sps.SubHeightC;
                planes[i] = new ushort[plane.Width * fullHeight];
                for (var y = 0; y < plane.Height; y++)
                {
                    plane.GetRow(y).CopyTo(planes[i].AsSpan(y * plane.Width));
                }
            }

            return planes;
        }
        finally
        {
            picture.Release();
        }
    }

    [Theory]
    [MemberData(nameof(UnsupportedVectors))]
    public void VectorsOfUnsupportedFeaturesAreRejected(string id)
    {
        var corpus = GoldenCorpus.Default;
        var vector = corpus.GetHevcVector(id);
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Decode(corpus, vector, out _));
        Assert.Equal(ImageFormat.Heic, exception.Format);
        Assert.False(string.IsNullOrEmpty(exception.Feature));
    }

    [Fact]
    public void TheCorpusHasVectorsOfBothKinds()
    {
        Assert.True(SupportedVectors.Count >= 300, "The corpus lost HEVC vectors.");
        Assert.NotEmpty(UnsupportedVectors);
    }

    internal static List<HevcTestDecoder.DecodedPicture> Decode(GoldenCorpus corpus, HevcVectorEntry vector, out HevcTools tools, int maxDegreeOfParallelism = 1)
    {
        var data = File.ReadAllBytes(corpus.GetHevcVectorPath(vector));
        using var audit = new PoolAudit();
        List<HevcTestDecoder.DecodedPicture> pictures;
        try
        {
            pictures = HevcTestDecoder.Decode(data, new AllocationScope(ImageResourceLimits.Default, audit.Pool, vector.Id), out tools, maxDegreeOfParallelism: maxDegreeOfParallelism);
        }
        finally
        {
            audit.AssertClean(vector.Id);
        }

        return pictures;
    }

    // The digest of the corpus: the samples of the conformance window, rows in order, one byte per sample up to 8 bits and
    // two bytes, least significant first, above
    private static string GetPlaneDigest(HevcTestDecoder.DecodedPicture picture, int plane)
    {
        var sps = picture.Sps;
        var subWidth = plane is 0 ? 1 : sps.SubWidthC;
        var subHeight = plane is 0 ? 1 : sps.SubHeightC;
        var left = sps.OutputLeft / subWidth;
        var top = sps.OutputTop / subHeight;
        var width = sps.OutputWidth / subWidth;
        var height = sps.OutputHeight / subHeight;
        var stride = picture.GetWidth(plane);
        var samples = picture.Planes[plane];
        var bytesPerSample = sps.BitDepth > 8 ? 2 : 1;
        var bytes = new byte[width * height * bytesPerSample];
        var position = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var sample = samples[((top + y) * stride) + left + x];
                bytes[position++] = (byte)sample;
                if (bytesPerSample is 2)
                    bytes[position++] = (byte)(sample >> 8);
            }
        }

        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
