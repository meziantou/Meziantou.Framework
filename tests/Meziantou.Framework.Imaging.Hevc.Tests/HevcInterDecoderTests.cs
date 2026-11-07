using System.Security.Cryptography;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.TestHarness.Hevc;
using Meziantou.Framework.Imaging.Tests.Conformance.Hardening;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The HEVC decoder on small streams with pictures predicted from other pictures: the planes of every picture have the hash
/// that the stream carries for it, and the pictures come out in output order. The streams were also compared with the
/// output of the reference decoders when they were made.
/// </summary>
public sealed class HevcInterDecoderTests
{
    // The first tool of the inter prediction: the tools before it are those of the intra pictures
    private const HevcTools Reordering = HevcTools.PictureReordering;

    public static TheoryData<string, string, ulong> Streams => new()
    {
        // name, the access units of the pictures in output order, tools that the stream reaches
        { nameof(HevcTestStreams.X265Inter), "0 1", (ulong)(HevcTools.PSlices | HevcTools.InterSkip) },
        { nameof(HevcTestStreams.X265P420), "0 1 2 3 4 5 6 7", (ulong)(HevcTools.PSlices | HevcTools.InterSkip | HevcTools.InterMerge | HevcTools.InterMvd | HevcTools.FractionalInterpolation | HevcTools.MultipleReferences | HevcTools.ReferencePadding) },
        { nameof(HevcTestStreams.X265B422TenBits), "0 2 3 1", (ulong)(HevcTools.BSlices | HevcTools.InterBiPrediction | Reordering | HevcTools.MergeCombinedCandidates | HevcTools.TemporalMvp | HevcTools.Chroma422Split) },
        { nameof(HevcTestStreams.HmWeighted422TenBits), "0 2 1 3", (ulong)(HevcTools.ExplicitWeightedPrediction | HevcTools.BSlices | HevcTools.InterBiPrediction | Reordering) },
        { nameof(HevcTestStreams.HmAmp444), "0 2 1 4 3", (ulong)(HevcTools.InterAsymmetricPartitions | HevcTools.InterRectangularPartitions | HevcTools.MvpScaling | Reordering) },
        { nameof(HevcTestStreams.HmRandomAccess400TenBits), "0 3 2 4 1 5", (ulong)(HevcTools.BSlices | Reordering | HevcTools.InterTransformSplit | HevcTools.IntraInInterSlices | HevcTools.TemporalMvp | HevcTools.Tiles) },
        { nameof(HevcTestStreams.HmInterSlices420TwelveBits), "0 3 2 4 1", (ulong)(HevcTools.DependentSliceSegments | HevcTools.MultipleSlices | HevcTools.InterBiPrediction | Reordering) },
        { nameof(HevcTestStreams.KvazaarInterTiles420), "0 1 2 3 4", (ulong)(HevcTools.Tiles | HevcTools.PSlices | HevcTools.InterMvd | HevcTools.InterSkip) },
        { nameof(HevcTestStreams.KvazaarInterWavefront400), "0 1 2 3 4 5", (ulong)(HevcTools.Wavefront | HevcTools.BSlices | HevcTools.InterMerge) },
        { nameof(HevcTestStreams.HmExplicitRdpcm420TwelveBits), "0 3 2 4 1 6 5 7", (ulong)(HevcTools.ExplicitRdpcm | HevcTools.BSlices | Reordering) },
        { nameof(HevcTestStreams.HmCrossComponentInter444TenBits), "0 3 2 4 1 7 6 8 5 9", (ulong)(HevcTools.CrossComponentPrediction | HevcTools.InterMvd | HevcTools.BSlices | Reordering) },
        { nameof(HevcTestStreams.HmLowDelayP400TenBits), "0 1 2 3 4", (ulong)(HevcTools.PSlices | HevcTools.InterMvd | HevcTools.CuQpDelta) },
        { nameof(HevcTestStreams.X265OpenGop420), "0 2 3 1 5 6 4 7", (ulong)(HevcTools.BSlices | Reordering) },
        { nameof(HevcTestStreams.X265Radl420), "0 2 3 1 5 6 4 7", (ulong)(HevcTools.BSlices | Reordering) },
    };

    private static byte[] GetStream(string name) => (byte[])typeof(HevcTestStreams).GetProperty(name)!.GetValue(null)!;

    private static int[] ParseOrder(string order) => [.. order.Split(' ').Select(value => int.Parse(value, CultureInfo.InvariantCulture))];

    private static AllocationScope CreateScope(string name) => AllocationScope.Create(ImageConfiguration.Default, name);

    [Theory]
    [MemberData(nameof(Streams))]
    public void PicturesHaveTheHashOfTheirStreamInOutputOrder(string name, string order, ulong expected)
    {
        var expectedTools = (HevcTools)expected;
        using var audit = new PoolAudit();
        var scope = new AllocationScope(ImageResourceLimits.Default, audit.Pool, name);
        var pictures = HevcTestDecoder.Decode(GetStream(name), scope, out var tools);
        Assert.Equal(ParseOrder(order), pictures.Select(picture => picture.AccessUnitIndex).ToArray());
        foreach (var picture in pictures)
        {
            HevcTestDecoder.AssertMatchesHash(picture, name);
        }

        Assert.Equal(expectedTools, tools & expectedTools, $"{name} does not reach {expectedTools & ~tools}");

        // Every buffer of the decoder is returned: the pictures of the decoded picture buffer, their motion, the working
        // buffers of the prediction
        Assert.Null(audit.Verify());
    }

    [Fact]
    public void PictureOrderCountsFollowTheCodedVideoSequences()
    {
        // One coded video sequence: the pictures come out by increasing picture order count
        var openGop = HevcTestDecoder.Decode(HevcTestStreams.X265OpenGop420, CreateScope(nameof(HevcTestStreams.X265OpenGop420)), out _);
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, openGop.Select(picture => picture.PicOrderCntVal).ToArray());

        // A coded video sequence for each IDR picture, whose picture order count is 0: the RADL pictures of an IDR picture
        // precede it in output order
        var radl = HevcTestDecoder.Decode(HevcTestStreams.X265Radl420, CreateScope(nameof(HevcTestStreams.X265Radl420)), out _);
        Assert.Equal(new[] { 0, -2, -1, 0, -2, -1, 0, 1 }, radl.Select(picture => picture.PicOrderCntVal).ToArray());

        // An IDR picture in the fifth access unit
        var kvazaar = HevcTestDecoder.Decode(HevcTestStreams.KvazaarInterWavefront400, CreateScope(nameof(HevcTestStreams.KvazaarInterWavefront400)), out _);
        Assert.Equal(new[] { 0, 1, 2, 3, 0, 1 }, kvazaar.Select(picture => picture.PicOrderCntVal).ToArray());
    }

    [Theory]
    [InlineData(nameof(HevcTestStreams.HmCrossComponentInter444TenBits))]
    [InlineData(nameof(HevcTestStreams.HmExplicitRdpcm420TwelveBits))]
    [InlineData(nameof(HevcTestStreams.X265B422TenBits))]
    [InlineData(nameof(HevcTestStreams.X265P420))]
    public void APictureIsOutputWithinTheReorderBoundOfTheSequence(string name)
    {
        var accessUnits = ReferenceHevcStream.GroupAccessUnits(ReferenceHevcStream.SplitAnnexB(GetStream(name)));
        using var decoder = new HevcDecoder(CreateScope(name));
        Assert.Equal(0, decoder.MaxNumReorderPictures);
        var output = 0;
        var last = int.MinValue;
        for (var i = 0; i < accessUnits.Count; i++)
        {
            var data = ReferenceHevcStream.ToLengthPrefixed(accessUnits[i]);
            decoder.SetInput(data, 0, data.Length, nalLengthSize: 4);
            while (decoder.DecodeNextOutputPicture(XunitCancellationToken) is { } picture)
            {
                // Output order
                Assert.True(picture.PicOrderCntVal > last);
                last = picture.PicOrderCntVal;
                output++;
                picture.Release();
            }

            // At most sps_max_num_reorder_pics pictures wait for their turn
            Assert.True(output >= i + 1 - decoder.MaxNumReorderPictures, $"{output} pictures after {i + 1} access units");
        }

        decoder.EndOfSequence();
        while (decoder.DecodeNextOutputPicture(XunitCancellationToken) is { } picture)
        {
            Assert.True(picture.PicOrderCntVal > last);
            last = picture.PicOrderCntVal;
            output++;
            picture.Release();
        }

        Assert.Equal(accessUnits.Count, output);
    }

    [Fact]
    [SuppressMessage("Security", "CA5351:Do Not Use Broken Cryptographic Algorithms", Justification = "A digest of test data.")]
    public void LongTermReferencePicturesAndModifiedLists()
    {
        // No pinned encoder writes long-term reference pictures nor modifies its reference picture lists: the streams are
        // streams of the HM whose headers were rewritten, and their pictures are those that the reference decoders gave
        // for them (ffmpeg, libde265 and the HM agreed on the first one; ffmpeg and libde265 on the second one)
        var pictures = HevcTestDecoder.Decode(HevcTestStreams.HmLongTermRewritten400TenBits, CreateScope(nameof(HevcTestStreams.HmLongTermRewritten400TenBits)), out var tools);
        Assert.HasCount(5, pictures);
        Assert.Equal(HevcTools.LongTermReferences | HevcTools.ReferenceListModification | HevcTools.TemporalMvp, tools & (HevcTools.LongTermReferences | HevcTools.ReferenceListModification | HevcTools.TemporalMvp));
        Assert.Equal("5034E95AFC90A2A5E4D5F8746B967047", GetDigest(pictures));

        pictures = HevcTestDecoder.Decode(HevcTestStreams.HmLongTermRewrittenB420, CreateScope(nameof(HevcTestStreams.HmLongTermRewrittenB420)), out tools);
        Assert.HasCount(7, pictures);
        Assert.Equal(HevcTools.LongTermReferences | HevcTools.BSlices, tools & (HevcTools.LongTermReferences | HevcTools.BSlices | HevcTools.ReferenceListModification));
        Assert.Equal("E3217A106AFF949BFD87988A7A10C9EB", GetDigest(pictures));

        // The stream of the encoder, whose pictures are other pictures
        pictures = HevcTestDecoder.Decode(HevcTestStreams.HmLowDelayP400TenBits, CreateScope(nameof(HevcTestStreams.HmLowDelayP400TenBits)), out tools);
        Assert.Equal(HevcTools.None, tools & (HevcTools.LongTermReferences | HevcTools.ReferenceListModification));
        Assert.Equal("28A065600D9821AB8AA02CC528910501", GetDigest(pictures));

        static string GetDigest(List<HevcTestDecoder.DecodedPicture> pictures)
        {
            // The samples of every plane of every picture, on 16 bits
            using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
            foreach (var plane in pictures.SelectMany(picture => picture.Planes))
            {
                var bytes = new byte[plane.Length * 2];
                for (var i = 0; i < plane.Length; i++)
                {
                    bytes[i * 2] = (byte)plane[i];
                    bytes[(i * 2) + 1] = (byte)(plane[i] >> 8);
                }

                md5.AppendData(bytes);
            }

            return Convert.ToHexString(md5.GetHashAndReset());
        }
    }

    [Fact]
    public void TheRewriterOfTheTestStreamsGivesTheStreamsOfTheTests()
    {
        var options = HevcTestStreamRewriter.Options.SpsCandidate | HevcTestStreamRewriter.Options.MsbPresent | HevcTestStreamRewriter.Options.ModifyLists;
        var rewritten = HevcTestStreamRewriter.Rewrite(HevcTestStreams.HmLowDelayP400TenBits, options);
        Assert.NotNull(rewritten);
        Assert.Equal(HevcTestStreams.HmLongTermRewritten400TenBits, rewritten);

        // Every way of signalling the long-term reference picture gives a stream that is decoded, with the same number of
        // pictures: the pictures themselves are only known for the streams above
        for (var value = 0; value < 8; value++)
        {
            options = (HevcTestStreamRewriter.Options)value;
            rewritten = HevcTestStreamRewriter.Rewrite(HevcTestStreams.HmLowDelayP400TenBits, options);
            Assert.NotNull(rewritten);
            var pictures = HevcTestDecoder.Decode(rewritten, CreateScope(nameof(TheRewriterOfTheTestStreamsGivesTheStreamsOfTheTests)), out var tools);
            Assert.HasCount(5, pictures);
            Assert.True(tools.HasFlag(HevcTools.LongTermReferences));
            Assert.Equal(options.HasFlag(HevcTestStreamRewriter.Options.ModifyLists), tools.HasFlag(HevcTools.ReferenceListModification));
        }
    }

    // The access units of the open GOP stream: an IDR picture, a CRA picture and its two RASL pictures, a second CRA
    // picture and its two RASL pictures, a trailing picture
    private static List<List<ReadOnlyMemory<byte>>> GetOpenGopAccessUnits()
    {
        var accessUnits = ReferenceHevcStream.GroupAccessUnits(ReferenceHevcStream.SplitAnnexB(HevcTestStreams.X265OpenGop420)).Select(accessUnit => accessUnit.ToList()).ToList();
        Assert.Equal(new[] { 20, 21, 8, 8, 21, 8, 8, 1 }, accessUnits.Select(accessUnit => ReferenceHevcStream.GetNalUnitType(accessUnit.First(unit => ReferenceHevcStream.GetNalUnitType(unit.Span) < 32).Span)).ToArray());
        return accessUnits;
    }

    private static readonly byte[] EndOfSequence = [36 << 1, 1];
    private static readonly byte[] EndOfBitstream = [37 << 1, 1];

    private static List<(int AccessUnitIndex, int PicOrderCntVal)> DecodeAccessUnits(IEnumerable<IEnumerable<ReadOnlyMemory<byte>>> accessUnits, InputResourceTracker? tracker = null)
    {
        using var audit = new PoolAudit();
        var result = new List<(int, int)>();
        var scope = new AllocationScope(ImageResourceLimits.Default, audit.Pool, nameof(DecodeAccessUnits));
        using (var decoder = new HevcDecoder(scope, tracker))
        {
            foreach (var accessUnit in accessUnits)
            {
                var data = ReferenceHevcStream.ToLengthPrefixed(accessUnit);
                decoder.SetInput(data, 0, data.Length, nalLengthSize: 4);
                Drain(decoder);
            }

            decoder.EndOfSequence();
            Drain(decoder);
        }

        Assert.Null(audit.Verify());
        return result;

        void Drain(HevcDecoder decoder)
        {
            while (decoder.DecodeNextOutputPicture(XunitCancellationToken) is { } picture)
            {
                try
                {
                    // The pictures that are output are the pictures of the encoder
                    Assert.NotNull(picture.DecodedPictureHash);
                    for (var cIdx = 0; cIdx < picture.PlaneCount && picture.DecodedPictureHash.HashType is HevcDecodedPictureHash.Md5; cIdx++)
                    {
                        var plane = picture.GetPlane(cIdx);
                        Assert.Equal(Convert.ToHexString(picture.DecodedPictureHash.GetMd5(cIdx)), Convert.ToHexString(ReferenceHevcPictureHash.ComputeMd5(plane.Samples, plane.Width, plane.Height, plane.Stride, picture.Sps.BitDepth)));
                    }

                    result.Add((picture.AccessUnitIndex, picture.PicOrderCntVal));
                }
                finally
                {
                    picture.Release();
                }
            }
        }
    }

    [Fact]
    public void TheRaslPicturesOfTheFirstPictureAreNotOutput()
    {
        // The stream from its first CRA picture: the RASL pictures of that picture refer to the IDR picture, which is not
        // there, and are not decoded (NoRaslOutputFlag is 1); those of the second CRA picture are
        var accessUnits = GetOpenGopAccessUnits();
        var parameterSets = accessUnits[0].Where(unit => ReferenceHevcStream.GetNalUnitType(unit.Span) is >= 32 and <= 34).ToList();
        var cut = accessUnits.Skip(1).ToList();
        cut[0] = [.. parameterSets, .. cut[0]];
        Assert.Equal(new[] { (0, 3), (4, 4), (5, 5), (3, 6), (6, 7) }, DecodeAccessUnits(cut).ToArray());

        // The whole stream: every picture
        Assert.Equal(new[] { (0, 0), (2, 1), (3, 2), (1, 3), (5, 4), (6, 5), (4, 6), (7, 7) }, DecodeAccessUnits(accessUnits).ToArray());
    }

    [Theory]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(18)]
    public void TheRaslPicturesOfABlaPictureAreNotOutput(int nalUnitType)
    {
        // The second CRA picture as a BLA picture: its RASL pictures are not decoded, the pictures before it are output,
        // and its picture order count is the one of its least significant bits
        var accessUnits = GetOpenGopAccessUnits();
        accessUnits[4] = [.. accessUnits[4].Select(unit =>
        {
            if (ReferenceHevcStream.GetNalUnitType(unit.Span) is not 21)
                return unit;

            var copy = unit.ToArray();
            copy[0] = (byte)((copy[0] & 0x81) | (nalUnitType << 1));
            return (ReadOnlyMemory<byte>)copy;
        })];
        Assert.Equal(new[] { (0, 0), (2, 1), (3, 2), (1, 3), (4, 6), (7, 7) }, DecodeAccessUnits(accessUnits).ToArray());
    }

    [Fact]
    public void AnEndOfSequenceStartsACodedVideoSequence()
    {
        // Twice the stream, which starts with an IDR picture whose no_output_of_prior_pics_flag is 0: every picture is
        // output, with or without an end of sequence NAL unit
        var accessUnits = GetOpenGopAccessUnits();
        var twice = new[] { (0, 0), (2, 1), (3, 2), (1, 3), (5, 4), (6, 5), (4, 6), (7, 7), (8, 0), (10, 1), (11, 2), (9, 3), (13, 4), (14, 5), (12, 6), (15, 7) };
        Assert.Equal(twice, DecodeAccessUnits([.. accessUnits, .. accessUnits]).ToArray());
        var withEndOfSequence = accessUnits.Select(accessUnit => accessUnit.ToList()).ToList();
        withEndOfSequence[7].Add(EndOfSequence);
        Assert.Equal(twice, DecodeAccessUnits([.. withEndOfSequence, .. accessUnits]).ToArray());

        // The stream, an end of sequence NAL unit, then the stream from its first CRA picture: the picture is the first one
        // of a coded video sequence, so its RASL pictures are not decoded, and NoOutputOfPriorPicsFlag is 1 for a CRA
        // picture (clause C.5.2.2): the last picture of the first sequence, which waits for its turn, is never output.
        // Its samples are charged as hidden pixels.
        var cut = accessUnits.Skip(1).ToList();
        var tracker = new InputResourceTracker(ImageResourceLimits.Default);
        var expected = new[] { (0, 0), (2, 1), (3, 2), (1, 3), (5, 4), (6, 5), (4, 6), (8, 3), (12, 4), (13, 5), (11, 6), (14, 7) };
        Assert.Equal(expected, DecodeAccessUnits([.. withEndOfSequence, .. cut], tracker).ToArray());
        Assert.Equal(32 * 32, tracker.TotalPixels);

        // An end of bitstream NAL unit: the pictures of the bitstream that ends are all output
        var withEndOfBitstream = accessUnits.Select(accessUnit => accessUnit.ToList()).ToList();
        withEndOfBitstream[7].Add(EndOfBitstream);
        expected = [(0, 0), (2, 1), (3, 2), (1, 3), (5, 4), (6, 5), (4, 6), (7, 7), (8, 3), (12, 4), (13, 5), (11, 6), (14, 7)];
        Assert.Equal(expected, DecodeAccessUnits([.. withEndOfBitstream, .. cut]).ToArray());
    }

    [Fact]
    public void AnIdrPictureCanDiscardThePicturesThatAreNotOutputYet()
    {
        // no_output_of_prior_pics_flag, the second bit of the slice segment header, becomes 1 in the second and in the third
        // IDR pictures: the IDR picture before each of them waits for its turn when they come, and is never output
        var accessUnits = ReferenceHevcStream.GroupAccessUnits(ReferenceHevcStream.SplitAnnexB(HevcTestStreams.X265Radl420)).Select(accessUnit => accessUnit.ToList()).ToList();
        Assert.Equal(new[] { (0, 0), (2, -2), (3, -1), (1, 0), (5, -2), (6, -1), (4, 0), (7, 1) }, DecodeAccessUnits(accessUnits).ToArray());
        foreach (var index in new[] { 1, 4 })
        {
            accessUnits[index] = [.. accessUnits[index].Select(unit =>
            {
                if (ReferenceHevcStream.GetNalUnitType(unit.Span) is not 19)
                    return unit;

                var copy = unit.ToArray();
                copy[2] |= 0x40;
                return (ReadOnlyMemory<byte>)copy;
            })];
        }

        var tracker = new InputResourceTracker(ImageResourceLimits.Default);
        Assert.Equal(new[] { (2, -2), (3, -1), (5, -2), (6, -1), (4, 0), (7, 1) }, DecodeAccessUnits(accessUnits, tracker).ToArray());
        Assert.Equal(2 * 32 * 32, tracker.TotalPixels);
    }

    [Fact]
    public void APictureThatASliceRefersToIsInTheDecodedPictureBuffer()
    {
        // Nothing is concealed: without the second picture, the third one refers to a picture that is missing
        var accessUnits = ReferenceHevcStream.GroupAccessUnits(ReferenceHevcStream.SplitAnnexB(HevcTestStreams.X265P420)).ToList();
        var exception = Assert.Throws<InvalidImageContentException>(() => DecodeAccessUnits(accessUnits.Where((_, index) => index is not 1)));
        Assert.Contains("decoded picture buffer", exception.Message, StringComparison.Ordinal);

        // A stream starts with an IRAP picture
        var parameterSets = accessUnits[0].Where(unit => ReferenceHevcStream.GetNalUnitType(unit.Span) is >= 32 and <= 34).ToList();
        Assert.Throws<InvalidImageContentException>(() => DecodeAccessUnits([[.. parameterSets, .. accessUnits[1]], .. accessUnits.Skip(2)]));

        // As does a coded video sequence: the picture after an end of sequence NAL unit
        Assert.Throws<InvalidImageContentException>(() => DecodeAccessUnits([accessUnits[0], [.. accessUnits[1], EndOfSequence], .. accessUnits.Skip(2)]));
    }

    [Fact]
    public void ANonConformingSlicesStreamOfX265IsInvalid()
    {
        // x265 4.2 with slices=2:pools=1 writes a picture of 2x3 coding tree blocks in two slice segment NAL units: the
        // second one, whose slice_segment_address is 2, has 3 bytes of payload, in which its header does not even fit;
        // and the data of the first one never have end_of_slice_segment_flag equal to 1 within the picture (clause
        // 7.3.8.1). ffmpeg ("Overread slice header", "Skipping invalid undecodable NALU") and libde265 ("CTB outside of
        // image area (concealing stream error...)") conceal it into the same picture, which is not the picture of the
        // hash of the stream, and the HM stops on an assertion. Nothing is concealed here.
        var units = ReferenceHevcStream.SplitAnnexB(HevcTestStreams.X265BrokenSlices422);
        var slices = units.Where(unit => ReferenceHevcStream.GetNalUnitType(unit.Span) < 32).ToList();
        Assert.Equal(new[] { 94, 5 }, slices.Select(unit => unit.Length).ToArray());
        using var audit = new PoolAudit();
        Assert.Throws<InvalidImageContentException>(() => HevcTestDecoder.Decode(HevcTestStreams.X265BrokenSlices422, new AllocationScope(ImageResourceLimits.Default, audit.Pool, nameof(HevcTestStreams.X265BrokenSlices422)), out _));
        Assert.Null(audit.Verify());

        // The first slice segment alone is not a picture either
        Assert.Throws<InvalidImageContentException>(() => HevcTestDecoder.Decode(ReferenceHevcStream.ToAnnexB(units.Where(unit => !unit.Equals(slices[1]))), CreateScope(nameof(HevcTestStreams.X265BrokenSlices422)), out _));
    }
}
