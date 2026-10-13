namespace Meziantou.Framework.Imaging.CorpusGenerator;

// The recipes of the vectors derived from the stream of another recipe (see HevcCorpus.Stream.cs): each one names its
// source vector, how the stream is changed, and the number of pictures a decoder then outputs.
internal static partial class HevcCorpus
{
    private static HevcDerivation Metadata(string options) => new("metadata") { BitstreamFilter = options };

    /// <summary>Every CRA picture after the first picture becomes a BLA picture of the given type; the RASL pictures are
    /// removed unless the type allows leading pictures that are skipped (BLA_W_LP).</summary>
    private static HevcDerivation BrokenLink(int type) => new("nalType") { NalType = NalCra, NewNalType = type, RemovedTypes = type == NalBlaWLp ? [] : [NalRaslN, NalRaslR] };

    private static IEnumerable<HevcVector> DerivedVectors(IReadOnlyList<HevcVector> vectors)
    {
        // A conformance window with an offset on every side, in every chroma format: FFmpeg's hevc_metadata rewrites
        // the offsets of the sequence parameter set (in luma samples). The decoded pictures and their hashes do not
        // change; the output pictures are other parts of them
        yield return Derived(vectors, "x265-intra-420-8bit-34x38", "derived-crop-420-8bit-26x32", Metadata("crop_left=4:crop_right=10:crop_top=2:crop_bottom=6"), 1, "hevc.crop=bottom,left,right,top", "hevc.chroma=4:2:0");
        yield return Derived(vectors, "x265-intra-422-8bit-34x37", "derived-crop-422-8bit-30x35", Metadata("crop_left=6:crop_right=4:crop_top=1:crop_bottom=4"), 1, "hevc.crop=bottom,left,right,top", "hevc.chroma=4:2:2");
        yield return Derived(vectors, "x265-intra-444-10bit-33x35", "derived-crop-444-10bit-33x33", Metadata("crop_left=3:crop_right=4:crop_top=5:crop_bottom=2"), 2, "hevc.crop=bottom,left,right,top", "hevc.chroma=4:4:4")
            .Without(DecoderLibde265, Libde265HighBitDepthCrop);
        yield return Derived(vectors, "x265-intra-400-12bit-35x33", "derived-crop-400-12bit-37x33", Metadata("crop_left=1:crop_right=2:crop_top=3:crop_bottom=4"), 1, "hevc.crop=bottom,left,right,top", "hevc.chroma=4:0:0");
        yield return Derived(vectors, "x265-intra-gbr-8bit-37x35", "derived-crop-gbr-8bit-33x33", Metadata("crop_left=5:crop_right=2:crop_top=1:crop_bottom=6"), 1, "hevc.crop=bottom,left,right,top", "hevc.matrix=identity");
        yield return Derived(vectors, "x265-inter-420-8bit-66x50", "derived-crop-inter-420-8bit-62x46", Metadata("crop_left=8:crop_right=2:crop_top=4:crop_bottom=6"), 5, "hevc.crop=bottom,left,right,top", "hevc.frameTypes=inter");
        yield return Derived(vectors, "x265-inter-422-10bit-66x49", "derived-crop-inter-422-10bit-60x47", Metadata("crop_left=2:crop_right=10:crop_top=7:crop_bottom=2"), 5, "hevc.crop=bottom,left,right,top", "hevc.chroma=4:2:2")
            .Without(DecoderLibde265, Libde265HighBitDepthCrop);

        // Other rewrites of FFmpeg's bitstream filters: access unit delimiters, the chroma sample location of the VUI,
        // prefix SEI messages removed (the picture hashes are suffix SEI messages)
        yield return Derived(vectors, "x265-b-pyramid-64x64", "derived-aud-inserted-64x64", Metadata("aud=insert"), 10, "hevc.nal=AUD", "hevc.referenceB");
        yield return Derived(vectors, "x265-intra-vui-full-range-64x64", "derived-chroma-loc-5-64x64", Metadata("chroma_sample_loc_type=5"), 1, "hevc.chromaLoc=5");
        yield return Derived(vectors, "x265-intra-420-10bit-34x38", "derived-chroma-loc-0-10bit-34x38", Metadata("chroma_sample_loc_type=0"), 1, "hevc.chromaLoc=0");
        yield return Derived(vectors, "x265-intra-vui-bt2020-pq-10bit-64x64", "derived-prefix-sei-removed-64x64", new HevcDerivation("filterUnits") { BitstreamFilter = "remove_types=39" }, 1, "hevc.sei=decodedPictureHash");

        // A stream that starts at a CRA picture, as after a random access: the RASL pictures that follow it refer to
        // pictures that are not there, and are not output
        yield return Derived(vectors, "x265-open-gop-cra-rasl-64x64", "derived-start-at-cra-x265-64x64", new HevcDerivation("startAtIrap") { Occurrence = 0 }, 8, "hevc.raslSkipped", "hevc.nal=CRA");
        yield return Derived(vectors, "hm-random-access-cra-64x64", "derived-start-at-cra-hm-64x64", new HevcDerivation("startAtIrap") { Occurrence = 0 }, 5, "hevc.raslSkipped", "hevc.nal=CRA");
        yield return Derived(vectors, "hm-random-access-cra-422-10bit-64x64", "derived-start-at-cra-hm-422-10bit-64x64", new HevcDerivation("startAtIrap") { Occurrence = 0 }, 5, "hevc.raslSkipped", "hevc.chroma=4:2:2");

        // CRA pictures retyped as BLA pictures, as a splicer does: a BLA picture starts a coded video sequence, the
        // picture order count starts again from its least significant bits, and its RASL pictures are not output
        yield return Derived(vectors, "x265-open-gop-cra-rasl-64x64", "derived-bla-w-lp-x265-64x64", BrokenLink(NalBlaWLp), 12, "hevc.nal=BLA_W_LP", "hevc.raslSkipped", "hevc.sequences=3");
        yield return Derived(vectors, "x265-open-gop-cra-rasl-64x64", "derived-bla-w-radl-x265-64x64", BrokenLink(17), 12, "hevc.nal=BLA_W_RADL", "hevc.sequences=3");
        yield return Derived(vectors, "x265-open-gop-cra-rasl-64x64", "derived-bla-n-lp-x265-64x64", BrokenLink(18), 12, "hevc.nal=BLA_N_LP", "hevc.sequences=3");
        yield return Derived(vectors, "hm-random-access-cra-64x64", "derived-bla-w-lp-hm-64x64", BrokenLink(NalBlaWLp), 10, "hevc.nal=BLA_W_LP", "hevc.raslSkipped", "hevc.sequences=2");
        yield return Derived(vectors, "x265-open-gop-cra-rasl-422-10bit-64x64", "derived-bla-n-lp-x265-422-10bit-64x64", BrokenLink(18), 12, "hevc.nal=BLA_N_LP", "hevc.chroma=4:2:2");

        // An end of sequence NAL unit before a CRA picture: the picture starts a coded video sequence like a BLA picture,
        // and the pictures of the previous sequence that were not output yet are discarded
        yield return Derived(vectors, "x265-open-gop-cra-rasl-64x64", "derived-end-of-sequence-x265-64x64", new HevcDerivation("endOfSequence"), 8, "hevc.nal=EOS", "hevc.raslSkipped", "hevc.sequences=3", "hevc.noOutputOfPriorPics")
            .Without(DecoderLibde265, Libde265NoOutputOfPriorPics);
        yield return Derived(vectors, "hm-random-access-cra-64x64", "derived-end-of-sequence-hm-64x64", new HevcDerivation("endOfSequence"), 8, "hevc.nal=EOS", "hevc.raslSkipped", "hevc.sequences=2", "hevc.noOutputOfPriorPics")
            .Without(DecoderLibde265, Libde265NoOutputOfPriorPics);

        // The lower temporal sub-layers of a stream (sub-bitstream extraction): what a decoder that drops the higher
        // ones decodes
        yield return Derived(vectors, "hm-random-access-cra-64x64", "derived-sub-layer-0-hm-64x64", new HevcDerivation("subLayers") { MaxTemporalId = 0 }, 4, "hevc.nal=CRA");
        yield return Derived(vectors, "hm-random-access-cra-64x64", "derived-sub-layers-0-1-hm-64x64", new HevcDerivation("subLayers") { MaxTemporalId = 1 }, 7, "hevc.temporalLayers=2");
        yield return Derived(vectors, "x265-temporal-layers-64x64", "derived-sub-layer-0-x265-64x64", new HevcDerivation("subLayers") { MaxTemporalId = 0 }, 3, "hevc.frameTypes=inter");

        // Two streams one after the other: the picture size changes with the sequence parameter set of the second one
        yield return Derived(vectors, "x265-p-two-pictures-34x38", "derived-two-sizes-420-8bit", new HevcDerivation("concatenation") { Append = "x265-inter-420-8bit-66x50" }, 7, "hevc.frameSizes", "hevc.sequences=2");
        yield return Derived(vectors, "x265-intra-420-10bit-34x38", "derived-two-sizes-eos-420-10bit", new HevcDerivation("concatenation") { Append = "x265-inter-420-10bit-66x50", EndOfSequence = true }, 6, "hevc.frameSizes", "hevc.sequences=2", "hevc.nal=EOS")
            with { Inter = true };
        yield return Derived(vectors, "x265-intra-444-12bit-33x35", "derived-two-sizes-444-12bit", new HevcDerivation("concatenation") { Append = "x265-inter-444-12bit-65x49" }, 7, "hevc.frameSizes", "hevc.chroma=4:4:4")
            with { Inter = true };

        // Filler data, an end of sequence and an end of bitstream after the last picture
        yield return Derived(vectors, "x265-p-two-pictures-34x38", "derived-trailing-units-34x38", new HevcDerivation("trailingUnits"), 2, "hevc.nal=EOS", "hevc.nal=EOB", "hevc.nal=FD");
    }
}
