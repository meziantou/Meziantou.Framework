namespace Meziantou.Framework.Imaging.CorpusGenerator;

// The recipes of the vectors kvazaar encodes (through FFmpeg, 8-bit 4:2:0, 64x64 CTBs): what x265 does not write, tiles,
// dependent slice segments, a slice per tile, and another encoder's choices for the common tools.
internal static partial class HevcCorpus
{
    private static IEnumerable<HevcVector> KvazaarVectors()
    {
        // Intra and inter pictures of several CTBs
        yield return Kvazaar("kvazaar-intra-136x104", Noise(TestSrc2("136x104"), 8, 101), 1, inter: false, "qp=30", "hevc.ctb=64", "hevc.ctbs=3x2");
        yield return Kvazaar("kvazaar-intra-full-search-64x64", Mandelbrot("64x64"), 2, inter: false, "qp=24,full-intra-search=1,rd=2,tr-depth-intra=2", "hevc.frameTypes=intra").Hash("checksum");
        yield return Kvazaar("kvazaar-low-delay-p-64x64", Moving("64x64"), 6, inter: true, "gop=lp-g4d3t1,period=0,ref=2", "hevc.sliceTypes=P", "hevc.refs=2");
        yield return Kvazaar("kvazaar-low-delay-ref4-104x72", Rotating("104x72"), 7, inter: true, "gop=lp-g4d4t1,period=0,ref=4", "hevc.sliceTypes=P", "hevc.refs=3");
        yield return Kvazaar("kvazaar-random-access-gop8-64x64", Moving("64x64"), 12, inter: true, "gop=8,period=8", "hevc.nal=CRA", "hevc.nal=RASL_R", "hevc.sliceTypes=B", "hevc.outputOrder=reordered");
        yield return Kvazaar("kvazaar-random-access-gop8-closed-64x64", Moving("64x64"), 12, inter: true, "gop=8,period=8,open-gop=0", "hevc.sliceTypes=B", "hevc.outputOrder=reordered");
        yield return Kvazaar("kvazaar-random-access-gop16-96x64", Rotating("96x64"), 17, inter: true, "gop=16,period=16", "hevc.sliceTypes=B", "hevc.frames=17", "hevc.referenceB");
        yield return Kvazaar("kvazaar-poc-wrap-64x64", Moving("64x64"), 20, inter: true, "gop=lp-g4d3t1,period=0,ref=2,qp=36", "hevc.pocWrap", "hevc.pocLsbBits=4");
        yield return Kvazaar("kvazaar-inter-tools-64x64", Rotating("64x64"), 5, inter: true, "smp=1,amp=1,transform-skip=1,signhide=1,tmvp=0,bipred=1,max-merge=2,deblock=2:-2,period=0,gop=0,ref=2",
            "hevc.amp", "hevc.transformSkip", "hevc.signDataHiding=1", "hevc.temporalMvp=0", "hevc.mergeCand=2", "hevc.deblocking.offsets=2/-2");
        yield return Kvazaar("kvazaar-inter-tmvp-bipred-128x64", Moving("128x64"), 6, inter: true, "tmvp=1,bipred=1,period=0,gop=8,max-merge=5", "hevc.temporalMvp=1", "hevc.mergeCand=5", "hevc.sliceTypes=B");

        // Tiles. A tile of the Main profiles is at least 256 luma samples wide and 64 high (clause A.3.2), which HM
        // checks: the pictures are wide
        yield return Kvazaar("kvazaar-tiles-2x1-512x64", Noise(TestSrc2("512x64"), 6, 102), 1, inter: false, "tiles=2x1,qp=32", "hevc.tiles=2x1", "hevc.tiles.spacing=uniform", "hevc.tiles.loopFilterAcross=0");
        yield return Kvazaar("kvazaar-tiles-2x2-inter-512x128", Moving("512x128"), 3, inter: true, "tiles=2x2,qp=34,period=0,gop=0", "hevc.tiles=2x2", "hevc.frameTypes=inter");
        yield return Kvazaar("kvazaar-tiles-2x2-explicit-768x128", Noise(TestSrc2("768x128"), 6, 103), 1, inter: false, "tiles-width-split=256,tiles-height-split=64,qp=34", "hevc.tiles=2x2", "hevc.tiles.spacing=explicit").Hash("checksum");
        yield return Kvazaar("kvazaar-tiles-2x1-explicit-832x64", Noise(TestSrc2("832x64"), 6, 104), 2, inter: true, "tiles-width-split=320,qp=34,period=0,gop=0", "hevc.tiles=2x1", "hevc.tiles.spacing=explicit");

        // A slice per tile: the slices disable in-loop filtering across their boundaries. Without sample adaptive offset
        // the three decoders agree; with it, libde265 filters across the slices
        yield return Kvazaar("kvazaar-tiles-slices-no-sao-512x128", Noise(TestSrc2("512x128"), 6, 105), 1, inter: false, "tiles=2x2,slices=tiles,sao=off,qp=32", "hevc.tiles=2x2", "hevc.slices=4", "hevc.loopFilterAcrossSlices=0", "hevc.sao=off");
        yield return Kvazaar("kvazaar-tiles-slices-sao-512x128", Noise(TestSrc2("512x128"), 6, 106), 1, inter: false, "tiles=2x2,slices=tiles,qp=36", "hevc.tiles=2x2", "hevc.slices=4", "hevc.loopFilterAcrossSlices=0", "hevc.sao=luma")
            .Without(DecoderLibde265, Libde265SaoAcrossSlices);
        yield return Kvazaar("kvazaar-tiles-slices-sao-inter-512x128", Moving("512x128"), 3, inter: true, "tiles=2x2,slices=tiles,qp=36,period=0,gop=0", "hevc.tiles=2x2", "hevc.slices=4", "hevc.loopFilterAcrossSlices=0", "hevc.sao=luma")
            .Without(DecoderLibde265, Libde265SaoAcrossSlices);

        // Smaller tiles than the Main profiles allow, which FFmpeg and libde265 decode
        yield return Kvazaar("kvazaar-tiles-small-2x2-128x128", Noise(TestSrc2("128x128"), 6, 107), 1, inter: false, "tiles=2x2,qp=30", "hevc.tiles=2x2").Without(DecoderHm, HmSmallTiles);
        yield return Kvazaar("kvazaar-tiles-small-3x3-inter-192x192", Rotating("192x192"), 3, inter: true, "tiles=3x3,qp=34,period=0,gop=0", "hevc.tiles=3x3").Without(DecoderHm, HmSmallTiles);
        yield return Kvazaar("kvazaar-tiles-small-explicit-320x192", Noise(TestSrc2("320x192"), 6, 108), 1, inter: false, "tiles-width-split=64,tiles-height-split=128,qp=34", "hevc.tiles=2x2", "hevc.tiles.spacing=explicit").Without(DecoderHm, HmSmallTiles);

        // Wavefronts, with each CTB row in a dependent slice segment, and in a picture one CTB wide
        yield return Kvazaar("kvazaar-wpp-128x192", Noise(TestSrc2("128x192"), 6, 109), 1, inter: false, "wpp=1,qp=30", "hevc.wpp", "hevc.ctbs=2x3");
        yield return Kvazaar("kvazaar-wpp-inter-192x128", Moving("192x128"), 4, inter: true, "wpp=1,qp=32,period=0,gop=0", "hevc.wpp", "hevc.frameTypes=inter");
        yield return Kvazaar("kvazaar-wpp-dependent-slices-128x192", Noise(TestSrc2("128x192"), 6, 110), 2, inter: true, "wpp=1,slices=wpp,qp=30,period=0,gop=0", "hevc.wpp", "hevc.dependentSlices=3").Hash("checksum");
        yield return Kvazaar("kvazaar-wpp-one-ctb-wide-64x192", Noise(TestSrc2("64x192"), 6, 111), 2, inter: true, "wpp=1,qp=30,period=0,gop=0", "hevc.wpp", "hevc.ctbs=1x3");
        yield return Kvazaar("kvazaar-wpp-one-ctb-wide-dependent-slices-48x256", Noise(TestSrc2("48x256"), 6, 112), 1, inter: false, "wpp=1,slices=wpp,qp=28", "hevc.wpp", "hevc.ctbs=1x4", "hevc.dependentSlices=4");

        // Lossless, quantization and filters as kvazaar writes them
        yield return Kvazaar("kvazaar-lossless-64x64", Noise(Moving("64x64"), 6, 113), 3, inter: true, "lossless=1,period=0,gop=0", "hevc.lossless");
        yield return Kvazaar("kvazaar-lossless-implicit-rdpcm-64x64", Noise(Moving("64x64"), 6, 120), 3, inter: true, "lossless=1,implicit-rdpcm=1,period=0,gop=0", "hevc.lossless", "hevc.implicitRdpcm")
            .Without(DecoderFfmpeg, FfmpegBypassResidualTools);
        yield return Kvazaar("kvazaar-sao-band-64x64", Noise(TestSrc2("64x64"), 16, 114), 1, inter: false, "sao=band,qp=38", "hevc.sao=luma");
        yield return Kvazaar("kvazaar-sao-edge-64x64", Noise(TestSrc2("64x64"), 16, 115), 1, inter: false, "sao=edge,qp=38", "hevc.sao=luma");
        yield return Kvazaar("kvazaar-sao-full-inter-128x64", Noise(Moving("128x64"), 16, 116), 4, inter: true, "sao=full,qp=40,period=0,gop=0", "hevc.sao=luma", "hevc.sao=chroma");
        yield return Kvazaar("kvazaar-no-filters-64x64", Noise(TestSrc2("64x64"), 10, 117), 2, inter: true, "sao=off,deblock=0,qp=36,period=0,gop=0", "hevc.sao=off", "hevc.deblocking=0");
        yield return Kvazaar("kvazaar-scaling-default-64x64", Noise(TestSrc2("64x64"), 10, 118), 2, inter: true, "scaling-list=default,qp=30,period=0,gop=0", "hevc.scalingList=default");

        // No "scaling-list=custom,cqmfile=...": kvazaar 2.3.2 writes lists with coefficients equal to 0, which clause 7.4.5
        // forbids (see the traps of HevcCorpus.Vectors.cs). The lists of a file are in the vectors of x265 and HM

        yield return Kvazaar("kvazaar-vaq-128x64", Noise(TestSrc2("128x64"), 12, 119), 2, inter: true, "vaq=5,qp=30,period=0,gop=0", "hevc.cuQpDelta");
        yield return Kvazaar("kvazaar-aud-vps-period-64x64", Moving("64x64"), 6, inter: true, "aud=1,vps-period=1,period=2,gop=0", "hevc.nal=AUD", "hevc.repeatedParameterSets");
    }
}
