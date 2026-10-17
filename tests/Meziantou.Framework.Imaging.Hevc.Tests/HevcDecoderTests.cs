using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.TestHarness.Hevc;
using Meziantou.Framework.Imaging.Tests.Conformance.Hardening;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The HEVC decoder on small streams of the pinned encoders: the planes of every picture have the hash that the stream
/// carries for it (Rec. ITU-T H.265, clause D.3.20), computed by the reference of the test harness.
/// </summary>
public sealed class HevcDecoderTests
{
    public static TheoryData<string, int, int, int, int, int, int, int> Streams => new()
    {
        // name, pictures, width and height of the pictures, of their conformance window, chroma format, bit depth
        { nameof(HevcTestStreams.X265Main420), 1, 24, 16, 24, 16, 1, 8 },
        { nameof(HevcTestStreams.X265Rext444TenBits), 1, 24, 24, 19, 17, 3, 10 },
        { nameof(HevcTestStreams.X265Lossless422), 1, 16, 16, 16, 16, 2, 8 },
        { nameof(HevcTestStreams.HmPcm420), 1, 16, 16, 16, 16, 1, 8 },
        { nameof(HevcTestStreams.HmSlices420), 1, 48, 32, 48, 32, 1, 8 },
        { nameof(HevcTestStreams.HmTiles420), 1, 48, 32, 48, 32, 1, 8 },
        { nameof(HevcTestStreams.HmWavefront420), 1, 48, 32, 48, 32, 1, 8 },
        { nameof(HevcTestStreams.HmRangeExtensions444), 1, 16, 16, 16, 16, 3, 8 },
        { nameof(HevcTestStreams.HmCrc420TenBits), 2, 16, 8, 16, 8, 1, 10 },
        { nameof(HevcTestStreams.HmChecksum422TwelveBits), 1, 16, 8, 16, 8, 2, 12 },
        { nameof(HevcTestStreams.HmSao422TenBits), 1, 32, 32, 16, 11, 2, 10 },
        { nameof(HevcTestStreams.HmChromaQpOffsets444TenBits), 2, 16, 24, 10, 19, 3, 10 },
        { nameof(HevcTestStreams.KvazaarTilesWavefront420), 2, 200, 152, 194, 146, 1, 8 },
    };

    internal static byte[] GetStream(string name) => name switch
    {
        nameof(HevcTestStreams.X265Main420) => HevcTestStreams.X265Main420,
        nameof(HevcTestStreams.X265Rext444TenBits) => HevcTestStreams.X265Rext444TenBits,
        nameof(HevcTestStreams.X265Lossless422) => HevcTestStreams.X265Lossless422,
        nameof(HevcTestStreams.X265Inter) => HevcTestStreams.X265Inter,
        nameof(HevcTestStreams.HmPcm420) => HevcTestStreams.HmPcm420,
        nameof(HevcTestStreams.HmSlices420) => HevcTestStreams.HmSlices420,
        nameof(HevcTestStreams.HmTiles420) => HevcTestStreams.HmTiles420,
        nameof(HevcTestStreams.HmWavefront420) => HevcTestStreams.HmWavefront420,
        nameof(HevcTestStreams.HmRangeExtensions444) => HevcTestStreams.HmRangeExtensions444,
        nameof(HevcTestStreams.HmCrc420TenBits) => HevcTestStreams.HmCrc420TenBits,
        nameof(HevcTestStreams.HmChecksum422TwelveBits) => HevcTestStreams.HmChecksum422TwelveBits,
        nameof(HevcTestStreams.HmSao422TenBits) => HevcTestStreams.HmSao422TenBits,
        nameof(HevcTestStreams.HmChromaQpOffsets444TenBits) => HevcTestStreams.HmChromaQpOffsets444TenBits,
        nameof(HevcTestStreams.KvazaarTilesWavefront420) => HevcTestStreams.KvazaarTilesWavefront420,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, message: null),
    };

    private static AllocationScope CreateScope(string name) => AllocationScope.Create(ImageConfiguration.Default, name);

    [Theory]
    [MemberData(nameof(Streams))]
    public void PicturesHaveTheHashOfTheirStream(string name, int pictureCount, int width, int height, int outputWidth, int outputHeight, int chromaFormatIdc, int bitDepth)
    {
        using var audit = new PoolAudit();
        var scope = new AllocationScope(ImageResourceLimits.Default, audit.Pool, name);
        var pictures = HevcTestDecoder.Decode(GetStream(name), scope, out _);
        Assert.HasCount(pictureCount, pictures);
        for (var i = 0; i < pictures.Count; i++)
        {
            var picture = pictures[i];
            var sps = picture.Sps;
            Assert.Equal((width, height, chromaFormatIdc, bitDepth), (sps.PicWidthInLumaSamples, sps.PicHeightInLumaSamples, sps.ChromaFormatIdc, sps.BitDepth));
            Assert.Equal((0, 0, outputWidth, outputHeight), (sps.OutputLeft, sps.OutputTop, sps.OutputWidth, sps.OutputHeight));
            Assert.HasCount(chromaFormatIdc is 0 ? 1 : 3, picture.Planes);

            // An access unit per picture, in output order: the pictures after the first one are IDR pictures, whose picture
            // order count is 0, or intra pictures of the same coded video sequence
            Assert.Equal(i, picture.AccessUnitIndex);
            Assert.True(picture.PicOrderCntVal == i || picture.PicOrderCntVal is 0, $"The picture order count is {picture.PicOrderCntVal}");
            Assert.All(picture.Planes, plane => Assert.True(plane.Max() < 1 << bitDepth));
            HevcTestDecoder.AssertMatchesHash(picture, name);
        }

        // Every buffer of the decoder is returned
        Assert.Null(audit.Verify());
    }

    [Fact]
    public void TheStreamsReachTheToolsTheyWereMadeFor()
    {
        AssertTools(nameof(HevcTestStreams.X265Main420), HevcTools.Deblocking | HevcTools.SaoMerge | HevcTools.IntraNxN | HevcTools.Dst4x4 | HevcTools.Transform8x8 | HevcTools.Transform16x16 | HevcTools.SignDataHiding | HevcTools.EscapeCoefficients | HevcTools.IntraReferenceFiltering);
        AssertTools(nameof(HevcTestStreams.X265Rext444TenBits), HevcTools.TransformSkip | HevcTools.HorizontalScan | HevcTools.VerticalScan | HevcTools.IntraPlanar | HevcTools.IntraDc | HevcTools.IntraAngular | HevcTools.IntraReferenceSubstitution);
        AssertTools(nameof(HevcTestStreams.X265Lossless422), HevcTools.TransquantBypass | HevcTools.Chroma422Split);
        AssertTools(nameof(HevcTestStreams.HmPcm420), HevcTools.Pcm);
        AssertTools(nameof(HevcTestStreams.HmSlices420), HevcTools.MultipleSlices | HevcTools.DependentSliceSegments | HevcTools.DeblockingStrongFilter);
        AssertTools(nameof(HevcTestStreams.HmTiles420), HevcTools.Tiles);
        AssertTools(nameof(HevcTestStreams.HmWavefront420), HevcTools.Wavefront);
        AssertTools(nameof(HevcTestStreams.HmRangeExtensions444), HevcTools.ImplicitRdpcm | HevcTools.TransformSkipRotation | HevcTools.TransformSkipContext | HevcTools.CrossComponentPrediction | HevcTools.PersistentRiceAdaptation | HevcTools.CuQpDelta | HevcTools.ScalingList | HevcTools.TransquantBypass);
        AssertTools(nameof(HevcTestStreams.HmChecksum422TwelveBits), HevcTools.Chroma422Split | HevcTools.Transform4x4);
        AssertTools(nameof(HevcTestStreams.HmSao422TenBits), HevcTools.SaoBandOffset | HevcTools.SaoEdgeOffset | HevcTools.ScalingList | HevcTools.PersistentRiceAdaptation);
        AssertTools(nameof(HevcTestStreams.HmChromaQpOffsets444TenBits), HevcTools.ChromaQpOffsetList | HevcTools.ImplicitRdpcm | HevcTools.TransformSkipRotation | HevcTools.TransformSkipContext);
        AssertTools(nameof(HevcTestStreams.KvazaarTilesWavefront420), HevcTools.Tiles | HevcTools.Wavefront | HevcTools.MultipleSlices | HevcTools.Transform32x32 | HevcTools.CuQpDelta);

        static void AssertTools(string name, HevcTools expected)
        {
            HevcTestDecoder.Decode(GetStream(name), CreateScope(name), out var tools);
            Assert.Equal(expected, tools & expected, $"{name} does not reach {expected & ~tools}");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void TheLengthOfANalUnitHasOneTwoOrFourBytes(int nalLengthSize)
    {
        // The NAL units of this stream are shorter than 256 bytes
        var name = nameof(HevcTestStreams.HmChecksum422TwelveBits);
        var pictures = HevcTestDecoder.Decode(GetStream(name), CreateScope(name), out _, nalLengthSize: nalLengthSize);
        Assert.Single(pictures);
        HevcTestDecoder.AssertMatchesHash(pictures[0], name);

        using var decoder = new HevcDecoder(CreateScope(name));
        Assert.Throws<ArgumentOutOfRangeException>(() => decoder.SetInput([0, 0, 0], 0, 3, nalLengthSize: 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => decoder.SetInput([0, 0, 0], 2, 2, nalLengthSize: 4));
        Assert.Throws<ArgumentNullException>(() => decoder.SetInput(null!, 0, 0, nalLengthSize: 4));
    }

    [Fact]
    public void ParameterSetsOfADecoderConfigurationAreUsedByTheSlices()
    {
        // The parameter sets are given apart, as a file gives them in its decoder configuration record; the access unit
        // has the slice segment and its supplemental enhancement information, and starts in the middle of an array
        var name = nameof(HevcTestStreams.X265Rext444TenBits);
        var units = ReferenceHevcStream.SplitAnnexB(GetStream(name));
        using var decoder = new HevcDecoder(CreateScope(name));
        foreach (var unit in units.Where(unit => ReferenceHevcStream.GetNalUnitType(unit.Span) is >= 32 and <= 34))
        {
            decoder.AddParameterSet(unit.Span);
        }

        var accessUnit = ReferenceHevcStream.ToLengthPrefixed(units.Where(unit => ReferenceHevcStream.GetNalUnitType(unit.Span) is < 32 or > 34));
        byte[] data = [1, 2, 3, .. accessUnit, 4, 5];
        var copy = (byte[])data.Clone();
        decoder.SetInput(data, 3, accessUnit.Length, nalLengthSize: 4);
        decoder.EndOfSequence();
        var picture = decoder.DecodeNextOutputPicture(XunitCancellationToken);
        Assert.NotNull(picture);
        try
        {
            Assert.Equal((0, 3, 10), (picture.AccessUnitIndex, picture.PlaneCount, picture.Sps.BitDepth));
            Assert.NotNull(picture.DecodedPictureHash);
            Assert.Equal(HevcDecodedPictureHash.Md5, picture.DecodedPictureHash.HashType);
            var plane = picture.GetPlane(2);
            Assert.Equal((24, 24), (plane.Width, plane.Height));
            Assert.Equal(Convert.ToHexString(picture.DecodedPictureHash.GetMd5(2)), Convert.ToHexString(ReferenceHevcPictureHash.ComputeMd5(plane.Samples, plane.Width, plane.Height, plane.Stride, 10)));
        }
        finally
        {
            picture.Release();
        }

        // The input is consumed; the data of the caller are as they were
        Assert.Null(decoder.DecodeNextOutputPicture(XunitCancellationToken));
        Assert.Equal(copy, data);
        Assert.False(decoder.HasEnhancementLayers);

        // A slice segment is not a parameter set
        var slice = units.First(unit => ReferenceHevcStream.GetNalUnitType(unit.Span) < 32);
        Assert.Throws<InvalidImageContentException>(() => decoder.AddParameterSet(slice.Span));
        Assert.Throws<InvalidOperationException>(() => decoder.SetInput(data, 3, accessUnit.Length, nalLengthSize: 4));
    }

    [Fact]
    public void InLoopFiltersCanBeLeftOut()
    {
        // A stream that deblocks its samples: without the deblocking filter the picture is another one
        var name = nameof(HevcTestStreams.HmTiles420);
        var all = HevcTestDecoder.Decode(GetStream(name), CreateScope(name), out var tools)[0];
        Assert.True(tools.HasFlag(HevcTools.DeblockingStrongFilter));
        var none = HevcTestDecoder.Decode(GetStream(name), CreateScope(name), out tools, HevcInLoopFilters.None)[0];
        Assert.False(tools.HasFlag(HevcTools.Deblocking));
        var deblocked = HevcTestDecoder.Decode(GetStream(name), CreateScope(name), out _, HevcInLoopFilters.Deblocking)[0];
        var offset = HevcTestDecoder.Decode(GetStream(name), CreateScope(name), out _, HevcInLoopFilters.Sao)[0];
        Assert.False(all.Planes[0].AsSpan().SequenceEqual(none.Planes[0]));
        Assert.Equal(all.Planes[0], deblocked.Planes[0]);
        Assert.Equal(none.Planes[0], offset.Planes[0]);

        // A stream that offsets its samples, in bands and along edges, and does not deblock them
        name = nameof(HevcTestStreams.HmSao422TenBits);
        all = HevcTestDecoder.Decode(GetStream(name), CreateScope(name), out tools)[0];
        Assert.Equal(HevcTools.SaoBandOffset | HevcTools.SaoEdgeOffset, tools & (HevcTools.SaoBandOffset | HevcTools.SaoEdgeOffset | HevcTools.Deblocking));
        none = HevcTestDecoder.Decode(GetStream(name), CreateScope(name), out tools, HevcInLoopFilters.None)[0];
        deblocked = HevcTestDecoder.Decode(GetStream(name), CreateScope(name), out _, HevcInLoopFilters.Deblocking)[0];
        offset = HevcTestDecoder.Decode(GetStream(name), CreateScope(name), out _, HevcInLoopFilters.Sao)[0];
        Assert.Contains(all.Planes.Zip(none.Planes), pair => !pair.First.AsSpan().SequenceEqual(pair.Second));
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(all.Planes[i], offset.Planes[i]);
            Assert.Equal(none.Planes[i], deblocked.Planes[i]);
        }

        using var decoder = new HevcDecoder(CreateScope(name));
        Assert.Equal(HevcInLoopFilters.All, decoder.InLoopFilters);
    }

    [Fact]
    public void NalUnitsOfOtherLayersAreIgnored()
    {
        // A copy of the slice segment in the layer 1 follows the slice segment: nuh_layer_id is the bits 7 to 12 of the header
        var name = nameof(HevcTestStreams.X265Main420);
        var units = ReferenceHevcStream.SplitAnnexB(GetStream(name)).ToList();
        var index = units.FindIndex(unit => ReferenceHevcStream.GetNalUnitType(unit.Span) < 32);
        var layer = units[index].ToArray();
        layer[1] |= 1 << 3;
        Assert.Equal(1, ReferenceHevcStream.GetLayerId(layer));
        units.Insert(index + 1, layer);
        var data = ReferenceHevcStream.ToLengthPrefixed(units);
        using var decoder = new HevcDecoder(CreateScope(name));
        decoder.SetInput(data, 0, data.Length, nalLengthSize: 4);
        decoder.EndOfSequence();
        var picture = decoder.DecodeNextOutputPicture(XunitCancellationToken);
        Assert.NotNull(picture);
        picture.Release();
        Assert.True(decoder.HasEnhancementLayers);
        Assert.Null(decoder.DecodeNextOutputPicture(XunitCancellationToken));
    }

    [Fact]
    public void APictureNeedsItsParameterSetsAndAllItsSliceSegments()
    {
        var name = nameof(HevcTestStreams.HmSlices420);
        var units = ReferenceHevcStream.SplitAnnexB(GetStream(name)).ToList();
        var slices = units.Where(unit => ReferenceHevcStream.GetNalUnitType(unit.Span) < 32).ToList();
        Assert.HasCount(4, slices);

        // Without picture parameter set, without sequence parameter set, without video parameter set
        foreach (var missing in new[] { ReferenceHevcStream.NalPps, ReferenceHevcStream.NalSps, ReferenceHevcStream.NalVps })
        {
            AssertInvalid([.. units.Where(unit => ReferenceHevcStream.GetNalUnitType(unit.Span) != missing)]);
        }

        // Without the last slice segment, without the second one, without the first one; with a slice segment twice
        AssertInvalid([.. units.Where(unit => !unit.Equals(slices[3]))]);
        AssertInvalid([.. units.Where(unit => !unit.Equals(slices[1]))]);
        AssertInvalid([.. units.Where(unit => !unit.Equals(slices[0]))]);
        var twice = units.ToList();
        twice.Insert(units.IndexOf(slices[2]), slices[2]);
        AssertInvalid(twice);

        // The stream as it is
        Assert.Single(Decode(units));

        static void AssertInvalid(List<ReadOnlyMemory<byte>> units)
        {
            var exception = Assert.Throws<InvalidImageContentException>(() => Decode(units));
            Assert.Equal(ImageFormat.Heic, exception.Format);
        }

        static List<int> Decode(List<ReadOnlyMemory<byte>> units)
        {
            using var audit = new PoolAudit();
            var pictures = new List<int>();
            try
            {
                var scope = new AllocationScope(ImageResourceLimits.Default, audit.Pool, nameof(APictureNeedsItsParameterSetsAndAllItsSliceSegments));
                using var decoder = new HevcDecoder(scope);
                var data = ReferenceHevcStream.ToLengthPrefixed(units);
                decoder.SetInput(data, 0, data.Length, nalLengthSize: 4);
                decoder.EndOfSequence();
                while (decoder.DecodeNextOutputPicture(XunitCancellationToken) is { } picture)
                {
                    pictures.Add(picture.PicOrderCntVal);
                    picture.Release();
                }
            }
            finally
            {
                Assert.Null(audit.Verify());
            }

            return pictures;
        }
    }

    [Fact]
    public void ADisposedDecoderRefusesEveryCall()
    {
        var decoder = new HevcDecoder(CreateScope(nameof(ADisposedDecoderRefusesEveryCall)));
        decoder.Dispose();
        decoder.Dispose();
        Assert.Throws<ObjectDisposedException>(() => decoder.SetInput([], 0, 0, nalLengthSize: 4));
        Assert.Throws<ObjectDisposedException>(() => decoder.AddParameterSet([0x40, 0x01]));
        Assert.Throws<ObjectDisposedException>(() => decoder.EndOfSequence());
        Assert.Throws<ObjectDisposedException>(() => decoder.DecodeNextOutputPicture(XunitCancellationToken));
    }

    [Fact]
    public void ParameterSetsAreCopiedFromADecoderThatHasThem()
    {
        // The decoders of the pictures that share a configuration record (the tiles of a grid) take its parameter sets from
        // a decoder that read them once
        var name = nameof(HevcTestStreams.X265Rext444TenBits);
        var units = ReferenceHevcStream.SplitAnnexB(HevcTestStreams.X265Rext444TenBits);
        var accessUnit = ReferenceHevcStream.ToLengthPrefixed(units.Where(unit => ReferenceHevcStream.GetNalUnitType(unit.Span) is < 32 or > 34));
        using var audit = new PoolAudit();
        var scope = new AllocationScope(ImageResourceLimits.Default, audit.Pool, name);
        using (var template = new HevcDecoder(scope))
        {
            foreach (var unit in units.Where(unit => ReferenceHevcStream.GetNalUnitType(unit.Span) is >= 32 and <= 34))
            {
                template.AddParameterSet(unit.Span);
            }

            for (var i = 0; i < 3; i++)
            {
                using var decoder = new HevcDecoder(scope);
                decoder.CopyParameterSetsFrom(template);
                decoder.SetInput(accessUnit, 0, accessUnit.Length, nalLengthSize: 4);
                decoder.EndOfSequence();
                var picture = decoder.DecodeNextOutputPicture(XunitCancellationToken);
                Assert.NotNull(picture);
                try
                {
                    Assert.NotNull(picture.DecodedPictureHash);
                    var plane = picture.GetPlane(0);
                    Assert.Equal(Convert.ToHexString(picture.DecodedPictureHash.GetMd5(0)), Convert.ToHexString(ReferenceHevcPictureHash.ComputeMd5(plane.Samples, plane.Width, plane.Height, plane.Stride, picture.Sps.BitDepth)));
                }
                finally
                {
                    picture.Release();
                }
            }

            // A decoder without the parameter sets cannot decode the slice segments
            using var empty = new HevcDecoder(scope);
            empty.SetInput(accessUnit, 0, accessUnit.Length, nalLengthSize: 4);
            Assert.Throws<InvalidImageContentException>(() => empty.DecodeNextOutputPicture(XunitCancellationToken));
        }

        Assert.Null(audit.Verify());
    }
}
