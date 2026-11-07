namespace Meziantou.Framework.Imaging.CorpusGenerator;

// The recipes of the vectors x265 encodes (through FFmpeg): every chroma format at 8, 10 and 12 bits, and most of the
// coding tools of version 1 of the specification. See HevcCorpus.Vectors.cs for the helpers and the traps.
internal static partial class HevcCorpus
{
    private static IEnumerable<HevcVector> X265IntraVectors()
    {
        // Every chroma format at 8, 10 and 12 bits. The sizes are not multiples of the 8x8 minimum coding block: the
        // picture is padded and cropped by the conformance window, to an odd size where the chroma format has no
        // subsampling in that direction. An all-intra stream of x265 signals an intra profile of the range extensions
        foreach (var (depth, suffix) in new[] { (8, ""), (10, "10le"), (12, "12le") })
        {
            yield return X265Intra($"x265-intra-420-{depth}bit-34x38", TestSrc2("34x38"), "yuv420p" + suffix, 1, "", "hevc.chroma=4:2:0", $"hevc.bitDepth={depth}", "hevc.crop=bottom", "hevc.crop=right", "hevc.frameTypes=intra");
            yield return X265Intra($"x265-intra-422-{depth}bit-34x37", Noise(Mandelbrot("34x37"), 6, depth), "yuv422p" + suffix, 1, "", "hevc.chroma=4:2:2", $"hevc.bitDepth={depth}", "hevc.crop=bottom").Hash("checksum");
            yield return X265Intra($"x265-intra-444-{depth}bit-33x35", Noise(TestSrc("33x35"), 8, depth), "yuv444p" + suffix, 2, "", "hevc.chroma=4:4:4", $"hevc.bitDepth={depth}", "hevc.crop=right");
            yield return X265Intra($"x265-intra-400-{depth}bit-35x33", Noise(TestSrc("35x33"), 6, depth), "gray" + suffix, 1, "", "hevc.chroma=4:0:0", $"hevc.bitDepth={depth}", "hevc.crop=right").Hash("crc")
                with { Excluded = depth > 8 ? [(DecoderLibde265, Libde265MonochromeOutput)] : [] };
            yield return X265Intra($"x265-intra-gbr-{depth}bit-37x35", Mandelbrot("37x35"), "gbrp" + suffix, 1, "", "hevc.matrix=identity", $"hevc.bitDepth={depth}", "hevc.chroma=4:4:4");
        }

        // Coding tree block and minimum coding block sizes, several CTBs in both directions. The CRC of x265 is only
        // right for whole CTBs (see the traps)
        yield return X265Intra("x265-intra-ctb16-cb8-80x48", Noise(TestSrc2("80x48"), 8, 1), "yuv420p", 1, "ctu=16:min-cu-size=8", "hevc.ctb=16", "hevc.minCb=8", "hevc.ctbs=5x3");
        yield return X265Intra("x265-intra-ctb32-cb8-100x76", Noise(TestSrc2("100x76"), 8, 2), "yuv420p", 1, "ctu=32:min-cu-size=8", "hevc.ctb=32", "hevc.minCb=8", "hevc.ctbs=4x3");
        yield return X265Intra("x265-intra-ctb32-cb16-96x64", Noise(Mandelbrot("96x64"), 8, 3), "yuv420p", 1, "ctu=32:min-cu-size=16", "hevc.ctb=32", "hevc.minCb=16");
        yield return X265Intra("x265-intra-ctb64-cb8-136x72", Noise(TestSrc2("136x72"), 8, 4), "yuv420p", 1, "ctu=64:min-cu-size=8", "hevc.ctb=64", "hevc.minCb=8", "hevc.ctbs=3x2");
        yield return X265Intra("x265-intra-ctb64-cb16-130x70", Noise(TestSrc("130x70"), 8, 5), "yuv420p", 1, "ctu=64:min-cu-size=16", "hevc.ctb=64", "hevc.minCb=16", "hevc.crop=bottom").Hash("checksum");
        yield return X265Intra("x265-intra-ctb64-cb32-128x128", Noise(Mandelbrot("128x128"), 8, 6), "yuv420p", 1, "ctu=64:min-cu-size=32", "hevc.ctb=64", "hevc.minCb=32");
        yield return X265Intra("x265-intra-ctb16-422-10bit-52x40", Noise(TestSrc2("52x40"), 8, 7), "yuv422p10le", 1, "ctu=16", "hevc.ctb=16", "hevc.chroma=4:2:2");
        yield return X265Intra("x265-intra-ctb32-422-12bit-70x50", Noise(TestSrc2("70x50"), 8, 8), "yuv422p12le", 1, "ctu=32", "hevc.ctb=32", "hevc.chroma=4:2:2");
        yield return X265Intra("x265-intra-ctb64-422-8bit-136x70", Noise(Mandelbrot("136x70"), 8, 9), "yuv422p", 1, "ctu=64", "hevc.ctb=64", "hevc.chroma=4:2:2");
        yield return X265Intra("x265-intra-ctb16-444-12bit-40x56", Noise(TestSrc2("40x56"), 8, 10), "yuv444p12le", 1, "ctu=16", "hevc.ctb=16", "hevc.chroma=4:4:4");
        yield return X265Intra("x265-intra-ctb64-444-10bit-131x67", Noise(Cropped(TestSrc2("132x68"), "yuv444p10le", "131x67"), 8, 11), "yuv444p10le", 1, "ctu=64", "hevc.ctb=64", "hevc.chroma=4:4:4");
        yield return X265Intra("x265-intra-ctb64-400-10bit-136x66", Noise(Mandelbrot("136x66"), 8, 12), "gray10le", 1, "ctu=64", "hevc.ctb=64", "hevc.chroma=4:0:0");
        yield return X265Intra("x265-intra-smallest-16x16", TestSrc2("16x16"), "yuv420p", 1, "ctu=16", "hevc.ctbs=1x1");
        yield return X265Intra("x265-intra-cif-352x288", Noise(TestSrc2("352x288"), 6, 13), "yuv420p", 1, "ctu=64:qp=34", "hevc.ctbs=6x5");

        // The stills the benchmarks decode: 384x216, lossy, with the deblocking filter and sample adaptive offset, on a
        // picture that has smooth areas and fine detail (the Mandelbrot set, under a noise that leaves a residual in every block)
        yield return X265Intra("x265-benchmark-intra-420-384x216", Noise(Mandelbrot("384x216"), 8, 301), "yuv420p", 1, "ctu=64:qp=24", "hevc.deblocking=1", "hevc.sao=luma", "hevc.sao=chroma", "hevc.ctbs=6x4");
        yield return X265Intra("x265-benchmark-intra-444-384x216", Noise(Mandelbrot("384x216"), 8, 302), "yuv444p", 1, "ctu=64:qp=24", "hevc.deblocking=1", "hevc.sao=luma", "hevc.sao=chroma", "hevc.chroma=4:4:4");

        // Transform block sizes and transform hierarchy depths; a search that tries every intra mode on varied content
        yield return X265Intra("x265-intra-tb32-depth1-96x64", Noise(Mandelbrot("96x64"), 8, 14), "yuv420p", 1, "ctu=32:max-tu-size=32:tu-intra-depth=1", "hevc.maxTb=32", "hevc.tuDepthIntra=0");
        yield return X265Intra("x265-intra-tb32-depth4-128x64", Noise(TestSrc2("128x64"), 10, 15), "yuv420p", 1, "ctu=64:max-tu-size=32:tu-intra-depth=4:rd=5", "hevc.maxTb=32", "hevc.tuDepthIntra=3");
        yield return X265Intra("x265-intra-tb16-depth3-72x56", Noise(TestSrc2("72x56"), 10, 16), "yuv420p", 1, "ctu=32:max-tu-size=16:tu-intra-depth=3:rd=5", "hevc.maxTb=16", "hevc.tuDepthIntra=2");
        yield return X265Intra("x265-intra-tb8-depth2-56x40", Noise(TestSrc("56x40"), 10, 17), "yuv420p", 1, "ctu=16:max-tu-size=8:tu-intra-depth=2", "hevc.maxTb=8", "hevc.tuDepthIntra=1");
        yield return X265Intra("x265-intra-tb4-48x40", Noise(TestSrc2("48x40"), 10, 18), "yuv420p", 1, "ctu=16:max-tu-size=4", "hevc.maxTb=4");
        yield return X265Intra("x265-intra-tb4-444-10bit-40x40", Noise(TestSrc2("40x40"), 10, 19), "yuv444p10le", 1, "ctu=16:max-tu-size=4", "hevc.maxTb=4", "hevc.chroma=4:4:4");
        yield return X265Intra("x265-intra-tb4-422-8bit-40x44", Noise(TestSrc2("40x44"), 10, 20), "yuv422p", 1, "ctu=16:max-tu-size=4", "hevc.maxTb=4", "hevc.chroma=4:2:2");
        yield return X265Intra("x265-intra-modes-mandelbrot-96x80", Mandelbrot("96x80"), "yuv420p", 1, "ctu=32:rd=6:fast-intra=0:qp=22", "hevc.ctb=32");
        yield return X265Intra("x265-intra-modes-rotated-96x80", Rotating("96x80"), "yuv420p", 3, "ctu=64:rd=6:fast-intra=0:qp=24", "hevc.ctb=64", "hevc.frames=3");
        yield return X265Intra("x265-intra-modes-rotated-444-10bit-64x64", Rotating("64x64"), "yuv444p10le", 2, "ctu=32:rd=6:fast-intra=0:qp=20", "hevc.chroma=4:4:4");
        yield return X265Intra("x265-intra-modes-rotated-422-10bit-64x64", Rotating("64x64"), "yuv422p10le", 2, "ctu=32:rd=6:fast-intra=0:qp=20", "hevc.chroma=4:2:2");
        yield return X265Intra("x265-intra-modes-noise-400-12bit-64x48", Noise(Rotating("64x48"), 20, 21), "gray12le", 2, "ctu=16:rd=6:fast-intra=0:qp=18", "hevc.chroma=4:0:0");

        // Intra tools of the parameter sets: strong intra smoothing (32x32 blocks), transform skip, sign data hiding
        yield return X265Intra("x265-intra-strong-smoothing-off-128x64", Mandelbrot("128x64"), "yuv420p", 1, "ctu=64:strong-intra-smoothing=0:qp=36", "hevc.strongIntraSmoothing=0");
        yield return X265Intra("x265-intra-strong-smoothing-on-128x64", Mandelbrot("128x64"), "yuv420p", 1, "ctu=64:strong-intra-smoothing=1:qp=36", "hevc.strongIntraSmoothing=1").Hash("crc");
        yield return X265Intra("x265-intra-strong-smoothing-10bit-128x64", Mandelbrot("128x64"), "yuv420p10le", 1, "ctu=64:strong-intra-smoothing=1:qp=38", "hevc.strongIntraSmoothing=1", "hevc.bitDepth=10");
        yield return X265Intra("x265-intra-tskip-64x48", Noise(TestSrc("64x48"), 25, 22), "yuv420p", 1, "tskip=1:rd=5:qp=24", "hevc.transformSkip");
        yield return X265Intra("x265-intra-tskip-444-12bit-48x48", Noise(TestSrc("48x48"), 25, 23), "yuv444p12le", 1, "tskip=1:rd=5:qp=24", "hevc.transformSkip", "hevc.bitDepth=12");
        yield return X265Intra("x265-intra-tskip-422-10bit-48x40", Noise(TestSrc("48x40"), 25, 24), "yuv422p10le", 1, "tskip=1:rd=5:qp=24", "hevc.transformSkip", "hevc.chroma=4:2:2");
        yield return X265Intra("x265-intra-signhide-off-64x64", Noise(TestSrc2("64x64"), 10, 25), "yuv420p", 1, "signhide=0", "hevc.signDataHiding=0");
        yield return X265Intra("x265-intra-signhide-on-rdoq-64x64", Noise(TestSrc2("64x64"), 10, 25), "yuv420p", 1, "signhide=1:rd=5:rdoq-level=2", "hevc.signDataHiding=1");

        // Quantization: the whole range of slice QPs, chroma QP offsets in every chroma format (their mapping to the
        // chroma QP depends on it), QP deltas in quantization groups of every size
        yield return X265Intra("x265-intra-qp0-48x48", Noise(TestSrc2("48x48"), 10, 26), "yuv420p", 1, "qp=0:ipratio=1", "hevc.sliceQp=0");
        yield return X265Intra("x265-intra-qp1-444-48x48", Noise(TestSrc2("48x48"), 10, 26), "yuv444p", 1, "qp=1:ipratio=1", "hevc.sliceQp=1");
        yield return X265Intra("x265-intra-qp12-10bit-48x48", Noise(TestSrc2("48x48"), 10, 27), "yuv420p10le", 1, "qp=12:ipratio=1", "hevc.sliceQp=12");
        yield return X265Intra("x265-intra-qp22-422-12bit-48x48", Noise(TestSrc2("48x48"), 10, 28), "yuv422p12le", 1, "qp=22:ipratio=1", "hevc.sliceQp=22");
        yield return X265Intra("x265-intra-qp40-64x64", Noise(TestSrc2("64x64"), 10, 29), "yuv420p", 1, "qp=40:ipratio=1", "hevc.sliceQp=40").Hash("crc");
        yield return X265Intra("x265-intra-qp51-64x64", Noise(TestSrc2("64x64"), 10, 30), "yuv420p", 1, "qp=51:ipratio=1", "hevc.sliceQp=51");
        yield return X265Intra("x265-intra-qp51-420-12bit-64x64", Noise(TestSrc2("64x64"), 10, 31), "yuv420p12le", 1, "qp=51:ipratio=1", "hevc.sliceQp=51", "hevc.bitDepth=12").Without(DecoderFfmpeg, FfmpegHighQp12Bit);
        yield return X265Intra("x265-intra-chroma-qp-offsets-420-64x48", Noise(TestSrc2("64x48"), 10, 32), "yuv420p", 1, "qp=30:cbqpoffs=-6:crqpoffs=8", "hevc.chromaQpOffset=-6/8");
        yield return X265Intra("x265-intra-chroma-qp-offsets-422-64x48", Noise(TestSrc2("64x48"), 10, 33), "yuv422p", 1, "qp=44:cbqpoffs=6:crqpoffs=-4", "hevc.chromaQpOffset=6/-4", "hevc.chroma=4:2:2");
        yield return X265Intra("x265-intra-chroma-qp-offsets-444-10bit-64x48", Noise(TestSrc2("64x48"), 10, 34), "yuv444p10le", 1, "qp=40:cbqpoffs=12:crqpoffs=-12", "hevc.chromaQpOffset=12/-12", "hevc.chroma=4:4:4");
        yield return X265Intra("x265-intra-chroma-qp-high-420-64x48", Noise(TestSrc2("64x48"), 10, 35), "yuv420p", 1, "qp=48:cbqpoffs=3:crqpoffs=1:ipratio=1", "hevc.chromaQpOffset=3/1", "hevc.sliceQp=48");
        yield return X265Intra("x265-intra-aq-qg64-136x72", Noise(TestSrc2("136x72"), 12, 36), "yuv420p", 1, "ctu=64:aq-mode=1:qg-size=64:crf=28", "hevc.cuQpDelta", "hevc.qgSize=64");
        yield return X265Intra("x265-intra-aq-qg32-136x72", Noise(TestSrc2("136x72"), 12, 37), "yuv420p", 1, "ctu=64:aq-mode=2:qg-size=32:crf=28", "hevc.cuQpDelta", "hevc.qgSize=32");
        yield return X265Intra("x265-intra-aq-qg16-136x72", Noise(Mandelbrot("136x72"), 12, 38), "yuv420p", 1, "ctu=64:aq-mode=3:qg-size=16:crf=26", "hevc.cuQpDelta", "hevc.qgSize=16");
        yield return X265Intra("x265-intra-aq-qg8-100x76", Noise(TestSrc2("100x76"), 12, 39), "yuv420p", 1, "ctu=32:aq-mode=4:qg-size=8:crf=24", "hevc.cuQpDelta", "hevc.qgSize=8");
        yield return X265Intra("x265-intra-aq-qg16-422-10bit-72x72", Noise(TestSrc2("72x72"), 12, 40), "yuv422p10le", 1, "ctu=32:aq-mode=3:aq-strength=2:qg-size=16:crf=26", "hevc.cuQpDelta", "hevc.qgSize=16", "hevc.chroma=4:2:2");
        yield return X265Intra("x265-intra-aq-qg8-444-12bit-72x72", Noise(TestSrc2("72x72"), 12, 41), "yuv444p12le", 1, "ctu=16:aq-mode=3:aq-strength=2:qg-size=8:crf=26", "hevc.cuQpDelta", "hevc.qgSize=8", "hevc.chroma=4:4:4");

        // Scaling lists: the default lists, and lists of the sequence parameter set, some coded and some copied
        yield return X265Intra("x265-intra-scaling-default-64x64", Noise(TestSrc2("64x64"), 10, 42), "yuv420p", 1, "scaling-list=default:ctu=32", "hevc.scalingList=default");
        yield return X265Intra("x265-intra-scaling-default-444-10bit-64x64", Noise(TestSrc2("64x64"), 10, 43), "yuv444p10le", 1, "scaling-list=default:ctu=32", "hevc.scalingList=default", "hevc.chroma=4:4:4");
        yield return X265Intra("x265-intra-scaling-default-422-12bit-64x64", Noise(TestSrc2("64x64"), 10, 44), "yuv422p12le", 1, "scaling-list=default:ctu=32", "hevc.scalingList=default", "hevc.chroma=4:2:2");
        yield return X265Intra("x265-intra-scaling-sps-64x64", Noise(TestSrc2("64x64"), 10, 45), "yuv420p", 1, "scaling-list={file}:ctu=32", "hevc.scalingList=sps", "hevc.scalingList.coding=explicit")
            with { File = ScalingLists(1, copies: false) };
        yield return X265Intra("x265-intra-scaling-sps-copies-444-64x64", Noise(TestSrc2("64x64"), 10, 46), "yuv444p", 1, "scaling-list={file}:ctu=32", "hevc.scalingList=sps", "hevc.scalingList.coding=copy", "hevc.chroma=4:4:4")
            with { File = ScalingLists(2, copies: true) };
        yield return X265Intra("x265-intra-scaling-sps-default-4x4-422-64x64", Noise(TestSrc2("64x64"), 10, 86), "yuv422p", 1, "scaling-list={file}:ctu=32", "hevc.scalingList=sps", "hevc.scalingList.coding=default", "hevc.chroma=4:2:2")
            with { File = ScalingLists(8, copies: false, defaults: true) };
        yield return X265Intra("x265-intra-scaling-sps-10bit-136x72", Noise(Mandelbrot("136x72"), 10, 47), "yuv420p10le", 1, "scaling-list={file}:ctu=64", "hevc.scalingList=sps", "hevc.bitDepth=10")
            with { File = ScalingLists(3, copies: false) };

        // In-loop filters: deblocking on, with offsets, and off; sample adaptive offset on and off, at every bit depth
        // (its offsets are scaled above 10 bits)
        yield return X265Intra("x265-intra-deblock-offsets-64x64", Noise(TestSrc2("64x64"), 10, 48), "yuv420p", 1, "deblock=-3,4:qp=36", "hevc.deblocking=1", "hevc.deblocking.offsets=4/-3");
        yield return X265Intra("x265-intra-deblock-offsets-max-422-64x64", Noise(TestSrc2("64x64"), 10, 49), "yuv422p", 1, "deblock=6,6:qp=40", "hevc.deblocking.offsets=6/6", "hevc.chroma=4:2:2");
        yield return X265Intra("x265-intra-deblock-offsets-min-444-10bit-64x64", Noise(TestSrc2("64x64"), 10, 50), "yuv444p10le", 1, "deblock=-6,-6:qp=40", "hevc.deblocking.offsets=-6/-6", "hevc.chroma=4:4:4");
        yield return X265Intra("x265-intra-no-filters-64x64", Noise(TestSrc2("64x64"), 10, 51), "yuv420p", 1, "no-deblock=1:sao=0:qp=38", "hevc.deblocking=0", "hevc.sao=off");
        yield return X265Intra("x265-intra-deblock-only-64x64", Noise(TestSrc2("64x64"), 10, 52), "yuv420p", 1, "sao=0:qp=42", "hevc.deblocking=1", "hevc.sao=off");
        yield return X265Intra("x265-intra-sao-only-64x64", Noise(TestSrc2("64x64"), 10, 53), "yuv420p", 1, "no-deblock=1:sao=1:qp=42", "hevc.deblocking=0", "hevc.sao=luma");
        yield return X265Intra("x265-intra-sao-8bit-136x72", Noise(TestSrc2("136x72"), 18, 54), "yuv420p", 1, "ctu=64:sao=1:qp=40", "hevc.sao=luma", "hevc.sao=chroma");
        yield return X265Intra("x265-intra-sao-ctb16-10bit-72x56", Noise(TestSrc2("72x56"), 18, 55), "yuv420p10le", 1, "ctu=16:sao=1:qp=40", "hevc.sao=luma", "hevc.bitDepth=10", "hevc.ctb=16");
        yield return X265Intra("x265-intra-sao-12bit-72x56", Noise(TestSrc2("72x56"), 18, 56), "yuv420p12le", 1, "ctu=32:sao=1:qp=40", "hevc.sao=luma", "hevc.bitDepth=12");
        yield return X265Intra("x265-intra-sao-444-12bit-72x56", Noise(Mandelbrot("72x56"), 18, 57), "yuv444p12le", 1, "ctu=32:sao=1:qp=38", "hevc.sao=chroma", "hevc.bitDepth=12", "hevc.chroma=4:4:4");
        yield return X265Intra("x265-intra-sao-422-10bit-72x56", Noise(Mandelbrot("72x56"), 18, 58), "yuv422p10le", 1, "ctu=32:sao=1:qp=38", "hevc.sao=chroma", "hevc.chroma=4:2:2");
        yield return X265Intra("x265-intra-sao-400-8bit-72x56", Noise(TestSrc2("72x56"), 18, 59), "gray", 1, "ctu=32:sao=1:qp=38", "hevc.sao=luma", "hevc.chroma=4:0:0");
        yield return X265Intra("x265-intra-sao-non-deblock-72x56", Noise(TestSrc2("72x56"), 18, 60), "yuv420p", 1, "ctu=16:sao=1:sao-non-deblock=1:limit-sao=1:qp=44", "hevc.sao=luma");

        // Lossless: every coding unit bypasses the transform, the quantization and the filters. The decoded pictures
        // are the source pictures, which the generator checks ("hevc.lossless")
        yield return X265Intra("x265-lossless-intra-420-8bit-64x48", Noise(TestSrc2("64x48"), 10, 61), "yuv420p", 1, "lossless=1:ctu=32", "hevc.lossless", "hevc.level=8.5");
        yield return X265Intra("x265-lossless-intra-422-12bit-66x50", Noise(TestSrc2("66x50"), 10, 62), "yuv422p12le", 1, "lossless=1:ctu=32", "hevc.lossless", "hevc.chroma=4:2:2");
        yield return X265Intra("x265-lossless-intra-444-10bit-65x47", Noise(Cropped(TestSrc2("66x48"), "yuv444p10le", "65x47"), 10, 63), "yuv444p10le", 1, "lossless=1:ctu=32", "hevc.lossless", "hevc.chroma=4:4:4").Hash("checksum");
        yield return X265Intra("x265-lossless-intra-400-8bit-65x47", Noise(Cropped(TestSrc2("66x48"), "gray", "65x47"), 10, 64), "gray", 1, "lossless=1:ctu=32", "hevc.lossless", "hevc.chroma=4:0:0");
        yield return X265Intra("x265-lossless-intra-gbr-8bit-34x36", Noise(TestSrc("34x36"), 10, 65), "gbrp", 1, "lossless=1:ctu=32", "hevc.lossless", "hevc.matrix=identity");
        yield return X265Intra("x265-lossless-intra-gbr-12bit-34x36", Noise(TestSrc("34x36"), 10, 66), "gbrp12le", 1, "lossless=1:ctu=32", "hevc.lossless", "hevc.matrix=identity", "hevc.bitDepth=12");
        yield return X265Intra("x265-lossless-intra-ctb16-48x48", Noise(TestSrc2("48x48"), 10, 67), "yuv420p", 1, "lossless=1:ctu=16", "hevc.lossless", "hevc.ctb=16", "hevc.level=8.5").Without(DecoderHm, HmSmallCtbAtHighLevel);
        yield return X265Intra("x265-cu-lossless-intra-64x64", Noise(TestSrc2("64x64"), 10, 68), "yuv420p", 1, "cu-lossless=1:qp=30:ctu=32", "hevc.transquantBypass");
        yield return X265Intra("x265-cu-lossless-intra-444-10bit-64x64", Noise(TestSrc("64x64"), 6, 69), "yuv444p10le", 1, "cu-lossless=1:qp=34:ctu=32", "hevc.transquantBypass", "hevc.chroma=4:4:4");

        // Wavefronts and slices (x265 only codes several slices with wavefronts)
        yield return X265Intra("x265-intra-wpp-ctb16-64x64", Noise(TestSrc2("64x64"), 10, 70), "yuv420p", 1, "wpp=1:ctu=16", "hevc.wpp", "hevc.ctbs=4x4");
        yield return X265Intra("x265-intra-wpp-ctb64-136x136", Noise(TestSrc2("136x136"), 10, 71), "yuv420p", 1, "wpp=1:ctu=64", "hevc.wpp", "hevc.ctbs=3x3");
        yield return X265Intra("x265-intra-wpp-444-10bit-64x64", Noise(TestSrc2("64x64"), 10, 72), "yuv444p10le", 1, "wpp=1:ctu=16", "hevc.wpp", "hevc.chroma=4:4:4");
        yield return X265Intra("x265-intra-slices-4-64x128", Noise(TestSrc2("64x128"), 10, 73), "yuv420p", 1, "slices=4:wpp=1:ctu=16", "hevc.slices=4", "hevc.loopFilterAcrossSlices=0");
        yield return X265Intra("x265-intra-slices-2-422-10bit-64x96", Noise(TestSrc2("64x96"), 10, 74), "yuv422p10le", 1, "slices=2:wpp=1:ctu=16", "hevc.slices=2", "hevc.chroma=4:2:2");
        yield return X265Intra("x265-intra-slices-3-wpp-64x96", Noise(TestSrc2("64x96"), 10, 75), "yuv420p", 1, "slices=3:wpp=1:ctu=16", "hevc.slices=3", "hevc.wpp");

        // Video usability information and SEI messages: nothing of it changes a decoded sample, and the default
        // display window is not applied
        yield return X265Intra("x265-intra-vui-bt2020-pq-10bit-64x64", TestSrc2("64x64"), "yuv420p10le", 1,
            "colorprim=bt2020:transfer=smpte2084:colormatrix=bt2020nc:chromaloc=2:master-display=G(13250,34500)B(7500,3000)R(34000,16000)WP(15635,16450)L(10000000,1):max-cll=1000,400",
            "hevc.colourDescription=9/16/9", "hevc.chromaLoc=2", "hevc.sei=masteringDisplayColourVolume", "hevc.sei=contentLightLevel");
        yield return X265Intra("x265-intra-vui-full-range-64x64", TestSrc2("64x64"), "yuv420p", 1, "range=full:colorprim=bt709:transfer=iec61966-2-1:colormatrix=bt470bg:chromaloc=1", "hevc.fullRange", "hevc.colourDescription=1/13/5", "hevc.chromaLoc=1");
        yield return X265Intra("x265-intra-vui-display-window-64x64", TestSrc2("64x64"), "yuv420p", 1, "display-window=4,2,8,6:overscan=show:sar=4:videoformat=pal:chromaloc=3", "hevc.defaultDisplayWindow", "hevc.chromaLoc=3");
        yield return X265Intra("x265-intra-vui-display-window-444-66x66", TestSrc2("66x66"), "yuv444p", 1, "display-window=3,1,5,7", "hevc.defaultDisplayWindow", "hevc.crop=bottom");
        yield return X265Intra("x265-intra-aud-hrd-64x64", TestSrc2("64x64"), "yuv420p", 2, "aud=1:hrd=1:vbv-bufsize=2000:vbv-maxrate=2000:repeat-headers=1", "hevc.nal=AUD", "hevc.hrd", "hevc.sei=bufferingPeriod", "hevc.sei=pictureTiming", "hevc.repeatedParameterSets");
        yield return X265Intra("x265-intra-high-tier-64x64", TestSrc2("64x64"), "yuv420p", 1, "level-idc=41:high-tier=1", "hevc.tier=high", "hevc.level=4.1");
        yield return X265Intra("x265-intra-info-sei-32x32", TestSrc2("32x32"), "yuv420p", 1, "info=1", "hevc.sei=userDataUnregistered");
        yield return X265Intra("x265-intra-atc-recovery-sei-64x64", TestSrc2("64x64"), "yuv420p10le", 2, "atc-sei=18:idr-recovery-sei=1:transfer=arib-std-b67", "hevc.sei=alternativeTransferCharacteristics", "hevc.sei=recoveryPoint");
    }

    private static IEnumerable<HevcVector> X265InterVectors()
    {
        // The profiles of version 1, and every chroma format at 8, 10 and 12 bits with P and B pictures
        foreach (var (depth, suffix) in new[] { (8, ""), (10, "10le"), (12, "12le") })
        {
            yield return X265($"x265-inter-420-{depth}bit-66x50", Moving("66x50"), "yuv420p" + suffix, 5, "bframes=2:b-adapt=0", "hevc.chroma=4:2:0", $"hevc.bitDepth={depth}", "hevc.sliceTypes=B", "hevc.sliceTypes=P", "hevc.outputOrder=reordered",
                depth == 8 ? "hevc.profile=main" : depth == 10 ? "hevc.profile=main10" : "hevc.profile=rext-420-12");
            yield return X265($"x265-inter-422-{depth}bit-66x49", Cropped(Moving("66x50"), "yuv422p" + suffix, "66x49"), "yuv422p" + suffix, 5, "bframes=2:b-adapt=0", "hevc.chroma=4:2:2", $"hevc.bitDepth={depth}", "hevc.sliceTypes=B").Hash("checksum");
            yield return X265($"x265-inter-444-{depth}bit-65x49", Cropped(Rotating("66x50"), "yuv444p" + suffix, "65x49"), "yuv444p" + suffix, 5, "bframes=2:b-adapt=0", "hevc.chroma=4:4:4", $"hevc.bitDepth={depth}", "hevc.sliceTypes=B");
            yield return X265($"x265-inter-400-{depth}bit-65x49", Cropped(Rotating("66x50"), "gray" + suffix, "65x49"), "gray" + suffix, 5, "bframes=2:b-adapt=0", "hevc.chroma=4:0:0", $"hevc.bitDepth={depth}", "hevc.sliceTypes=B").Hash("crc")
                with { Excluded = depth > 8 ? [(DecoderLibde265, Libde265MonochromeOutput)] : [] };
            yield return X265($"x265-inter-gbr-{depth}bit-64x48", Moving("64x48"), "gbrp" + suffix, 4, "bframes=1:b-adapt=0", "hevc.matrix=identity", $"hevc.bitDepth={depth}", "hevc.sliceTypes=B");
        }

        // A monochrome stream above 8 bits that x265 does not crop on the right: the three decoders write its pictures
        yield return X265("x265-b-400-12bit-64x48", Moving("64x48"), "gray12le", 5, "bframes=2:b-adapt=0", "hevc.chroma=4:0:0", "hevc.bitDepth=12", "hevc.sliceTypes=B");
        yield return X265("x265-b-400-10bit-72x56", Rotating("72x56"), "gray10le", 5, "bframes=2:b-adapt=0", "hevc.chroma=4:0:0", "hevc.bitDepth=10", "hevc.sliceTypes=B").Hash("checksum");

        // P pictures only: output in decoding order, one to four references, merge candidates
        yield return X265("x265-p-ref1-merge1-64x64", Moving("64x64"), "yuv420p", 4, "bframes=0:ref=1:max-merge=1", "hevc.sliceTypes=P", "hevc.refs=1", "hevc.mergeCand=1");
        yield return X265("x265-p-ref2-merge2-64x64", Rotating("64x64"), "yuv420p", 5, "bframes=0:ref=2:max-merge=2", "hevc.refs=2", "hevc.mergeCand=2");
        yield return X265("x265-p-ref3-merge3-64x64", Rotating("64x64"), "yuv420p", 6, "bframes=0:ref=3:max-merge=3", "hevc.refs=3", "hevc.mergeCand=3");
        yield return X265("x265-p-ref4-merge4-64x64", Moving("64x64"), "yuv420p", 7, "bframes=0:ref=4:max-merge=4", "hevc.refs=4", "hevc.mergeCand=4");
        yield return X265("x265-p-merge5-422-10bit-64x64", Rotating("64x64"), "yuv422p10le", 4, "bframes=0:ref=2:max-merge=5", "hevc.mergeCand=5", "hevc.chroma=4:2:2");
        yield return X265("x265-p-two-pictures-34x38", Moving("34x38"), "yuv420p", 2, "bframes=0", "hevc.frames=2", "hevc.sliceTypes=P");

        // Temporal motion vector prediction on and off
        yield return X265("x265-tmvp-off-64x64", Moving("64x64"), "yuv420p", 6, "temporal-mvp=0:bframes=2:b-adapt=0", "hevc.temporalMvp=0");
        yield return X265("x265-tmvp-on-64x64", Moving("64x64"), "yuv420p", 6, "temporal-mvp=1:bframes=2:b-adapt=0", "hevc.temporalMvp=1");
        yield return X265("x265-tmvp-off-444-10bit-64x64", Rotating("64x64"), "yuv444p10le", 4, "temporal-mvp=0:bframes=0", "hevc.temporalMvp=0", "hevc.chroma=4:4:4");

        // Block structure of inter pictures: CTB sizes, transform hierarchy depths, rectangular and asymmetric partitions
        yield return X265("x265-inter-ctb16-80x48", Rotating("80x48"), "yuv420p", 5, "ctu=16:min-cu-size=8:bframes=1:b-adapt=0:tu-inter-depth=2", "hevc.ctb=16", "hevc.tuDepthInter=1");
        yield return X265("x265-inter-ctb32-100x76", Moving("100x76"), "yuv420p", 5, "ctu=32:min-cu-size=8:bframes=2:b-adapt=0:tu-inter-depth=3:limit-tu=0", "hevc.ctb=32", "hevc.tuDepthInter=2");
        yield return X265("x265-inter-ctb64-136x72", Rotating("136x72"), "yuv420p", 5, "ctu=64:min-cu-size=8:bframes=2:b-adapt=0:tu-inter-depth=4:rd=5", "hevc.ctb=64", "hevc.tuDepthInter=3");
        yield return X265("x265-inter-ctb64-cb32-128x128", Moving("128x128"), "yuv420p", 4, "ctu=64:min-cu-size=32:bframes=1:b-adapt=0", "hevc.ctb=64", "hevc.minCb=32");
        yield return X265("x265-inter-amp-rect-96x64", Rotating("96x64"), "yuv420p", 6, "ctu=32:rect=1:amp=1:rd=5:bframes=2:b-adapt=0:limit-modes=0", "hevc.amp");
        yield return X265("x265-inter-amp-rect-ctb64-136x72", Rotating("136x72"), "yuv420p", 5, "ctu=64:rect=1:amp=1:rd=5:bframes=1:b-adapt=0:limit-modes=0", "hevc.amp", "hevc.ctb=64");
        yield return X265("x265-inter-amp-rect-444-10bit-64x64", Rotating("64x64"), "yuv444p10le", 5, "ctu=32:rect=1:amp=1:rd=5:bframes=1:b-adapt=0:limit-modes=0", "hevc.amp", "hevc.chroma=4:4:4");
        yield return X265("x265-inter-amp-rect-422-12bit-64x64", Rotating("64x64"), "yuv422p12le", 5, "ctu=32:rect=1:amp=1:rd=5:bframes=1:b-adapt=0:limit-modes=0", "hevc.amp", "hevc.chroma=4:2:2");
        yield return X265("x265-inter-tb4-64x48", Rotating("64x48"), "yuv420p", 4, "ctu=16:max-tu-size=4:bframes=0", "hevc.maxTb=4");
        yield return X265("x265-inter-tskip-64x48", Noise(Moving("64x48"), 20, 76), "yuv420p", 4, "tskip=1:rd=5:bframes=1:b-adapt=0", "hevc.transformSkip");
        yield return X265("x265-inter-constrained-intra-64x64", Rotating("64x64"), "yuv420p", 6, "constrained-intra=1:bframes=0:rd=5", "hevc.constrainedIntraPred");
        yield return X265("x265-inter-constrained-intra-b-444-64x64", Rotating("64x64"), "yuv444p", 6, "constrained-intra=1:bframes=2:b-adapt=0:rd=5:b-intra=1", "hevc.constrainedIntraPred", "hevc.chroma=4:4:4");

        // B pictures: without and with a pyramid (B pictures used as references), weighted prediction for a fade
        yield return X265("x265-b-no-pyramid-64x64", Moving("64x64"), "yuv420p", 8, "bframes=3:b-adapt=0:b-pyramid=0", "hevc.sliceTypes=B", "hevc.outputOrder=reordered", "hevc.nal=TRAIL_N");
        yield return X265("x265-b-pyramid-64x64", Moving("64x64"), "yuv420p", 10, "bframes=4:b-adapt=0:b-pyramid=1", "hevc.referenceB", "hevc.reorder");
        yield return X265("x265-b-pyramid-ref4-444-10bit-64x64", Rotating("64x64"), "yuv444p10le", 10, "bframes=5:b-adapt=0:b-pyramid=1:ref=4", "hevc.referenceB", "hevc.chroma=4:4:4");
        yield return X265("x265-b-intra-64x64", Rotating("64x64"), "yuv420p", 8, "bframes=3:b-adapt=0:b-intra=1:rd=5", "hevc.sliceTypes=B");
        yield return X265("x265-weighted-p-64x64", Fading("64x64"), "yuv420p", 6, "bframes=0:weightp=1", "hevc.weightedPred=p", "hevc.weights=luma");
        yield return X265("x265-weighted-p-10bit-64x64", Fading("64x64"), "yuv420p10le", 6, "bframes=0:weightp=1", "hevc.weightedPred=p", "hevc.weights=luma", "hevc.bitDepth=10");
        yield return X265("x265-weighted-p-444-12bit-64x64", Fading("64x64"), "yuv444p12le", 6, "bframes=0:weightp=1", "hevc.weightedPred=p", "hevc.weights=luma", "hevc.bitDepth=12");
        yield return X265("x265-weighted-p-422-8bit-64x64", Fading("64x64"), "yuv422p", 6, "bframes=0:weightp=1", "hevc.weightedPred=p", "hevc.weights=luma", "hevc.chroma=4:2:2");
        yield return X265("x265-weighted-b-64x64", Fading("64x64"), "yuv420p", 8, "bframes=2:b-adapt=0:weightp=1:weightb=1", "hevc.weightedPred=b", "hevc.weights=luma");
        yield return X265("x265-weighted-b-400-10bit-64x64", Fading("64x64"), "gray10le", 8, "bframes=2:b-adapt=0:weightp=1:weightb=1", "hevc.weightedPred=b", "hevc.chroma=4:0:0");

        // Random access points and leading pictures
        yield return X265("x265-open-gop-cra-rasl-64x64", Moving("64x64"), "yuv420p", 14, "open-gop=1:keyint=6:min-keyint=6:bframes=3:b-adapt=0:scenecut=0", "hevc.nal=CRA", "hevc.nal=RASL_N", "hevc.repeatedParameterSets");
        yield return X265("x265-open-gop-cra-rasl-422-10bit-64x64", Moving("64x64"), "yuv422p10le", 14, "open-gop=1:keyint=6:min-keyint=6:bframes=3:b-adapt=0:scenecut=0", "hevc.nal=CRA", "hevc.nal=RASL_N", "hevc.chroma=4:2:2");
        yield return X265("x265-closed-gop-idr-radl-64x64", Moving("64x64"), "yuv420p", 14, "open-gop=0:keyint=6:min-keyint=6:bframes=3:b-adapt=0:scenecut=0:radl=2", "hevc.nal=IDR_W_RADL", "hevc.nal=RADL_N", "hevc.nal=RADL_R", "hevc.sequences=3");
        yield return X265("x265-closed-gop-idr-n-lp-64x64", Moving("64x64"), "yuv420p", 9, "open-gop=0:keyint=3:min-keyint=3:bframes=0:scenecut=0", "hevc.nal=IDR_N_LP", "hevc.sequences=3");
        yield return X265("x265-intra-refresh-96x64", Moving("96x64"), "yuv420p", 8, "intra-refresh=1:keyint=4:bframes=0", "hevc.sliceTypes=P");
        yield return X265("x265-poc-wrap-64x64", Moving("64x64"), "yuv420p", 20, "bframes=0:log2-max-poc-lsb=4:keyint=100", "hevc.pocWrap", "hevc.pocLsbBits=4");
        yield return X265("x265-poc-wrap-b-48x32", Moving("48x32"), "yuv420p", 68, "ctu=16:bframes=3:b-adapt=0:log2-max-poc-lsb=4:keyint=100:qp=38", "hevc.pocWrap", "hevc.outputOrder=reordered");
        yield return X265("x265-temporal-layers-64x64", Moving("64x64"), "yuv420p", 9, "temporal-layers=3:bframes=3:b-adapt=0:b-pyramid=1", "hevc.temporalLayers=3", "hevc.nal=TSA_N");
        yield return X265("x265-pps-updates-64x64", Moving("64x64"), "yuv420p", 12, "opt-qp-pps=1:opt-ref-list-length-pps=1:keyint=4:min-keyint=4:ref=3:bframes=0:repeat-headers=1", "hevc.repeatedParameterSets");

        // Whole groups of pictures, and larger pictures
        yield return X265("x265-inter-16-pictures-qcif-176x144", Moving("176x144"), "yuv420p", 16, "bframes=3:b-adapt=0:b-pyramid=1:ref=3:qp=32", "hevc.frames=16", "hevc.referenceB");
        yield return X265("x265-inter-cif-352x288", Rotating("352x288"), "yuv420p", 5, "ctu=64:bframes=2:b-adapt=0:qp=34:rect=1:amp=1", "hevc.ctbs=6x5", "hevc.amp");

        // The sequence the benchmarks decode: five 176x144 pictures of the Mandelbrot set as the filter zooms into it, with
        // B pictures that are output before the pictures they are predicted from
        yield return X265("x265-benchmark-inter-420-176x144", Noise("mandelbrot=size=176x144:rate=10", 6, 303), "yuv420p", 5, "bframes=2:b-adapt=0:qp=26", "hevc.sliceTypes=B", "hevc.outputOrder=reordered", "hevc.deblocking=1", "hevc.sao=luma");
        yield return X265("x265-inter-cif-10bit-352x288", Moving("352x288"), "yuv420p10le", 4, "ctu=64:bframes=1:b-adapt=0:qp=36", "hevc.ctbs=6x5", "hevc.bitDepth=10");

        // Quantization, scaling lists and filters with inter pictures
        yield return X265("x265-inter-qp0-48x48", Moving("48x48"), "yuv420p", 3, "qp=0:ipratio=1:pbratio=1:bframes=1:b-adapt=0", "hevc.sliceQp=0");
        yield return X265("x265-inter-qp51-64x64", Moving("64x64"), "yuv420p", 4, "qp=51:ipratio=1:pbratio=1:bframes=1:b-adapt=0", "hevc.sliceQp=51");
        yield return X265("x265-inter-qp51-422-10bit-64x64", Moving("64x64"), "yuv422p10le", 4, "qp=51:ipratio=1:pbratio=1:bframes=1:b-adapt=0", "hevc.sliceQp=51", "hevc.chroma=4:2:2");
        yield return X265("x265-inter-aq-cutree-136x72", Noise(Moving("136x72"), 10, 77), "yuv420p", 6, "ctu=64:aq-mode=2:qg-size=16:cutree=1:crf=26:bframes=2:b-adapt=0", "hevc.cuQpDelta", "hevc.qgSize=16");
        yield return X265("x265-inter-aq-qg8-444-10bit-64x64", Noise(Moving("64x64"), 10, 78), "yuv444p10le", 4, "ctu=16:aq-mode=3:qg-size=8:crf=26:bframes=1:b-adapt=0", "hevc.cuQpDelta", "hevc.qgSize=8");
        yield return X265("x265-inter-chroma-qp-offsets-64x64", Moving("64x64"), "yuv420p", 4, "qp=34:cbqpoffs=-8:crqpoffs=5:bframes=1:b-adapt=0", "hevc.chromaQpOffset=-8/5");
        yield return X265("x265-inter-scaling-default-64x64", Moving("64x64"), "yuv420p", 4, "scaling-list=default:bframes=1:b-adapt=0", "hevc.scalingList=default");
        yield return X265("x265-inter-scaling-sps-422-10bit-64x64", Moving("64x64"), "yuv422p10le", 4, "scaling-list={file}:bframes=1:b-adapt=0", "hevc.scalingList=sps", "hevc.chroma=4:2:2")
            with { File = ScalingLists(4, copies: false) };
        yield return X265("x265-inter-no-filters-64x64", Moving("64x64"), "yuv420p", 5, "no-deblock=1:sao=0:bframes=1:b-adapt=0:qp=36", "hevc.deblocking=0", "hevc.sao=off");
        yield return X265("x265-inter-deblock-offsets-64x64", Moving("64x64"), "yuv420p", 5, "deblock=2,-2:sao=0:bframes=1:b-adapt=0:qp=38", "hevc.deblocking.offsets=-2/2");
        yield return X265("x265-inter-sao-12bit-64x64", Noise(Moving("64x64"), 12, 79), "yuv420p12le", 5, "sao=1:bframes=1:b-adapt=0:qp=40", "hevc.sao=luma", "hevc.bitDepth=12");
        yield return X265("x265-inter-selective-sao-64x64", Noise(Moving("64x64"), 12, 80), "yuv420p", 7, "selective-sao=2:bframes=2:b-adapt=0:qp=38", "hevc.sao=sliceOff", "hevc.sao=luma");
        yield return X265("x265-inter-signhide-off-64x64", Moving("64x64"), "yuv420p", 4, "signhide=0:bframes=1:b-adapt=0", "hevc.signDataHiding=0");

        // Lossless with inter pictures
        yield return X265("x265-lossless-inter-420-8bit-64x48", Noise(Moving("64x48"), 6, 81), "yuv420p", 4, "lossless=1:ctu=32:bframes=1:b-adapt=0", "hevc.lossless");
        yield return X265("x265-lossless-inter-444-10bit-64x48", Noise(Moving("64x48"), 6, 82), "yuv444p10le", 3, "lossless=1:ctu=32:bframes=0", "hevc.lossless", "hevc.chroma=4:4:4");
        yield return X265("x265-lossless-inter-422-12bit-64x48", Noise(Moving("64x48"), 6, 83), "yuv422p12le", 3, "lossless=1:ctu=32:bframes=0", "hevc.transquantBypass", "hevc.chroma=4:2:2");
        yield return X265("x265-lossless-inter-gbr-8bit-64x48", Noise(Moving("64x48"), 6, 84), "gbrp", 3, "lossless=1:ctu=32:bframes=1:b-adapt=0", "hevc.lossless", "hevc.matrix=identity");
        yield return X265("x265-cu-lossless-inter-64x64", Noise(Moving("64x64"), 6, 85), "yuv420p", 4, "cu-lossless=1:qp=32:ctu=32:bframes=1:b-adapt=0", "hevc.transquantBypass");

        // Wavefronts and slices with inter pictures
        yield return X265("x265-inter-wpp-ctb16-64x64", Moving("64x64"), "yuv420p", 5, "wpp=1:ctu=16:bframes=1:b-adapt=0", "hevc.wpp");
        yield return X265("x265-inter-wpp-ctb32-422-10bit-96x96", Rotating("96x96"), "yuv422p10le", 4, "wpp=1:ctu=32:bframes=1:b-adapt=0", "hevc.wpp", "hevc.chroma=4:2:2");
        yield return X265("x265-inter-slices-4-64x128", Moving("64x128"), "yuv420p", 5, "slices=4:wpp=1:ctu=16:bframes=1:b-adapt=0", "hevc.slices=4", "hevc.loopFilterAcrossSlices=0").Without(DecoderLibde265, Libde265SaoAcrossSlices);
        yield return X265("x265-inter-slices-2-wpp-444-64x96", Moving("64x96"), "yuv444p", 4, "slices=2:wpp=1:ctu=16:bframes=1:b-adapt=0", "hevc.slices=2", "hevc.wpp");
        yield return X265("x265-inter-aud-hrd-64x64", Moving("64x64"), "yuv420p", 6, "aud=1:hrd=1:vbv-bufsize=1000:vbv-maxrate=1000:bframes=2:b-adapt=0", "hevc.nal=AUD", "hevc.hrd");
    }

    private static HevcFile ScalingLists(int seed, bool copies, bool defaults = false) => new("scaling-list.txt",
        Inv($"Scaling lists in the format of HM, {(copies ? "the chroma lists equal to the luma list of their size and prediction type" : "each one with its own coefficients")}{(defaults ? ", the 4x4 lists equal to the default ones" : "")} (HevcCorpus.ScalingListFile, seed {seed})."),
        ScalingListFile(seed, copies, defaults));
}
