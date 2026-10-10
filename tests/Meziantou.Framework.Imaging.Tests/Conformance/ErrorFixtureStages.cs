using Meziantou.Framework.Imaging.TestHarness.Golden;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// Where the defect of each error fixture lies: before the first pixel payload (detected by a header identification and
/// by every load), in the container structure after it (detected by a full scan and by loads), or inside the compressed
/// pixel data of a valid container (detected only by pixel decoding: a full scan succeeds). Every error fixture must be
/// classified here, so that adding one forces an explicit decision.
/// </summary>
internal static class ErrorFixtureStages
{
    private static readonly HashSet<string> HeaderDefects = new(StringComparer.Ordinal)
    {
        "invalid/png/apng-default-fctl-partial",
        "invalid/png/apng-fdat-before-idat",
        "invalid/png/apng-invalid-dispose-op",
        "invalid/png/apng-num-plays-overflow",
        "invalid/png/apng-zero-frames",
        "invalid/png/bad-ihdr-crc",
        "invalid/png/chunk-length-overflow",
        "invalid/png/ihdr-not-first",
        "invalid/png/illegal-bit-depth",
        "invalid/png/unknown-critical-chunk",
        "invalid/gif/plain-text-extension",
        "invalid/gif/empty-logical-screen",
        "invalid/gif/graphic-control-wrong-size",
        "invalid/gif/no-image",
        "invalid/jpeg/arithmetic-coding",
        "invalid/jpeg/cmyk-four-components",
        "invalid/jpeg/lossless",
        "invalid/jpeg/non-integral-sampling",
        "invalid/jpeg/precision-12",
        "limit/jpeg/frame-pixels-excessive",
        "limit/png/frame-pixels-over-limit",
        "limit/png/width-over-limit",
        "limit/png/ztxt-over-metadata-limit",

        // WebP: the RIFF chunks up to the first image chunk (VP8X, ICCP, ANIM, the first ANMF header) and the bitstream
        // header (VP8L signature/version/size, VP8 frame tag, start code and first partition size)
        "invalid/webp/anim-missing-anim-chunk",
        "invalid/webp/chunk-size-exceeds-riff",
        "invalid/webp/extended-canvas-mismatch",
        "invalid/webp/extended-missing-image",
        "invalid/webp/lossless-bad-signature",
        "invalid/webp/lossless-bad-version",
        "invalid/webp/lossy-bad-start-code",
        "invalid/webp/lossy-first-partition-overflow",
        "invalid/webp/lossy-interframe",
        "limit/webp/lossless-metadata-over-limit",
        "limit/webp/lossless-width-over-limit",
        "limit/webp/lossy-frame-pixels-over-limit",
        // QOI: the 14-byte header (magic, dimensions, channels, colorspace)
        "invalid/qoi/channels-5",
        "invalid/qoi/colorspace-2",
        "invalid/qoi/truncated-header",
        "invalid/qoi/zero-height",
        "invalid/qoi/zero-width",
        "limit/qoi/frame-pixels-over-limit",
        "limit/qoi/width-beyond-int32",
        "limit/qoi/width-over-limit",

        // BMP: the file header, the DIB header and the masks that follow a 40-byte BI_BITFIELDS header, all read before
        // the palette and the pixel data
        "invalid/bmp/bit-count-2",
        "invalid/bmp/bit-count-7",
        "invalid/bmp/core-header",
        "invalid/bmp/embedded-png",
        "invalid/bmp/mask-10-bits",
        "invalid/bmp/non-contiguous-mask",
        "invalid/bmp/os2-v2-header",
        "invalid/bmp/overlapping-masks",
        "invalid/bmp/pixel-offset-too-small",
        "invalid/bmp/planes-2",
        "invalid/bmp/rle8",
        "invalid/bmp/truncated-header",
        "invalid/bmp/undefined-compression",
        "invalid/bmp/zero-green-mask",
        "invalid/bmp/zero-height",
        "invalid/bmp/zero-width",
        "limit/bmp/frame-pixels-over-limit",
        "limit/bmp/width-over-limit",

        // TGA: the 18-byte header (the only part a header identification reads)
        "invalid/tga/colormap-16bit-indexes",
        "invalid/tga/grayscale-16bit",
        "limit/tga/width-over-limit",

        // PNM: the magic number and the header tokens or PAM header lines up to ENDHDR
        "invalid/pnm/comment-in-token",
        "invalid/pnm/header-too-long",
        "invalid/pnm/maxval-65536",
        "invalid/pnm/maxval-zero",
        "invalid/pnm/pam-depth-mismatch",
        "invalid/pnm/pam-duplicate-width",
        "invalid/pnm/pam-missing-endhdr",
        "invalid/pnm/pam-tuple-cmyk",
        "invalid/pnm/pam-unknown-keyword",
        "invalid/pnm/zero-width",
        "limit/pnm/width-over-limit",
    };

    private static readonly HashSet<string> StructureDefects = new(StringComparer.Ordinal)
    {
        "invalid/png/apng-fctl-without-fdat",
        "invalid/png/apng-fdat-without-sequence",
        "invalid/png/apng-frame-count-excess",
        "invalid/png/apng-frame-count-short",
        "invalid/png/apng-invalid-blend-op",
        "invalid/png/apng-region-out-of-bounds",
        "invalid/png/apng-sequence-gap",
        "limit/png/apng-frames-over-limit",
        "invalid/png/bad-idat-crc",
        "invalid/png/missing-iend",
        "invalid/png/non-consecutive-idat",
        "invalid/png/plte-after-idat",
        "invalid/png/truncated-idat",
        "invalid/gif/truncated-image-data",
        "invalid/gif/unknown-block",
        "invalid/gif/lzw-minimum-code-size-12",
        "invalid/gif/missing-trailer",
        "invalid/jpeg/interleaved-scan-too-many-blocks",

        // Progressive scan parameters and coefficient progression are validated from the scan headers by the walker
        "invalid/jpeg/progressive-invalid-spectral-range",
        "invalid/jpeg/progressive-refinement-before-first-scan",
        "limit/gif/frames-over-limit",
        "limit/png/encoded-bytes-over-limit",

        // WebP: chunks after the first image chunk (later ANMF rectangles) and the RIFF/chunk sizes against the input length
        "invalid/webp/anim-frame-outside-canvas",
        "invalid/webp/anim-truncated",
        "invalid/webp/lossless-truncated",
        "invalid/webp/riff-size-exceeds-file",
        "limit/webp/anim-frames-over-limit",
        // QOI: the chunk stream is the structure (a full scan walks every chunk to find the end marker)
        "invalid/qoi/bad-end-marker",
        "invalid/qoi/chunk-after-last-pixel",
        "invalid/qoi/end-marker-truncated",
        "invalid/qoi/missing-end-marker",
        "invalid/qoi/run-past-end",
        "invalid/qoi/truncated-chunks",
        "invalid/qoi/truncated-in-chunk",
        "limit/qoi/encoded-bytes-over-limit",

        // BMP: the palette, the gap and the padded rows, all consumed by a full scan
        "invalid/bmp/truncated-pixels",

        // TGA: the packets and the TGA 2.0 trailer, which a full scan walks to the end of the input
        "invalid/tga/packet-past-end",
        "invalid/tga/truncated-packet",
        "invalid/tga/truncated-pixels",
        "invalid/tga/extension-premultiplied-alpha",
        "invalid/tga/extension-offset-past-footer",
        "invalid/tga/extension-size-too-small",
        "limit/tga/encoded-bytes-over-limit",

        // PNM: the raster; the plain reader parses every sample (and validates it against MAXVAL) to find the end of the image
        "invalid/pnm/plain-pbm-digit-2",
        "invalid/pnm/sample-above-maxval",
        "invalid/pnm/truncated-raster",
    };

    private static readonly HashSet<string> PixelDefects = new(StringComparer.Ordinal)
    {
        "invalid/png/apng-fdat-extra-scanline",

        // GIF LZW datastreams and color tables are only interpreted by the decoder; full scans charge frames, not pixels
        "invalid/gif/lzw-code-out-of-range",
        "invalid/gif/lzw-end-code-too-early",
        "invalid/gif/lzw-first-code-not-literal",
        "invalid/gif/lzw-string-exceeds-image",
        "invalid/gif/lzw-too-many-pixels",
        "invalid/gif/lzw-truncated-without-end-code",
        "invalid/gif/missing-color-table",
        "invalid/gif/palette-index-out-of-range",
        "limit/gif/total-pixels-over-limit",
        "invalid/png/apng-fdat-truncated-zlib",

        // Not a pixel-data defect, but likewise only found by decoding: full scans charge frames, never displayed pixels
        "limit/png/apng-total-pixels-over-limit",
        "invalid/png/bad-adler32",
        "invalid/png/bad-filter-type",
        "invalid/png/extra-bytes-after-zlib",
        "invalid/png/extra-image-data",
        "invalid/png/missing-adler32",
        "invalid/png/palette-index-out-of-range",
        "invalid/png/too-little-image-data",
        "invalid/png/truncated-zlib",
        "invalid/jpeg/invalid-huffman-code",
        "invalid/jpeg/restart-marker-missing",
        "invalid/jpeg/restart-out-of-sequence",
        "invalid/jpeg/truncated-entropy-data",
        "invalid/jpeg/progressive-refinement-invalid-code",
        "invalid/jpeg/progressive-truncated-scan",

        // JPEG table contents and scan coverage are only parsed by the decoder (identification walks the segments without
        // interpreting DQT/DHT payloads or checking that every component is coded)
        "invalid/jpeg/missing-component-scan",
        "invalid/jpeg/progressive-missing-component",
        "invalid/jpeg/oversubscribed-huffman-table",
        "invalid/jpeg/truncated-quantization-table",
        "invalid/jpeg/undefined-huffman-table",
        "invalid/jpeg/zero-quantization-value",

        // WebP: VP8L entropy-coded data, VP8 token partitions and ALPH payloads are only interpreted by the decoders; displayed
        // animation pixels are charged when composed
        "invalid/webp/lossless-corrupt-entropy-code",
        "invalid/webp/lossy-alpha-bad-compression",
        "invalid/webp/lossy-truncated-partition",
        "limit/webp/anim-total-pixels-over-limit",

        // BMP and TGA palettes are only looked up by the decoder; a full scan charges frames, never displayed pixels
        "invalid/bmp/palette-index-out-of-range",
        "invalid/tga/colormap-index-out-of-range",
        "limit/pnm/total-pixels-over-limit",
    };

    /// <summary>Gets a value indicating whether a header identification detects the defect.</summary>
    public static bool IsHeaderDefect(GoldenFixture fixture) => GetStage(fixture) == Stage.Header;

    /// <summary>Gets a value indicating whether only pixel decoding detects the defect (identification, even a full scan, succeeds).</summary>
    public static bool IsPixelDefect(GoldenFixture fixture) => GetStage(fixture) == Stage.Pixels;

    private static Stage GetStage(GoldenFixture fixture)
    {
        if (HeaderDefects.Contains(fixture.Id))
            return Stage.Header;

        if (StructureDefects.Contains(fixture.Id))
            return Stage.Structure;

        if (PixelDefects.Contains(fixture.Id))
            return Stage.Pixels;

        throw new InvalidOperationException($"The error fixture '{fixture.Id}' is not classified in {nameof(ErrorFixtureStages)}: add it to the header, structure or pixel defects.");
    }

    private enum Stage
    {
        Header,
        Structure,
        Pixels,
    }
}
