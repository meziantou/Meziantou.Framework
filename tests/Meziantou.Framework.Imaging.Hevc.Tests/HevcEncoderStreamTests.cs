using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.TestHarness.Hevc;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The building blocks of the encoder together: whole bitstreams written with the header writer, the NAL unit writer and
/// the arithmetic encoder, whose pictures are made of the simplest coding units, PCM coding units (Rec. ITU-T H.265,
/// clauses 7.3.8.5 and 7.3.8.7), which need no search. The decoder of the library must output the samples that were
/// written, which is what the conformance streams validate it for; the pictures also carry their hash, computed by the test
/// harness.
/// </summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
public sealed class HevcEncoderStreamTests
{
    [Theory]
    [InlineData(1, 8, 16, 16, 1, 0)]
    [InlineData(1, 8, 48, 32, 2, 0)]
    [InlineData(1, 8, 20, 12, 1, 5)]
    [InlineData(1, 10, 33, 17, 2, 0)]
    [InlineData(1, 12, 16, 48, 1, 9)]
    [InlineData(0, 8, 1, 1, 1, 0)]
    [InlineData(0, 10, 40, 24, 2, 10)]
    [InlineData(0, 12, 64, 16, 1, 0)]
    [InlineData(2, 8, 32, 32, 1, 0)]
    [InlineData(2, 10, 18, 30, 2, 7)]
    [InlineData(2, 12, 16, 16, 1, 0)]
    [InlineData(3, 8, 31, 33, 2, 0)]
    [InlineData(3, 10, 16, 16, 1, 0)]
    [InlineData(3, 12, 48, 16, 1, 12)]
    public void PicturesOfPcmCodingUnitsAreDecodedAsTheyWereWritten(int chromaFormatIdc, int bitDepth, int width, int height, int pictureCount, int pcmBitDepth)
    {
        var random = new Random((chromaFormatIdc * 1000) + (bitDepth * 100) + width + height);
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(PicturesOfPcmCodingUnitsAreDecodedAsTheyWereWritten));
        var settings = new HevcEncoderSettings(width, height, bitDepth, chromaFormatIdc, ColourPrimaries: 1, TransferCharacteristics: 13, MatrixCoeffs: 6, FullRange: true, ChromaSampleLocType: 1, StillPicture: pictureCount is 1, Lossless: true, Quality: 100, Effort: 0, KeyFrameInterval: 1);

        // Coding tree blocks of 16 samples that are each one coding unit: the coding quadtree has no syntax element
        var parameters = HevcSequenceParameters.Create(settings, HevcCodingTools.Pcm) with
        {
            MinCbLog2SizeY = 4,
            CtbLog2SizeY = 4,
            MaxTbLog2SizeY = 4,
            MaxTransformHierarchyDepthInter = 0,
            MaxTransformHierarchyDepthIntra = 0,
            PcmBitDepth = pcmBitDepth,
        };
        Assert.True(parameters.IsValid);

        // The parameter sets, written then read back: the encoder works with what a decoder reads
        using var rbsp = new PooledPayloadWriter(scope);
        using var byteStream = new PooledPayloadWriter(scope);
        using var configuration = new PooledPayloadWriter(scope);
        using var samples = new PooledPayloadWriter(scope);
        HevcHeaderWriter.WriteVideoParameterSet(rbsp, parameters);
        HevcNalWriter.WriteByteStream(byteStream, HevcConstants.NalVps, 0, rbsp.WrittenSpan);
        var vpsLength = HevcNalWriter.WriteNalUnit(configuration, HevcConstants.NalVps, 0, rbsp.WrittenSpan);
        rbsp.Reset();
        HevcHeaderWriter.WriteSequenceParameterSet(rbsp, parameters);
        var sps = HevcSequenceParameterSet.ParseRbsp(rbsp.WrittenSpan);
        HevcNalWriter.WriteByteStream(byteStream, HevcConstants.NalSps, 0, rbsp.WrittenSpan);
        var spsLength = HevcNalWriter.WriteNalUnit(configuration, HevcConstants.NalSps, 0, rbsp.WrittenSpan);
        rbsp.Reset();
        HevcHeaderWriter.WritePictureParameterSet(rbsp, sps, new HevcPictureParameters(26) { TransquantBypassEnabled = (width & 1) is 1 });
        var pps = HevcPictureParameterSet.ParseRbsp(rbsp.WrittenSpan);
        HevcNalWriter.WriteByteStream(byteStream, HevcConstants.NalPps, 0, rbsp.WrittenSpan);
        var ppsLength = HevcNalWriter.WriteNalUnit(configuration, HevcConstants.NalPps, 0, rbsp.WrittenSpan);
        using var layout = HevcPictureLayout.Create(scope, sps, pps);

        // The pictures: random samples of the coded size, the part outside the conformance window included
        var planeCount = sps.ComponentCount;
        var pictures = new ushort[pictureCount][][];
        var sampleOffsets = new List<int>();
        for (var pictureIndex = 0; pictureIndex < pictureCount; pictureIndex++)
        {
            var planes = new ushort[planeCount][];
            var md5 = new byte[planeCount * 16];
            for (var cIdx = 0; cIdx < planeCount; cIdx++)
            {
                var planeWidth = cIdx is 0 ? sps.PicWidthInLumaSamples : sps.PicWidthInLumaSamples / sps.SubWidthC;
                var planeHeight = cIdx is 0 ? sps.PicHeightInLumaSamples : sps.PicHeightInLumaSamples / sps.SubHeightC;
                planes[cIdx] = new ushort[planeWidth * planeHeight];
                for (var i = 0; i < planes[cIdx].Length; i++)
                {
                    // A PCM sample of less bits than the samples is their most significant bits
                    planes[cIdx][i] = (ushort)(random.Next(1 << sps.PcmBitDepthY) << (bitDepth - sps.PcmBitDepthY));
                }

                ReferenceHevcPictureHash.ComputeMd5(planes[cIdx], planeWidth, planeHeight, planeWidth, bitDepth).CopyTo(md5, cIdx * 16);
            }

            pictures[pictureIndex] = planes;

            // The slice segment: its header, read back, then its data
            rbsp.Reset();
            var slice = HevcSliceParameters.CreateKeyFrame(20 + pictureIndex);
            HevcHeaderWriter.WriteSliceSegmentHeader(rbsp, sps, pps, slice);
            var nal = HevcNalUnitHeader.Read([(byte)(slice.NalUnitType << 1), 1]);
            var header = HevcSliceSegmentHeader.Parse(rbsp.WrittenSpan, nal, layout, sliceHeader: null);
            Assert.Equal(rbsp.Length, header.DataOffset);
            var bins = WriteSliceSegmentData(rbsp, layout, header, planes, random);

            // Clause 9.3.5.7: a picture of PCM coding units has few bins for its size
            var rawMinCuBits = (1 << (2 * sps.MinCbLog2SizeY)) * (bitDepth + (2 * bitDepth / (sps.SubWidthC * sps.SubHeightC)));
            Assert.Equal(0, HevcCabacEncoder.GetCabacZeroWordCount(bins, rbsp.Length, rawMinCuBits, sps.PicSizeInCtbsY));
            if (pictureIndex is 1)
            {
                // Two cabac_zero_word syntax elements after the slice segment data change nothing
                rbsp.WriteZeros(4);
            }

            HevcNalWriter.WriteByteStream(byteStream, slice.NalUnitType, 0, rbsp.WrittenSpan);
            sampleOffsets.Add(samples.Length);
            HevcNalWriter.WriteLengthPrefixed(samples, slice.NalUnitType, 0, rbsp.WrittenSpan);
            rbsp.Reset();
            HevcHeaderWriter.WriteDecodedPictureHash(rbsp, new HevcDecodedPictureHash(HevcDecodedPictureHash.Md5, planeCount, md5, new uint[planeCount]));
            HevcNalWriter.WriteByteStream(byteStream, HevcConstants.NalSuffixSei, 0, rbsp.WrittenSpan);
            HevcNalWriter.WriteLengthPrefixed(samples, HevcConstants.NalSuffixSei, 0, rbsp.WrittenSpan);
        }

        sampleOffsets.Add(samples.Length);

        // As a byte stream, split into access units by the test harness
        var decoded = HevcTestDecoder.Decode(byteStream.WrittenSpan.ToArray(), scope, out var tools);
        AssertPictures(decoded, pictures, sps);
        Assert.True(tools.HasFlag(HevcTools.Pcm));
        Assert.Equal(pps.TransquantBypassEnabledFlag, tools.HasFlag(HevcTools.TransquantBypass));

        // The video parameter set always has emulation prevention bytes
        Assert.True(tools.HasFlag(HevcTools.EmulationPrevention));

        // As the items and the samples of a file: the parameter sets of a configuration record, then the NAL units of each
        // picture preceded by their length
        using var decoder = new HevcDecoder(scope);
        var parameterSets = configuration.WrittenSpan;
        decoder.AddParameterSet(parameterSets[..vpsLength]);
        decoder.AddParameterSet(parameterSets.Slice(vpsLength, spsLength));
        decoder.AddParameterSet(parameterSets.Slice(vpsLength + spsLength, ppsLength));
        var data = samples.WrittenSpan.ToArray();
        var output = new List<HevcTestDecoder.DecodedPicture>();
        for (var pictureIndex = 0; pictureIndex < pictureCount; pictureIndex++)
        {
            decoder.SetInput(data, sampleOffsets[pictureIndex], sampleOffsets[pictureIndex + 1] - sampleOffsets[pictureIndex], HevcNalWriter.NalLengthSize);
            Drain(decoder, output);
        }

        decoder.EndOfSequence();
        Drain(decoder, output);
        AssertPictures(output, pictures, sps);
    }

    // Clause 7.3.8.1 "General slice segment data syntax", for coding tree units that are one PCM coding unit
    private static long WriteSliceSegmentData(PooledPayloadWriter output, HevcPictureLayout layout, HevcSliceSegmentHeader header, ushort[][] planes, Random random)
    {
        var sps = layout.Sps;
        var pps = layout.Pps;
        Assert.Equal(sps.CtbLog2SizeY, sps.MinCbLog2SizeY);
        Assert.True(sps.PcmEnabledFlag && sps.CtbLog2SizeY >= sps.Log2MinIpcmCbSizeY && sps.CtbLog2SizeY <= sps.Log2MaxIpcmCbSizeY);

        // The context variables of the slice are those of a decoder (clause 9.3.2.2)
        var contexts = new HevcCabacDecoder();
        contexts.InitializeContexts(header.InitType, header.SliceQpY);
        var encoder = new HevcCabacEncoder();
        encoder.Initialize(output);
        var ctbSize = sps.CtbSizeY;
        var lumaStride = sps.PicWidthInLumaSamples;
        var chromaStride = sps.PicWidthInLumaSamples / sps.SubWidthC;
        for (var ctbAddr = 0; ctbAddr < sps.PicSizeInCtbsY; ctbAddr++)
        {
            var x0 = (ctbAddr % sps.PicWidthInCtbsY) * ctbSize;
            var y0 = (ctbAddr / sps.PicWidthInCtbsY) * ctbSize;

            // coding_quadtree( ): split_cu_flag is not present for a block of the minimum size. coding_unit( ):
            // cu_transquant_bypass_flag, part_mode PART_2Nx2N (one bin equal to 1 for an intra coding unit), pcm_flag
            if (pps.TransquantBypassEnabledFlag)
            {
                encoder.EncodeDecision(ref contexts.Contexts[HevcContextLayout.CuTransquantBypassFlag], ctbAddr is 0 ? 1 : random.Next(2));
            }

            encoder.EncodeDecision(ref contexts.Contexts[HevcContextLayout.PartMode], 1);
            encoder.EncodeTerminate(1);

            // pcm_alignment_zero_bit: the encoder completed the byte. pcm_sample( ): the samples of the three components
            Assert.False(encoder.IsActive);
            var writer = new HevcBitWriter(output);
            WritePcmSamples(ref writer, planes[0], lumaStride, x0, y0, ctbSize, ctbSize, sps.PcmBitDepthY, sps.BitDepth);
            for (var cIdx = 1; cIdx < planes.Length; cIdx++)
            {
                WritePcmSamples(ref writer, planes[cIdx], chromaStride, x0 / sps.SubWidthC, y0 / sps.SubHeightC, ctbSize / sps.SubWidthC, ctbSize / sps.SubHeightC, sps.PcmBitDepthC, sps.BitDepth);
            }

            // The samples end on a byte, where a new codeword starts (clause 9.3.2.6), with the same context variables
            Assert.True(writer.IsByteAligned);
            encoder.Initialize(output);

            // end_of_slice_segment_flag: the codeword ends with rbsp_slice_segment_trailing_bits( )
            encoder.EncodeTerminate(ctbAddr == sps.PicSizeInCtbsY - 1 ? 1 : 0);
        }

        Assert.False(encoder.IsActive);
        return encoder.BinCountsInNalUnits;
    }

    private static void WritePcmSamples(ref HevcBitWriter writer, ushort[] plane, int stride, int x0, int y0, int width, int height, int pcmBitDepth, int bitDepth)
    {
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                // Equations 8-12, 8-15 and 8-16: a decoder shifts a PCM sample up to the bit depth of the samples
                writer.WriteBits((uint)(plane[((y0 + y) * stride) + x0 + x] >> (bitDepth - pcmBitDepth)), pcmBitDepth);
            }
        }
    }

    private static void AssertPictures(List<HevcTestDecoder.DecodedPicture> decoded, ushort[][][] pictures, HevcSequenceParameterSet sps)
    {
        Assert.HasCount(pictures.Length, decoded);
        for (var i = 0; i < pictures.Length; i++)
        {
            Assert.HasCount(pictures[i].Length, decoded[i].Planes);
            Assert.Equal((sps.PicWidthInLumaSamples, sps.PicHeightInLumaSamples, sps.OutputWidth, sps.OutputHeight), (decoded[i].Sps.PicWidthInLumaSamples, decoded[i].Sps.PicHeightInLumaSamples, decoded[i].Sps.OutputWidth, decoded[i].Sps.OutputHeight));
            for (var cIdx = 0; cIdx < pictures[i].Length; cIdx++)
            {
                Assert.True(pictures[i][cIdx].AsSpan().SequenceEqual(decoded[i].Planes[cIdx]), $"The plane {cIdx} of the picture {i} is not the one that was written");
            }

            // The hash of the stream, checked with the functions of the test harness
            HevcTestDecoder.AssertMatchesHash(decoded[i], $"picture {i}");
        }
    }

    private static void Drain(HevcDecoder decoder, List<HevcTestDecoder.DecodedPicture> pictures)
    {
        while (decoder.DecodeNextOutputPicture(XunitCancellationToken) is { } picture)
        {
            try
            {
                var planes = new ushort[picture.PlaneCount][];
                for (var i = 0; i < planes.Length; i++)
                {
                    var plane = picture.GetPlane(i);
                    planes[i] = new ushort[plane.Width * plane.Height];
                    for (var y = 0; y < plane.Height; y++)
                    {
                        plane.GetRow(y).CopyTo(planes[i].AsSpan(y * plane.Width));
                    }
                }

                pictures.Add(new HevcTestDecoder.DecodedPicture(picture.PicOrderCntVal, picture.AccessUnitIndex, picture.Sps, planes, picture.DecodedPictureHash));
            }
            finally
            {
                picture.Release();
            }
        }
    }
}
