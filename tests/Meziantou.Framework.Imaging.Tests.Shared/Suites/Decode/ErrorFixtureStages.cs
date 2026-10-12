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
        "invalid/jxl/codestream-signature",
        "invalid/jxl/container-box-size",
        "invalid/jxl/container-parts-out-of-order",
        "invalid/jxl/container-without-codestream",
        "invalid/jxl/float-samples",
        "invalid/jxl/pq-white-point-e",
        "invalid/jxl/premultiplied-alpha",
        "invalid/jxl/xyb-icc",
        "limit/jxl/width-over-limit",
        "limit/jxl/height-over-limit",
        "limit/jxl/frame-pixels-over-limit",
        "limit/jxl/metadata-bytes-over-limit",
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
        "invalid/jpeg/lossless",
        "invalid/jpeg/non-integral-sampling",
        "invalid/jpeg/precision-16",
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

        // BMP: the file header, the Windows or OS/2 DIB header (its compression must agree with its bit count and row
        // order) and the masks that follow a 40-byte BI_BITFIELDS header, all read before the palette and the pixel data
        "invalid/bmp/bit-count-2",
        "invalid/bmp/bit-count-7",
        "invalid/bmp/embedded-png",
        "invalid/bmp/mask-10-bits",
        "invalid/bmp/non-contiguous-mask",
        "invalid/bmp/os2-core-bit-count-16",
        "invalid/bmp/os2-core-offset-inside-palette",
        "invalid/bmp/os2-huffman-1d",
        "invalid/bmp/overlapping-masks",
        "invalid/bmp/pixel-offset-too-small",
        "invalid/bmp/planes-2",
        "invalid/bmp/rle8-bit-count-4",
        "invalid/bmp/rle8-top-down",
        "invalid/bmp/truncated-header",
        "invalid/bmp/undefined-compression",
        "invalid/bmp/zero-green-mask",
        "invalid/bmp/zero-height",
        "invalid/bmp/zero-width",
        "limit/bmp/frame-pixels-over-limit",
        "limit/bmp/width-over-limit",

        // TGA: the 18-byte header (the only part a header identification reads)
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

        // AVIF: the file type box and the box that describes the image (the meta box: primary item, item properties,
        // references; or the moov box of an image sequence: tracks, edit lists, sample tables), which is parsed before any
        // item data or sample is read. The colr box is one of the item properties: matrix coefficients that are not
        // supported are known there
        "invalid/avif/a1op-on-grid",
        "invalid/avif/alpha-different-size",
        "invalid/avif/box-size-too-small",
        "invalid/avif/child-box-exceeds-parent",
        "invalid/avif/clap-after-irot",
        "invalid/avif/clap-fractional",
        "invalid/avif/derived-iden",
        "invalid/avif/derived-iovl",
        "invalid/avif/essential-unknown-property",
        "invalid/avif/ftyp-without-avif-brand",
        "invalid/avif/grid-tile-transform-differs",
        "invalid/avif/grid-tiles-differ",
        "invalid/avif/lsel-on-grid",
        "invalid/avif/lsel-out-of-range",
        "invalid/avif/matrix-ictcp",
        "invalid/avif/missing-av1c",
        "invalid/avif/missing-ftyp",
        "invalid/avif/missing-ispe",
        "invalid/avif/missing-pitm",
        "invalid/avif/missing-primary-item",
        "invalid/avif/premultiplied-alpha",
        "invalid/avif/sequence-edit-ends-before-last-image",
        "invalid/avif/sequence-edit-list-offset",
        "invalid/avif/sequence-fragmented",
        "invalid/avif/sequence-matrix-ictcp",
        "invalid/avif/sequence-missing-chunk-offsets",
        "invalid/avif/sequence-no-av1-track",
        "invalid/avif/truncated-meta",
        "limit/avif/frame-pixels-over-limit",
        "limit/avif/height-over-limit",
        "limit/avif/icc-over-metadata-limit",
        "limit/avif/width-over-limit",
    };

    private static readonly HashSet<string> StructureDefects = new(StringComparer.Ordinal)
    {
        "invalid/jxl/container-two-codestreams",
        "invalid/jxl/frame-header-corrupt",
        "limit/jxl/frames-over-limit",
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

        // BMP: the palette, the gap and the padded rows or the commands of the run-length stream, all walked by a full scan
        "invalid/bmp/rle4-delta-outside-canvas",
        "invalid/bmp/rle8-data-after-last-row",
        "invalid/bmp/rle8-missing-end-of-bitmap",
        "invalid/bmp/rle8-run-past-row-end",
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

        // AVIF: the boxes that follow the one that describes the image, the location of the item data and of the samples,
        // the grid descriptor (item data), the Exif and XMP items, and the sequence header of each coded item, which a
        // full scan compares with the av1C record
        "invalid/avif/a1op-beyond-operating-points",
        "invalid/avif/a1op-missing-operating-point",
        "invalid/avif/av1c-mismatch",
        "invalid/avif/grid-size-mismatch",
        "invalid/avif/grid-tile-count-mismatch",
        "invalid/avif/item-outside-file",
        "invalid/avif/item-outside-idat",
        // The Exif and XMP items of an image sequence are data of the file: a header identification does not read them,
        // nor the meta box of the track that locates them
        "invalid/avif/sequence-file-metadata-outside-file",
        "invalid/avif/sequence-track-meta-malformed",
        "invalid/avif/sequence-track-metadata-outside-file",
        "invalid/avif/sequence-truncated",
        "invalid/avif/truncated-item-data",
        "invalid/avif/two-meta-boxes",
        "limit/avif/encoded-bytes-over-limit",
        "limit/avif/exif-over-metadata-limit",
        "limit/avif/sequence-frames-over-limit",
        "limit/avif/sequence-xmp-over-metadata-limit",
    };

    private static readonly HashSet<string> PixelDefects = new(StringComparer.Ordinal)
    {
        "invalid/jxl/frame-data-corrupt",
        "limit/jxl/total-pixels-hidden-layer",
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
        // Four components are inks: the file is identified, and only a load without color conversion rejects it
        "invalid/jpeg/cmyk-adobe-progressive",
        "invalid/jpeg/cmyk-without-adobe-restart",
        "invalid/jpeg/cmyk-ycck-embedded-profile",
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
        "invalid/bmp/rle8-palette-index-out-of-range",
        "invalid/tga/colormap-index-out-of-range",
        "invalid/tga/colormap16-index-out-of-range",
        "limit/pnm/total-pixels-over-limit",

        // AVIF: the frame headers and the tile data of the AV1 bitstream are only interpreted by the decoder (the size of a
        // frame is in its frame header, and its spatial layer in the header of its unit); displayed and hidden frames, and
        // the lower layers of a layered image, are charged when they are decoded
        "invalid/avif/ispe-mismatch",
        "invalid/avif/lsel-beyond-layers",
        "invalid/avif/lsel-layer-size-mismatch",
        "invalid/avif/lsel-missing-layer",
        "invalid/avif/lsel-outside-operating-point",
        "invalid/avif/sequence-size-changes",
        "limit/avif/layered-lower-layers-total-pixels",
        "limit/avif/live-allocation-over-limit",
        "limit/avif/sequence-hidden-frames-total-pixels",
        "limit/avif/sequence-total-pixels-over-limit",
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
