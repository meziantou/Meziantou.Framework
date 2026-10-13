using System.Security.Cryptography;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Golden;
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

    internal static List<HevcTestDecoder.DecodedPicture> Decode(GoldenCorpus corpus, HevcVectorEntry vector, out HevcTools tools)
    {
        var data = File.ReadAllBytes(corpus.GetHevcVectorPath(vector));
        using var audit = new PoolAudit();
        List<HevcTestDecoder.DecodedPicture> pictures;
        try
        {
            pictures = HevcTestDecoder.Decode(data, new AllocationScope(ImageResourceLimits.Default, audit.Pool, vector.Id), out tools);
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
