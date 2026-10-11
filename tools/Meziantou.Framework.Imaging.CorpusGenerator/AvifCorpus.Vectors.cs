using System.Globalization;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

// The recipes of the AV1 conformance vectors: a deterministic lavfi source, a pixel format and encoder options. Requires
// lists the feature tags a vector exists for; they are read back from the encoded headers (AvifCorpus.Trace.cs), so a
// recipe whose encoder stops using the tool fails the generation instead of silently losing coverage.
//
// Traps (each one cost a vector during the selection):
// - libaom "-usage allintra" disables CDEF unless "-enable-cdef 1" is given;
// - libaom turns intra block copy on for screen-like content (testsrc, testsrc2) unless "enable-intrabc=0" is given;
// - SVT-AV1 only encodes 4:2:0, and its super-resolution of a frame whose size is not a multiple of 8 codes frames larger
//   than the sequence maximum, which libaom rejects: the super-resolution sources are multiples of 8;
// - both encoders pick 256-sample loop restoration units for small frames, and the restoration type is only switchable
//   within a plane that has several units: the two switchable vectors are 384x216;
// - libaom never codes delta_lf_multi = 1, and neither encoder segments on anything but the quantizer;
// - SVT-AV1 writes an empty stream (exit code 0) for a multi-frame encode with "lp=1" in this build, and without "lp" its
//   output is not always the same: the stream with a hidden switch frame ("sframe-mode=2") came out in two forms, one
//   run out of three. The inter recipes pass "lp=2", the lowest level of parallelism that writes a stream: every recipe
//   then gives the same bytes run after run (the bytes the other recipes gave without "lp"), which the check mode proves;
// - SVT-AV1 reference scaling ("resize-mode") has the same multiple-of-8 constraint as its super-resolution, and with
//   "sframe-dist" it codes a 16384x8704 sequence maximum with an explicit size in every frame header;
// - libaom only signals allow_intrabc for an all-intra encode of screen content that is large enough (176x144 "testsrc");
// - libaom rejects "superres-mode", "resize-mode", "sframe-dist", "enable-fwd-kf" and "delta-lf-mode=2" as -aom-params in
//   this build, never sets enable_dual_filter for an encode that has inter frames, never codes a bilinear, a smooth or a
//   dual interpolation filter for a frame, and, with cyclic refresh, signals segmentation_temporal_update = 1 without
//   ever coding seg_id_predicted = 1; SVT-AV1 does none of it either. These tools come from the vectors that an encoder
//   did not write alone: the frame-level bilinear and smooth filters from header patches (Patched), the filter per
//   direction, the predicted segment ids and the reference, skip and global motion features of a segment from
//   hand-authored inter frames (HandAuthored, Av1HandAuthoredStream.cs);
// - what the earlier size limits hid: loop restoration is only switchable in the U plane, and its units are only 64
//   samples wide, on a 512x256 frame whose halves differ (libaom, speed 1); SVT-AV1 offsets the luma DC quantizer
//   ("luma-y-dc-qindex-offset"), libaom never; the libaom film grain test vector 9 codes update_grain = 0 on inter frames.
internal static partial class AvifCorpus
{
    /// <param name="Name">The file name without extension; the vector id is <c>av1/Name</c>.</param>
    /// <param name="Source">The lavfi source graph.</param>
    /// <param name="PixelFormat">The FFmpeg pixel format given to the encoder, which is the native format of the stream.</param>
    /// <param name="Encoder">The encoder and its options.</param>
    /// <param name="Frames">The number of source frames, which is the number of frames a decoder outputs.</param>
    private sealed record Av1Vector(string Name, string Source, string PixelFormat, string[] Encoder, int Frames = 1)
    {
        /// <summary>The feature tags the encoded stream must signal: a tag, or one of the values of a <c>key=value</c> tag.</summary>
        public string[] Requires { get; init; } = [];

        /// <summary>
        /// <see langword="false"/> (intra vector): every frame must be a shown key frame. <see langword="true"/> (inter
        /// vector): the stream must have a frame that is not one (inter, intra-only, switch, hidden or shown again).
        /// </summary>
        public bool Inter { get; init; }

        /// <summary>
        /// The value written over the 2-bit <c>interpolation_filter</c> element of every frame header that codes
        /// <c>is_filter_switchable = 0</c> (1 is SMOOTH, 3 BILINEAR), or <see langword="null"/>: see <see cref="Patched"/>.
        /// </summary>
        public int? InterpolationFilterPatch { get; init; }

        /// <summary>The name of the vector whose encoded stream is patched, when <see cref="InterpolationFilterPatch"/> is set.</summary>
        public string? PatchOf { get; init; }

        /// <summary>
        /// The frames appended to the encoded stream, which is then one key frame, or <see langword="null"/>: see
        /// <see cref="HandAuthored"/>.
        /// </summary>
        public Av1HandAuthoredStream.Recipe? HandAuthoredFrames { get; init; }

        /// <summary>The number of frames the encoder is given: one key frame when frames are appended to its output.</summary>
        public int EncodedFrames => HandAuthoredFrames is null ? Frames : 1;
    }

    /// <summary>
    /// A hand-authored vector, for the coding tools that are coded block by block and that neither encoder produces (an
    /// interpolation filter per direction, a predicted segment id, the reference frame, skip and global motion features of
    /// a segment): one key frame encoded by libaom (64x64 superblocks, a full sequence header, a size that is a multiple of
    /// 64) followed by inter frames written bit by bit from the specification (<see cref="Av1HandAuthoredStream"/>). The
    /// headers, the tags and the digests are those of the final stream; like any vector, it is only recorded with the
    /// digests both decoders agree on. It is tagged <c>av1.hand_authored</c>, and its provenance names the recipe of the
    /// frames and the hash of the key frame stream they are appended to.
    /// </summary>
    private static Av1Vector HandAuthored(Av1HandAuthoredStream.Recipe recipe, string format, string pixelFormat, string size, params string[] requires) =>
        new($"hand-{recipe.Name}-{format}-{size}", Noise(TestSrc2(size), 10, 7), pixelFormat, AomGood(4, 20, "sb-size=64"), Frames: 1 + recipe.Frames.Length)
        {
            Inter = true,
            HandAuthoredFrames = recipe,
            Requires = ["av1.hand_authored", "av1.frame_types=inter", "av1.disable_cdf_update", .. requires],
        };

    /// <summary>
    /// A vector made by header patching, for a frame-level interpolation filter that neither encoder selects: the stream of
    /// another recipe, whose inter frame headers code <c>is_filter_switchable = 0</c> and a 2-bit
    /// <c>interpolation_filter</c>, with these 2 bits overwritten in every such header. No length changes and no block
    /// codes a filter (the filter is not switchable), so the result is a valid stream that decodes to other pictures;
    /// like any vector, it is only recorded with the digests both decoders agree on. It is tagged
    /// <c>av1.patched_header</c>, and its provenance names the source vector and the hash of the stream before the patch.
    /// </summary>
    private static Av1Vector Patched(IReadOnlyList<Av1Vector> vectors, string source, string name, int filter, params string[] requires)
    {
        var recipe = vectors.Single(vector => vector.Name == source);
        return recipe with { Name = name, PatchOf = source, InterpolationFilterPatch = filter, Requires = ["av1.patched_header", "av1.interpolation_filter=" + (filter == 3 ? "bilinear" : "smooth"), .. requires] };
    }

    // Sources
    private static string TestSrc2(string size) => "testsrc2=size=" + size;

    private static string Mandelbrot(string size) => "mandelbrot=size=" + size;

    private static string TestSrc(string size) => "testsrc=size=" + size;

    private static string Bars(string size) => "smptebars=size=" + size;

    /// <summary>The "testsrc2" pattern at 10 frames per second: its counters and its moving bars change in every frame.</summary>
    private static string Moving(string size) => Inv($"testsrc2=size={size}:rate=10");

    /// <summary>The "testsrc2" pattern rotating around its center, one radian every <paramref name="secondsPerRadian"/>
    /// seconds at 10 frames per second: a motion that a translation does not describe (warped and global motion).</summary>
    private static string Rotating(string size, int secondsPerRadian = 3)
    {
        var dimensions = size.Split('x');
        return Inv($"testsrc2=size={size}:rate=10,rotate=t/{secondsPerRadian}:ow={dimensions[0]}:oh={dimensions[1]}");
    }

    /// <summary>Two sources side by side: content whose halves call for different loop restoration filters.</summary>
    private static string SideBySide(string left, string right) => $"{left}[a];{right}[b];[a][b]hstack";

    /// <summary>Adds seeded noise: detail that keeps the encoders from coding flat blocks only.</summary>
    private static string Noise(string source, int strength, int seed) => Inv($"{source},noise=alls={strength}:allf=t:all_seed={seed}");

    // Encoders. Single-threaded: "-threads 1" is added to every command, "-row-mt 0" to libaom, "lp=1" to SVT-AV1 when it
    // only codes key frames and "lp=2" otherwise (see the traps).
    private static string[] Aom(string usage, bool still, int cpuUsed, int crf, string? parameters, string[] extra) =>
    [
        "-c:v", "libaom-av1", .. (still ? ["-still-picture", "1"] : Array.Empty<string>()), "-usage", usage,
        "-cpu-used", cpuUsed.ToString(CultureInfo.InvariantCulture), "-crf", crf.ToString(CultureInfo.InvariantCulture),
        .. extra, .. (parameters is null ? [] : new[] { "-aom-params", parameters }), "-row-mt", "0",
    ];

    /// <summary>libaom, all-intra usage, one still picture: a reduced still picture header.</summary>
    private static string[] AomStill(int cpuUsed, int crf, string? parameters = null, params string[] extra) => Aom("allintra", still: true, cpuUsed, crf, parameters, extra);

    /// <summary>libaom, all-intra usage without the still picture flag: a full sequence header and full frame headers.</summary>
    private static string[] AomIntra(int cpuUsed, int crf, string? parameters = null, params string[] extra) => Aom("allintra", still: false, cpuUsed, crf, parameters, extra);

    /// <summary>libaom, good-quality usage: CDEF and loop restoration are searched by default.</summary>
    private static string[] AomGood(int cpuUsed, int crf, string? parameters = null, params string[] extra) => Aom("good", still: false, cpuUsed, crf, parameters, extra);

    private static string[] AomLossless(int cpuUsed, string? parameters = null) => AomStill(cpuUsed, 0, "lossless=1:enable-intrabc=0" + (parameters is null ? "" : ":" + parameters));

    private static string[] Svt(int preset, int crf, string? parameters = null, params string[] extra) =>
    [
        "-c:v", "libsvtav1", "-preset", preset.ToString(CultureInfo.InvariantCulture), "-crf", crf.ToString(CultureInfo.InvariantCulture),
        .. extra, "-svtav1-params", "lp=1" + (parameters is null ? "" : ":" + parameters),
    ];

    /// <summary>libaom, good-quality usage, low delay: no lookahead, so no alternate reference frame and no hidden frame.</summary>
    private static string[] AomLowDelay(int cpuUsed, int crf, string? parameters = null, params string[] extra) => Aom("good", still: false, cpuUsed, crf, parameters, ["-lag-in-frames", "0", .. extra]);

    /// <summary>libaom, real-time usage (always low delay), constant quality.</summary>
    private static string[] AomRealtime(int cpuUsed, int crf, string? parameters = null, params string[] extra) => Aom("realtime", still: false, cpuUsed, crf, parameters, ["-lag-in-frames", "0", .. extra]);

    /// <summary>libaom, real-time usage at a target bitrate: the rate control cyclic refresh ("-aq-mode 3") needs.</summary>
    private static string[] AomRealtimeBitrate(int cpuUsed, string bitrate, params string[] extra) =>
        ["-c:v", "libaom-av1", "-usage", "realtime", "-cpu-used", cpuUsed.ToString(CultureInfo.InvariantCulture), "-b:v", bitrate, "-lag-in-frames", "0", .. extra, "-row-mt", "0"];

    /// <summary>SVT-AV1 with inter frames: random access (hidden frames shown later) unless "pred-struct=1" is given.
    /// "lp=2": see the traps.</summary>
    private static string[] SvtInter(int preset, int crf, string? parameters = null, params string[] extra) =>
    [
        "-c:v", "libsvtav1", "-preset", preset.ToString(CultureInfo.InvariantCulture), "-crf", crf.ToString(CultureInfo.InvariantCulture),
        .. extra, "-svtav1-params", "lp=2" + (parameters is null ? "" : ":" + parameters),
    ];

    private static List<Av1Vector> Vectors()
    {
        const string Lossless = "av1.lossless";
        const string Identity = "av1.matrix=identity";
        const string Reduced = "av1.reduced_still_picture_header";
        var grain = Noise(TestSrc2("66x50"), 10, 5);
        var restorationHalves = SideBySide(Noise(Bars("256x256"), 40, 3), TestSrc2("256x256") + ",gblur=sigma=2");

        return
        [
            // ---------------------------------------------------------------------------------------------------------
            // Lossless (Walsh-Hadamard transform, no filter): every subsampling and bit depth; identity matrix (planar GBR)
            // ---------------------------------------------------------------------------------------------------------
            new("lossless-gbr-8bit-34x36", TestSrc2("34x36"), "gbrp", AomLossless(6)) { Requires = [Lossless, Identity, "av1.bitDepth=8", "av1.subsampling=4:4:4", Reduced] },
            new("lossless-gbr-10bit-18x10", TestSrc2("18x10"), "gbrp10le", AomLossless(6)) { Requires = [Lossless, Identity, "av1.bitDepth=10"] },
            new("lossless-gbr-12bit-16x16", Mandelbrot("16x16"), "gbrp12le", AomLossless(6)) { Requires = [Lossless, Identity, "av1.bitDepth=12"] },
            new("lossless-444-8bit-18x10", Mandelbrot("18x10"), "yuv444p", AomLossless(6)) { Requires = [Lossless, "av1.bitDepth=8", "av1.subsampling=4:4:4"] },
            new("lossless-420-8bit-66x68", TestSrc2("66x68"), "yuv420p", AomLossless(6)) { Requires = [Lossless, "av1.bitDepth=8", "av1.subsampling=4:2:0"] },
            new("lossless-420-10bit-34x36", Mandelbrot("34x36"), "yuv420p10le", AomLossless(4)) { Requires = [Lossless, "av1.bitDepth=10", "av1.subsampling=4:2:0"] },
            new("lossless-420-12bit-34x36", TestSrc2("34x36"), "yuv420p12le", AomLossless(6)) { Requires = [Lossless, "av1.bitDepth=12", "av1.subsampling=4:2:0"] },
            new("lossless-422-8bit-34x36", TestSrc2("34x36"), "yuv422p", AomLossless(6)) { Requires = [Lossless, "av1.bitDepth=8", "av1.subsampling=4:2:2"] },
            new("lossless-400-8bit-64x64", Mandelbrot("64x64"), "gray", AomLossless(6)) { Requires = [Lossless, "av1.bitDepth=8", "av1.subsampling=4:0:0"] },
            new("lossless-400-10bit-18x10", TestSrc2("18x10"), "gray10le", AomLossless(6)) { Requires = [Lossless, "av1.bitDepth=10", "av1.subsampling=4:0:0"] },
            new("lossless-400-12bit-34x36", TestSrc2("34x36"), "gray12le", AomLossless(6)) { Requires = [Lossless, "av1.bitDepth=12", "av1.subsampling=4:0:0"] },
            new("lossless-screen-444-8bit-128x96", TestSrc("128x96"), "yuv444p", AomLossless(4, "tune-content=screen")) { Requires = [Lossless, "av1.allow_screen_content_tools"] },

            // ---------------------------------------------------------------------------------------------------------
            // Lossy: every subsampling x bit depth, several quantizers, tiny and odd sizes
            // ---------------------------------------------------------------------------------------------------------
            new("intra-420-8bit-8x8", TestSrc2("8x8"), "yuv420p", AomStill(4, 10)) { Requires = ["av1.subsampling=4:2:0", "av1.bitDepth=8", "av1.loop_filter"] },
            new("intra-420-8bit-18x10", Noise(TestSrc2("18x10"), 25, 7), "yuv420p", AomStill(3, 35)) { Requires = ["av1.subsampling=4:2:0", "av1.bitDepth=8"] },
            new("intra-420-8bit-34x36-q45", Mandelbrot("34x36"), "yuv420p", AomStill(2, 45)) { Requires = ["av1.subsampling=4:2:0", "av1.bitDepth=8"] },
            new("intra-420-8bit-34x36-q1", Noise(TestSrc2("34x36"), 10, 7), "yuv420p", AomStill(5, 1)) { Requires = ["av1.subsampling=4:2:0", "av1.bitDepth=8"] },
            new("intra-444-8bit-16x16", Mandelbrot("16x16"), "yuv444p", AomStill(2, 30)) { Requires = ["av1.subsampling=4:4:4", "av1.bitDepth=8"] },
            new("intra-422-8bit-18x10", Noise(TestSrc2("18x10"), 25, 7), "yuv422p", AomStill(6, 55)) { Requires = ["av1.subsampling=4:2:2", "av1.bitDepth=8"] },
            new("intra-400-8bit-34x36-q63", Mandelbrot("34x36"), "gray", AomStill(3, 63)) { Requires = ["av1.subsampling=4:0:0", "av1.bitDepth=8", "av1.base_q_idx=255"] },
            new("intra-420-10bit-64x64", Noise(TestSrc2("64x64"), 25, 7), "yuv420p10le", AomStill(5, 25)) { Requires = ["av1.subsampling=4:2:0", "av1.bitDepth=10"] },
            new("intra-444-10bit-66x68", Mandelbrot("66x68"), "yuv444p10le", AomStill(4, 35)) { Requires = ["av1.subsampling=4:4:4", "av1.bitDepth=10"] },
            new("intra-422-10bit-34x36", Noise(TestSrc2("34x36"), 25, 7), "yuv422p10le", AomStill(5, 20)) { Requires = ["av1.subsampling=4:2:2", "av1.bitDepth=10"] },
            new("intra-400-10bit-66x68", Noise(TestSrc2("66x68"), 25, 7), "gray10le", AomStill(6, 40)) { Requires = ["av1.subsampling=4:0:0", "av1.bitDepth=10"] },
            new("intra-420-12bit-128x128", Mandelbrot("128x128"), "yuv420p12le", AomStill(4, 45)) { Requires = ["av1.subsampling=4:2:0", "av1.bitDepth=12"] },
            new("intra-444-12bit-34x36", Noise(TestSrc2("34x36"), 25, 7), "yuv444p12le", AomStill(3, 15)) { Requires = ["av1.subsampling=4:4:4", "av1.bitDepth=12"] },
            new("intra-422-12bit-66x68", Mandelbrot("66x68"), "yuv422p12le", AomStill(6, 50)) { Requires = ["av1.subsampling=4:2:2", "av1.bitDepth=12"] },
            new("intra-400-12bit-16x16", Noise(TestSrc2("16x16"), 25, 7), "gray12le", AomStill(4, 5)) { Requires = ["av1.subsampling=4:0:0", "av1.bitDepth=12"] },
            new("intra-420-8bit-200x152-q60", Noise(TestSrc2("200x152"), 25, 7), "yuv420p", AomStill(6, 60)) { Requires = ["av1.loop_filter"] },
            new("intra-420-8bit-96x96-cpu0", Mandelbrot("96x96"), "yuv420p", AomStill(0, 30)) { Requires = ["av1.superblock=128", "av1.tx_mode=select"] },
            new("intra-444-8bit-96x96-cpu1", TestSrc2("96x96"), "yuv444p", AomStill(1, 20, "enable-intrabc=0")) { Requires = ["av1.allow_screen_content_tools"] },
            new("intra-svt-420-8bit-64x64", Noise(TestSrc2("64x64"), 25, 7), "yuv420p", Svt(6, 30, "avif=1")) { Requires = ["av1.still_picture", Reduced, "av1.enable_intra_edge_filter=0"] },

            // ---------------------------------------------------------------------------------------------------------
            // Superblock size, transform and partition restrictions, intra prediction tools, CDF updates
            // ---------------------------------------------------------------------------------------------------------
            new("sb128-no-tx64-420-130x98", Mandelbrot("130x98"), "yuv420p", AomStill(5, 20, "sb-size=128:enable-tx64=0")) { Requires = ["av1.superblock=128"] },
            new("reduced-tx-set-no-rect-partitions", Mandelbrot("96x96"), "yuv420p", AomStill(6, 40, "reduced-tx-type-set=1:enable-rect-partitions=0")) { Requires = ["av1.reduced_tx_set"] },
            new("no-ab-no-4way-partitions", Mandelbrot("96x96"), "yuv420p", AomStill(3, 30, "enable-ab-partitions=0:enable-1to4-partitions=0")),
            new("no-cfl-no-filter-intra", Mandelbrot("96x96"), "yuv420p", AomStill(3, 30, "enable-cfl-intra=0:enable-filter-intra=0")) { Requires = ["av1.enable_filter_intra=0"] },
            new("no-smooth-paeth-angle-delta", Mandelbrot("96x96"), "yuv420p", AomStill(3, 30, "enable-smooth-intra=0:enable-paeth-intra=0:enable-angle-delta=0")),
            new("no-intra-edge-filter", Mandelbrot("96x96"), "yuv420p", AomStill(3, 30, "enable-intra-edge-filter=0")) { Requires = ["av1.enable_intra_edge_filter=0"] },
            new("no-cdf-update", Mandelbrot("96x96"), "yuv420p", AomIntra(3, 30, "cdf-update-mode=0")) { Requires = ["av1.disable_cdf_update"] },

            // ---------------------------------------------------------------------------------------------------------
            // Tiles and tile groups
            // ---------------------------------------------------------------------------------------------------------
            new("tiles-2x2-420-200x152", Mandelbrot("200x152"), "yuv420p", AomStill(5, 40, "sb-size=64", "-tile-columns", "1", "-tile-rows", "1")) { Requires = ["av1.tiles=2x2", "av1.tiles.spacing=uniform"] },
            new("tiles-4x1-uniform-420-200x64", Mandelbrot("200x64"), "yuv420p", AomStill(5, 40, "sb-size=64", "-tile-columns", "2")) { Requires = ["av1.tiles=4x1", "av1.tiles.spacing=uniform"] },
            new("tiles-4x1-explicit-420-264x64", Mandelbrot("264x64"), "yuv420p", AomStill(5, 40, "sb-size=64", "-tile-columns", "2")) { Requires = ["av1.tiles=4x1", "av1.tiles.spacing=explicit"] },
            new("tiles-2x2-two-groups-420-200x152", Mandelbrot("200x152"), "yuv420p", AomGood(5, 40, "sb-size=64:num-tile-groups=2", "-tile-columns", "1", "-tile-rows", "1")) { Requires = ["av1.tiles=2x2", "av1.tileGroups=2"] },
            new("tiles-2x2-four-groups-444-10bit-130x98", Mandelbrot("130x98"), "yuv444p10le", AomIntra(5, 35, "sb-size=64:num-tile-groups=4", "-tile-columns", "1", "-tile-rows", "1")) { Requires = ["av1.tiles=2x2", "av1.tileGroups=4"] },
            new("tiles-1x2-sb128-420-130x152", Mandelbrot("130x152"), "yuv420p", AomStill(5, 45, "sb-size=128", "-tile-rows", "1")) { Requires = ["av1.tiles=1x2", "av1.superblock=128"] },

            // ---------------------------------------------------------------------------------------------------------
            // Quantizer matrices, delta q, delta lf, segmentation
            // ---------------------------------------------------------------------------------------------------------
            new("qmatrix-420-64x64", Noise(TestSrc2("64x64"), 25, 7), "yuv420p", AomStill(5, 25, "enable-qm=1:enable-chroma-deltaq=1")) { Requires = ["av1.using_qmatrix"] },
            new("qmatrix-422-130x98", Mandelbrot("130x98"), "yuv422p", AomGood(3, 60, "enable-qm=1:deltaq-mode=0")) { Requires = ["av1.using_qmatrix", "av1.subsampling=4:2:2"] },
            new("qmatrix-444-10bit-66x68", Mandelbrot("66x68"), "yuv444p10le", AomStill(4, 40, "enable-qm=1:qm-min=2:qm-max=10")) { Requires = ["av1.using_qmatrix", "av1.bitDepth=10"] },
            new("delta-q-420-130x98", Noise(Bars("130x98"), 10, 3), "yuv420p", AomStill(4, 40, "deltaq-mode=2")) { Requires = ["av1.delta_q_present"] },
            new("delta-q-delta-lf-420-130x98", Noise(Bars("130x98"), 10, 3), "yuv420p", AomStill(4, 40, "deltaq-mode=2:delta-lf-mode=1")) { Requires = ["av1.delta_q_present", "av1.delta_lf_present"] },
            new("delta-q-delta-lf-444-10bit-130x98", Noise(Bars("130x98"), 10, 3), "yuv444p10le", AomStill(4, 30, "deltaq-mode=4:delta-lf-mode=1")) { Requires = ["av1.delta_q_present", "av1.delta_lf_present", "av1.bitDepth=10"] },
            new("delta-q-delta-lf-cdef-420-130x98", Mandelbrot("130x98"), "yuv420p", AomGood(4, 40, "deltaq-mode=2:delta-lf-mode=1")) { Requires = ["av1.delta_q_present", "av1.delta_lf_present"] },
            new("tune-iq-420-130x98", Noise(Bars("130x98"), 10, 3), "yuv420p", AomStill(4, 40, "tune=iq")) { Requires = ["av1.delta_q_present", "av1.using_qmatrix"] },
            new("segmentation-420-130x98", Mandelbrot("130x98"), "yuv420p", AomGood(5, 45, null, "-aq-mode", "1")) { Requires = ["av1.segmentation_enabled", "av1.segmentation.features=0"] },
            new("segmentation-sb128-420-10bit-130x98", Mandelbrot("130x98"), "yuv420p10le", AomGood(5, 45, "sb-size=128", "-aq-mode", "1")) { Requires = ["av1.segmentation_enabled", "av1.superblock=128"] },
            new("segmentation-svt-420-130x98", Mandelbrot("130x98"), "yuv420p", Svt(6, 40, "aq-mode=1")) { Requires = ["av1.segmentation_enabled"] },
            new("delta-q-svt-420-130x98", Noise(Bars("130x98"), 10, 3), "yuv420p", Svt(6, 40, "aq-mode=2:enable-variance-boost=1")) { Requires = ["av1.delta_q_present", "av1.cdef_bits=1"] },
            // SVT-AV1 is the encoder that offsets the luma DC quantizer (delta_q_y_dc); libaom never does
            new("delta-q-y-dc-svt-420-130x98", Noise(Bars("130x98"), 10, 3), "yuv420p", Svt(6, 40, "luma-y-dc-qindex-offset=6")) { Requires = ["av1.delta_q.y_dc"] },

            // ---------------------------------------------------------------------------------------------------------
            // Palette (screen content tools); intra block copy is off
            // ---------------------------------------------------------------------------------------------------------
            new("palette-444-8bit-128x96", TestSrc("128x96"), "yuv444p", AomStill(4, 20, "tune-content=screen:enable-intrabc=0")) { Requires = ["av1.allow_screen_content_tools", "av1.subsampling=4:4:4"] },
            new("palette-420-8bit-128x96", TestSrc("128x96"), "yuv420p", AomStill(4, 30, "tune-content=screen:enable-intrabc=0")) { Requires = ["av1.allow_screen_content_tools", "av1.subsampling=4:2:0"] },
            new("palette-420-10bit-128x96", TestSrc("128x96"), "yuv420p10le", AomStill(4, 30, "tune-content=screen:enable-intrabc=0")) { Requires = ["av1.allow_screen_content_tools", "av1.bitDepth=10"] },
            new("palette-400-8bit-66x68", TestSrc("66x68"), "gray", AomStill(4, 25, "tune-content=screen:enable-intrabc=0")) { Requires = ["av1.allow_screen_content_tools", "av1.subsampling=4:0:0"] },

            // ---------------------------------------------------------------------------------------------------------
            // CDEF
            // ---------------------------------------------------------------------------------------------------------
            new("cdef-420-130x98", Mandelbrot("130x98"), "yuv420p", AomStill(5, 40, null, "-enable-cdef", "1")) { Requires = ["av1.cdef_bits=0"] },
            new("cdef-422-10bit-66x68", Noise(TestSrc2("66x68"), 25, 7), "yuv422p10le", AomStill(4, 45, null, "-enable-cdef", "1")) { Requires = ["av1.cdef_bits=0", "av1.subsampling=4:2:2"] },
            new("cdef-400-12bit-66x68", Mandelbrot("66x68"), "gray12le", AomStill(4, 45, null, "-enable-cdef", "1")) { Requires = ["av1.cdef_bits=0", "av1.subsampling=4:0:0"] },
            new("cdef-bits1-420-130x98", Mandelbrot("130x98"), "yuv420p", AomGood(4, 35)) { Requires = ["av1.cdef_bits=1"] },

            // ---------------------------------------------------------------------------------------------------------
            // Loop restoration: Wiener, self-guided, switchable; luma and chroma
            // ---------------------------------------------------------------------------------------------------------
            new("lr-wiener-420-200x152", Mandelbrot("200x152"), "yuv420p", AomStill(4, 50)) { Requires = ["av1.lr.y=wiener", "av1.lr.u=wiener", "av1.lr.v=wiener"] },
            new("lr-sgrproj-mixed-420-200x152", Noise(Mandelbrot("200x152"), 8, 2), "yuv420p", AomStill(2, 40)) { Requires = ["av1.lr.y=sgrproj", "av1.lr.u=wiener", "av1.lr.v=sgrproj"] },
            new("lr-wiener-chroma-444-130x98", Mandelbrot("130x98"), "yuv444p", AomGood(2, 50)) { Requires = ["av1.lr.u=wiener", "av1.lr.v=wiener", "av1.subsampling=4:4:4"] },
            new("lr-sgrproj-luma-420-10bit-192x128", Noise(TestSrc2("192x128"), 12, 7), "yuv420p10le", AomGood(0, 50)) { Requires = ["av1.lr.y=sgrproj", "av1.bitDepth=10"] },
            new("lr-sgrproj-svt-420-200x152", Noise(TestSrc2("200x152"), 12, 7), "yuv420p", Svt(2, 40, "enable-restoration=1")) { Requires = ["av1.lr.y=sgrproj", "av1.lr.u=sgrproj", "av1.lr.v=sgrproj"] },
            new("lr-switchable-luma-420-384x216", Noise(TestSrc2("384x216"), 25, 7), "yuv420p", AomGood(4, 42)) { Requires = ["av1.lr.y=switchable", "av1.cdef_bits=2"] },
            new("lr-switchable-chroma-444-384x216", Noise(Mandelbrot("384x216"), 15, 4), "yuv444p", AomGood(4, 45)) { Requires = ["av1.lr.v=switchable"] },

            // 512x256, two halves of different content: the planes have several restoration units and the encoder picks
            // another filter for each of them (switchable in every plane), and 64-sample units at the lower quantizer
            new("lr-switchable-all-planes-420-512x256", restorationHalves, "yuv420p", AomGood(1, 35)) { Requires = ["av1.lr.y=switchable", "av1.lr.u=switchable", "av1.lr.v=switchable", "av1.lr.unitSize=128"] },
            new("lr-units-64-420-512x256", restorationHalves, "yuv420p", AomGood(1, 25)) { Requires = ["av1.lr.unitSize=64", "av1.lr.v=switchable"] },
            new("lr-switchable-chroma-422-512x256", SideBySide(Noise(TestSrc2("256x256"), 25, 7), Mandelbrot("256x256")), "yuv422p", AomGood(1, 45)) { Requires = ["av1.lr.v=switchable", "av1.subsampling=4:2:2", "av1.lr.unitSize=128"] },

            // ---------------------------------------------------------------------------------------------------------
            // Super-resolution (SVT-AV1 key frames): several denominators, with and without loop restoration
            // ---------------------------------------------------------------------------------------------------------
            new("superres-d9-420-128x96", Noise(TestSrc2("128x96"), 25, 7), "yuv420p", Svt(8, 50, "superres-mode=1:superres-denom=9:superres-kf-denom=9:enable-restoration=1")) { Requires = ["av1.superres.denominator=9"] },
            new("superres-d13-420-128x96", Noise(TestSrc2("128x96"), 25, 7), "yuv420p", Svt(8, 40, "superres-mode=1:superres-denom=13:superres-kf-denom=13:enable-restoration=1")) { Requires = ["av1.superres.denominator=13"] },
            new("superres-d16-420-128x96", Noise(TestSrc2("128x96"), 25, 7), "yuv420p", Svt(8, 35, "superres-mode=1:superres-denom=16:superres-kf-denom=16:enable-restoration=0")) { Requires = ["av1.superres.denominator=16"] },
            new("superres-d10-420-10bit-200x152", Mandelbrot("200x152"), "yuv420p10le", Svt(5, 35, "superres-mode=1:superres-denom=10:superres-kf-denom=10:enable-restoration=1")) { Requires = ["av1.superres.denominator=10", "av1.bitDepth=10"] },
            new("superres-d14-still-420-64x64", Mandelbrot("64x64"), "yuv420p", Svt(5, 30, "avif=1:superres-mode=1:superres-denom=14:superres-kf-denom=14")) { Requires = ["av1.superres.denominator=14", Reduced] },

            // ---------------------------------------------------------------------------------------------------------
            // Film grain synthesis: the libaom test vectors (film-grain-test=1..16) and SVT-AV1 estimated grain
            // ---------------------------------------------------------------------------------------------------------
            new("film-grain-test1-420-8bit-66x50", grain, "yuv420p", AomStill(6, 35, "film-grain-test=1")) { Requires = ["av1.film_grain"] },
            new("film-grain-test2-444-8bit-64x64", Noise(TestSrc2("64x64"), 10, 5), "yuv444p", AomStill(6, 35, "film-grain-test=2")) { Requires = ["av1.film_grain", "av1.subsampling=4:4:4"] },
            new("film-grain-test3-422-8bit-66x50", grain, "yuv422p", AomStill(6, 35, "film-grain-test=3")) { Requires = ["av1.film_grain", "av1.subsampling=4:2:2"] },
            new("film-grain-test5-400-8bit-66x50", grain, "gray", AomStill(6, 35, "film-grain-test=5")) { Requires = ["av1.film_grain", "av1.subsampling=4:0:0"] },
            new("film-grain-test8-420-10bit-130x98", Noise(TestSrc2("130x98"), 10, 5), "yuv420p10le", AomStill(6, 35, "film-grain-test=8")) { Requires = ["av1.film_grain", "av1.bitDepth=10"] },
            new("film-grain-test9-422-10bit-66x50", grain, "yuv422p10le", AomStill(6, 35, "film-grain-test=9")) { Requires = ["av1.film_grain", "av1.subsampling=4:2:2", "av1.bitDepth=10"] },
            new("film-grain-test11-420-8bit-66x50", grain, "yuv420p", AomStill(6, 35, "film-grain-test=11")) { Requires = ["av1.film_grain"] },
            new("film-grain-test12-444-10bit-66x50", grain, "yuv444p10le", AomStill(6, 35, "film-grain-test=12")) { Requires = ["av1.film_grain", "av1.subsampling=4:4:4", "av1.bitDepth=10"] },
            new("film-grain-test13-400-12bit-66x50", grain, "gray12le", AomStill(6, 35, "film-grain-test=13")) { Requires = ["av1.film_grain", "av1.subsampling=4:0:0", "av1.bitDepth=12"] },
            new("film-grain-test14-420-12bit-66x50", grain, "yuv420p12le", AomStill(6, 35, "film-grain-test=14")) { Requires = ["av1.film_grain", "av1.bitDepth=12"] },
            new("film-grain-test15-444-12bit-34x36", Noise(TestSrc2("34x36"), 10, 5), "yuv444p12le", AomStill(6, 35, "film-grain-test=15")) { Requires = ["av1.film_grain", "av1.bitDepth=12"] },
            new("film-grain-test4-420-8bit-66x50", grain, "yuv420p", AomStill(6, 35, "film-grain-test=4")) { Requires = ["av1.film_grain"] },
            new("film-grain-test6-444-8bit-66x50", grain, "yuv444p", AomStill(6, 35, "film-grain-test=6")) { Requires = ["av1.film_grain", "av1.subsampling=4:4:4"] },
            new("film-grain-test7-422-8bit-66x50", grain, "yuv422p", AomStill(6, 35, "film-grain-test=7")) { Requires = ["av1.film_grain", "av1.subsampling=4:2:2"] },
            new("film-grain-test10-420-10bit-66x50", grain, "yuv420p10le", AomStill(6, 35, "film-grain-test=10")) { Requires = ["av1.film_grain", "av1.bitDepth=10"] },
            new("film-grain-test16-422-12bit-66x50", grain, "yuv422p12le", AomStill(6, 35, "film-grain-test=16")) { Requires = ["av1.film_grain", "av1.subsampling=4:2:2", "av1.bitDepth=12"] },
            new("film-grain-svt-420-8bit-128x96", Noise(TestSrc2("128x96"), 10, 5), "yuv420p", Svt(6, 35, "avif=1:film-grain=30")) { Requires = ["av1.film_grain"] },
            new("film-grain-svt-420-10bit-128x96", Noise(TestSrc2("128x96"), 10, 5), "yuv420p10le", Svt(6, 35, "avif=1:film-grain=50")) { Requires = ["av1.film_grain", "av1.bitDepth=10"] },

            // ---------------------------------------------------------------------------------------------------------
            // Several key frames in one stream (every frame resets the decoder state)
            // ---------------------------------------------------------------------------------------------------------
            new("frames-3-good-420-64x64", Noise(TestSrc2("64x64"), 25, 7), "yuv420p", AomGood(6, 40, null, "-g", "1"), Frames: 3) { Requires = ["av1.frames=3"] },
            new("frames-2-svt-420-10bit-66x50", Mandelbrot("66x50"), "yuv420p10le", Svt(8, 45, null, "-g", "1"), Frames: 2) { Requires = ["av1.frames=2", "av1.bitDepth=10"] },

            .. InterVectors(),
        ];
    }

    /// <summary>The recipes and the vectors made from them by header patching.</summary>
    private static List<Av1Vector> VectorsWithPatches()
    {
        var vectors = Vectors();
        return
        [
            .. vectors,

            // -------------------------------------------------------------------------------------------------------------
            // Frame-level BILINEAR and SMOOTH interpolation filters, which neither encoder selects: header patches of
            // streams whose inter frames all code the regular filter (see Patched). Low delay with warped and global
            // motion and compound references, alternate reference frames (hidden frames), every subsampling, 8 to 12 bits
            // -------------------------------------------------------------------------------------------------------------
            Patched(vectors, "inter-low-delay-420-8bit-96x64", "bilinear-low-delay-420-8bit-96x64", 3, "av1.reference_select", "av1.allow_warped_motion"),
            Patched(vectors, "inter-alt-ref-420-8bit-176x144", "bilinear-alt-ref-420-8bit-176x144", 3, "av1.hidden_frames", "av1.show_existing_frame"),
            Patched(vectors, "inter-alt-ref-422-10bit-64x64", "bilinear-alt-ref-422-10bit-64x64", 3, "av1.subsampling=4:2:2", "av1.bitDepth=10"),
            Patched(vectors, "inter-low-delay-444-12bit-64x64", "bilinear-low-delay-444-12bit-64x64", 3, "av1.subsampling=4:4:4", "av1.bitDepth=12"),
            Patched(vectors, "inter-low-delay-400-8bit-64x64", "bilinear-low-delay-400-8bit-64x64", 3, "av1.subsampling=4:0:0"),
            Patched(vectors, "inter-tiles-2x2-420-8bit-128x128", "bilinear-tiles-2x2-420-8bit-128x128", 3, "av1.tiles=2x2"),
            Patched(vectors, "inter-low-delay-420-8bit-96x64", "smooth-low-delay-420-8bit-96x64", 1, "av1.reference_select"),
            Patched(vectors, "inter-alt-ref-420-12bit-64x64", "smooth-alt-ref-420-12bit-64x64", 1, "av1.bitDepth=12", "av1.hidden_frames"),

            // -------------------------------------------------------------------------------------------------------------
            // Hand-authored inter frames after a libaom key frame (see HandAuthored): the tools that are coded block by block
            // and that no encoder produces. Every subsampling, 8 to 12 bits; 128x128, the size of the 2x2 tiles of one
            // superblock the recipes are written for
            // -------------------------------------------------------------------------------------------------------------
            .. HandAuthoredVectors(Av1HandAuthoredStream.DualFilters, "av1.enable_dual_filter", "av1.interpolation_filter=switchable", "av1.interpolation_filter=bilinear"),
            .. HandAuthoredVectors(Av1HandAuthoredStream.SegmentFeatures, "av1.segmentation.temporal_update", "av1.segmentation.update_map=0", "av1.segmentation.features=5", "av1.segmentation.features=6", "av1.segmentation.features=7"),
        ];
    }

    private static List<Av1Vector> HandAuthoredVectors(Av1HandAuthoredStream.Recipe recipe, params string[] requires) =>
    [
        HandAuthored(recipe, "420-8bit", "yuv420p", "128x128", ["av1.tiles=2x2", "av1.subsampling=4:2:0", "av1.bitDepth=8", .. requires]),
        HandAuthored(recipe, "444-10bit", "yuv444p10le", "128x128", ["av1.tiles=2x2", "av1.subsampling=4:4:4", "av1.bitDepth=10", .. requires]),
        HandAuthored(recipe, "422-12bit", "yuv422p12le", "128x128", ["av1.tiles=2x2", "av1.subsampling=4:2:2", "av1.bitDepth=12", .. requires]),
        HandAuthored(recipe, "400-8bit", "gray", "128x128", ["av1.tiles=2x2", "av1.subsampling=4:0:0", "av1.bitDepth=8", .. requires]),
        HandAuthored(recipe, "420-10bit", "yuv420p10le", "128x128", ["av1.tiles=2x2", "av1.subsampling=4:2:0", "av1.bitDepth=10", .. requires]),
    ];

    // Inter vectors: 2 to 30 output frames. The tags name what a recipe exists for; the coding tools that the headers do not
    // signal (compound prediction types, overlapped and warped motion compensation, intra block copy, scaled references)
    // are checked on the decoded blocks by Av1ToolCoverageTests.
    private static List<Av1Vector> InterVectors()
    {
        const string InterFrames = "av1.frame_types=inter";
        const string Hidden = "av1.hidden_frames";
        const string ShowExisting = "av1.show_existing_frame";
        const string Compound = "av1.reference_select";
        const string IntraBlockCopy = "av1.allow_intrabc";
        var screen = TestSrc("96x64") + ":rate=10";
        var grainy = Noise(Rotating("64x64"), 8, 7);

        return
        [
            // ---------------------------------------------------------------------------------------------------------
            // libaom, low delay (no hidden frame): every subsampling, 8, 10 and 12-bit
            // ---------------------------------------------------------------------------------------------------------
            new("inter-realtime-420-8bit-64x64", Moving("64x64"), "yuv420p", AomRealtime(8, 40), Frames: 2) { Inter = true, Requires = [InterFrames, "av1.frames=2", "av1.primary_ref_frame=set"] },
            new("inter-low-delay-420-8bit-96x64", Rotating("96x64"), "yuv420p", AomLowDelay(2, 40), Frames: 4) { Inter = true, Requires = [InterFrames, Compound, "av1.skip_mode_present", "av1.allow_warped_motion", "av1.gm=rotzoom", "av1.primary_ref_frame=none"] },
            new("inter-low-delay-422-8bit-64x64", Rotating("64x64"), "yuv422p", AomLowDelay(4, 45), Frames: 3) { Inter = true, Requires = [InterFrames, "av1.subsampling=4:2:2", "av1.bitDepth=8"] },
            new("inter-low-delay-400-8bit-64x64", Rotating("64x64"), "gray", AomLowDelay(4, 40), Frames: 4) { Inter = true, Requires = [InterFrames, "av1.subsampling=4:0:0", "av1.bitDepth=8"] },
            new("inter-low-delay-420-10bit-64x64", Rotating("64x64"), "yuv420p10le", AomLowDelay(4, 40), Frames: 4) { Inter = true, Requires = [InterFrames, "av1.subsampling=4:2:0", "av1.bitDepth=10"] },
            new("inter-low-delay-444-12bit-64x64", Rotating("64x64"), "yuv444p12le", AomLowDelay(4, 45), Frames: 3) { Inter = true, Requires = [InterFrames, "av1.subsampling=4:4:4", "av1.bitDepth=12"] },
            new("inter-lossless-420-8bit-32x32", Moving("32x32"), "yuv420p", AomLowDelay(5, 0, "lossless=1"), Frames: 3) { Inter = true, Requires = [InterFrames, "av1.lossless"] },

            // ---------------------------------------------------------------------------------------------------------
            // libaom, alternate reference frames: hidden frames, show_existing_frame, compound prediction
            // ---------------------------------------------------------------------------------------------------------
            new("inter-alt-ref-420-8bit-64x64", Rotating("64x64"), "yuv420p", AomGood(1, 45), Frames: 8) { Inter = true, Requires = [InterFrames, Hidden, ShowExisting, Compound, "av1.frames=8"] },
            new("inter-alt-ref-420-8bit-176x144", Rotating("176x144", 5), "yuv420p", AomGood(2, 58), Frames: 5) { Inter = true, Requires = [InterFrames, Hidden, ShowExisting, Compound] },
            new("inter-alt-ref-420-8bit-70x54", Rotating("70x54", 4), "yuv420p", AomGood(2, 40), Frames: 5) { Inter = true, Requires = [InterFrames, Hidden, ShowExisting, Compound] },
            new("inter-alt-ref-420-8bit-16x16", Moving("16x16"), "yuv420p", AomGood(2, 32), Frames: 4) { Inter = true, Requires = [InterFrames, Hidden, "av1.force_integer_mv"] },
            new("inter-alt-ref-422-10bit-64x64", Rotating("64x64"), "yuv422p10le", AomGood(3, 45), Frames: 5) { Inter = true, Requires = [InterFrames, Hidden, ShowExisting, "av1.subsampling=4:2:2", "av1.bitDepth=10"] },
            new("inter-alt-ref-400-12bit-64x64", Rotating("64x64"), "gray12le", AomGood(3, 45), Frames: 5) { Inter = true, Requires = [InterFrames, Hidden, ShowExisting, "av1.subsampling=4:0:0", "av1.bitDepth=12"] },
            new("inter-alt-ref-420-12bit-64x64", Rotating("64x64"), "yuv420p12le", AomGood(5, 45), Frames: 5) { Inter = true, Requires = [InterFrames, Hidden, ShowExisting, "av1.subsampling=4:2:0", "av1.bitDepth=12"] },
            // The slowest speed searches every compound type (wedge, difference-weighted, distance-weighted, inter-intra)
            new("inter-compound-tools-420-8bit-96x64", Rotating("96x64"), "yuv420p", AomGood(0, 35), Frames: 6) { Inter = true, Requires = [InterFrames, Hidden, ShowExisting, Compound, "av1.skip_mode_present", "av1.superblock=128", "av1.interpolation_filter=switchable"] },
            // Without masked compounds the distance-weighted compound is what remains
            new("inter-dist-wtd-420-10bit-64x64", Rotating("64x64"), "yuv420p10le", AomGood(0, 45, "enable-dist-wtd-comp=1:enable-masked-comp=0"), Frames: 5) { Inter = true, Requires = [InterFrames, Compound, "av1.interpolation_filter=sharp", "av1.bitDepth=10"] },
            new("inter-restricted-tools-420-8bit-64x64", Rotating("64x64"), "yuv420p", AomGood(2, 40, "enable-obmc=0:enable-warped-motion=0:enable-global-motion=0:enable-ref-frame-mvs=0:enable-masked-comp=0:enable-interintra-comp=0:enable-tx64=0:reduced-tx-type-set=1"), Frames: 5) { Inter = true, Requires = [InterFrames, Compound, "av1.reduced_tx_set"] },
            new("inter-key-frames-420-8bit-64x64", Rotating("64x64"), "yuv420p", AomGood(5, 45, null, "-g", "2"), Frames: 5) { Inter = true, Requires = ["av1.frame_types=key", InterFrames, "av1.frames=5"] },

            // ---------------------------------------------------------------------------------------------------------
            // libaom: error resilience, CDF updates, tiles, quantizer matrices, segmentation, screen content, film grain
            // ---------------------------------------------------------------------------------------------------------
            new("inter-error-resilient-420-8bit-64x64", Rotating("64x64"), "yuv420p", AomGood(3, 40, "error-resilient=1"), Frames: 5) { Inter = true, Requires = [InterFrames, "av1.error_resilient_mode", "av1.frame_id_numbers_present", "av1.primary_ref_frame=none"] },
            new("inter-frame-parallel-420-8bit-64x64", Rotating("64x64"), "yuv420p", AomGood(3, 40, "frame-parallel=1"), Frames: 5) { Inter = true, Requires = [InterFrames, "av1.disable_frame_end_update_cdf"] },
            new("inter-tiles-2x2-420-8bit-128x128", Rotating("128x128"), "yuv420p", AomGood(3, 45, "sb-size=64", "-tile-columns", "1", "-tile-rows", "1"), Frames: 4) { Inter = true, Requires = [InterFrames, "av1.tiles=2x2"] },
            new("inter-qmatrix-420-8bit-64x64", Rotating("64x64"), "yuv420p", AomGood(3, 40, "enable-qm=1:deltaq-mode=1"), Frames: 4) { Inter = true, Requires = [InterFrames, "av1.using_qmatrix"] },
            // The inter frames keep the segmentation map of their primary reference frame
            new("inter-segmentation-420-8bit-96x64", Noise(Rotating("96x64"), 8, 7), "yuv420p", AomGood(3, 40, null, "-aq-mode", "1"), Frames: 5) { Inter = true, Requires = [InterFrames, "av1.segmentation_enabled", "av1.segmentation.update_map=0"] },
            // Cyclic refresh codes a new map in every inter frame, with and without temporal prediction
            new("inter-cyclic-refresh-420-8bit-96x64", Moving("96x64"), "yuv420p", AomRealtimeBitrate(7, "15k", "-aq-mode", "3"), Frames: 5) { Inter = true, Requires = [InterFrames, "av1.segmentation_enabled", "av1.segmentation.update_map=1", "av1.segmentation.temporal_update"] },
            new("inter-screen-420-8bit-96x64", screen, "yuv420p", AomGood(3, 40, "tune-content=screen"), Frames: 5) { Inter = true, Requires = [InterFrames, "av1.allow_screen_content_tools", "av1.subsampling=4:2:0"] },
            new("inter-screen-444-8bit-96x64", screen, "yuv444p", AomGood(2, 30, "tune-content=screen"), Frames: 4) { Inter = true, Requires = [InterFrames, "av1.allow_screen_content_tools", "av1.subsampling=4:4:4", "av1.bitDepth=8"] },
            new("inter-film-grain-420-8bit-64x64", Noise(Rotating("64x64"), 8, 7), "yuv420p", AomGood(4, 40, "film-grain-test=3"), Frames: 4) { Inter = true, Requires = [InterFrames, "av1.film_grain"] },
            new("inter-film-grain-444-10bit-64x64", Noise(Rotating("64x64"), 8, 7), "yuv444p10le", AomGood(4, 45, "film-grain-test=12"), Frames: 4) { Inter = true, Requires = [InterFrames, "av1.film_grain", "av1.subsampling=4:4:4", "av1.bitDepth=10"] },

            // ---------------------------------------------------------------------------------------------------------
            // Intra block copy: all-intra screen content (the only vectors whose key frames must signal allow_intrabc)
            // ---------------------------------------------------------------------------------------------------------
            new("intrabc-420-8bit-176x144", TestSrc("176x144"), "yuv420p", AomIntra(4, 40, "tune-content=screen")) { Requires = [IntraBlockCopy, "av1.subsampling=4:2:0"] },
            new("intrabc-444-8bit-176x144", TestSrc("176x144"), "yuv444p", AomIntra(4, 40, "tune-content=screen")) { Requires = [IntraBlockCopy, "av1.subsampling=4:4:4"] },

            // ---------------------------------------------------------------------------------------------------------
            // SVT-AV1: random access and low delay, 8 and 10-bit, film grain, screen content
            // ---------------------------------------------------------------------------------------------------------
            new("inter-svt-p8-420-8bit-64x64", Rotating("64x64"), "yuv420p", SvtInter(8, 45), Frames: 5) { Inter = true, Requires = [InterFrames, Hidden, ShowExisting, Compound, "av1.interpolation_filter=switchable"] },
            new("inter-svt-p0-420-8bit-64x64", Noise(Rotating("64x64"), 6, 7), "yuv420p", SvtInter(0, 45), Frames: 5) { Inter = true, Requires = [InterFrames, Hidden, ShowExisting, Compound, "av1.allow_warped_motion", "av1.gm=rotzoom"] },
            new("inter-svt-p6-420-10bit-64x64", Rotating("64x64"), "yuv420p10le", SvtInter(6, 45), Frames: 5) { Inter = true, Requires = [InterFrames, Hidden, ShowExisting, "av1.bitDepth=10"] },
            new("inter-svt-p6-420-8bit-176x144", Rotating("176x144", 5), "yuv420p", SvtInter(6, 58), Frames: 5) { Inter = true, Requires = [InterFrames, Hidden, ShowExisting, IntraBlockCopy] },
            new("inter-svt-low-delay-420-8bit-64x64", Rotating("64x64"), "yuv420p", SvtInter(4, 45, "pred-struct=1"), Frames: 5) { Inter = true, Requires = [InterFrames, Compound, "av1.skip_mode_present"] },
            new("inter-svt-film-grain-420-8bit-64x64", Noise(Rotating("64x64"), 8, 7), "yuv420p", SvtInter(8, 45, "film-grain=8"), Frames: 5) { Inter = true, Requires = [InterFrames, ShowExisting, "av1.film_grain"] },
            new("inter-svt-screen-420-8bit-96x64", screen, "yuv420p", SvtInter(6, 40, "scm=1"), Frames: 5) { Inter = true, Requires = [InterFrames, "av1.allow_screen_content_tools", IntraBlockCopy] },
            new("inter-svt-loop-restoration-420-8bit-128x96", Noise(Rotating("128x96"), 6, 7), "yuv420p", SvtInter(4, 55, "enable-restoration=1"), Frames: 5) { Inter = true, Requires = [InterFrames, "av1.lr.y=wiener"] },
            new("inter-svt-segmentation-420-8bit-96x64", Noise(Rotating("96x64"), 6, 7), "yuv420p", SvtInter(6, 45, "aq-mode=1"), Frames: 5) { Inter = true, Requires = [InterFrames, "av1.segmentation_enabled", "av1.segmentation.update_map=1"] },

            // ---------------------------------------------------------------------------------------------------------
            // SVT-AV1: super-resolution with inter frames, reference scaling (the frame size changes inside the stream),
            // switch frames, intra-only frames. Sizes are multiples of 8 (see the traps)
            // ---------------------------------------------------------------------------------------------------------
            new("inter-svt-superres-420-8bit-96x64", Rotating("96x64"), "yuv420p", SvtInter(8, 50, "superres-mode=1:superres-denom=11:superres-kf-denom=8"), Frames: 5) { Inter = true, Requires = [InterFrames, "av1.superres.denominator=11"] },
            new("inter-svt-resize-fixed-420-8bit-128x96", Rotating("128x96"), "yuv420p", SvtInter(8, 55, "resize-mode=1:resize-denom=12:resize-kf-denom=10"), Frames: 5) { Inter = true, Requires = [InterFrames, "av1.frame_sizes=102x77", "av1.frame_sizes=85x64", "av1.found_ref", "av1.render_and_frame_size_different"] },
            new("inter-svt-resize-random-420-8bit-96x64", Rotating("96x64"), "yuv420p", SvtInter(8, 50, "resize-mode=2"), Frames: 7) { Inter = true, Requires = [InterFrames, "av1.frame_sizes=64x43", "av1.frame_sizes=59x39", "av1.frame_sizes=70x47", "av1.frame_sizes=51x34", "av1.found_ref"] },
            new("inter-svt-switch-frame-420-8bit-64x64", Rotating("64x64"), "yuv420p", SvtInter(8, 55, "sframe-dist=4:sframe-mode=1:pred-struct=1"), Frames: 9) { Inter = true, Requires = ["av1.frame_types=switch", "av1.frame_size_override_flag"] },
            // Random access: the switch frame is the hidden alternate reference frame
            new("inter-svt-hidden-switch-frame-420-8bit-64x64", Rotating("64x64"), "yuv420p", SvtInter(8, 55, "sframe-dist=4:sframe-mode=2"), Frames: 5) { Inter = true, Requires = ["av1.frame_types=switch", Hidden, ShowExisting] },
            new("inter-svt-intra-only-420-8bit-64x64", Rotating("64x64"), "yuv420p", SvtInter(8, 50, "irefresh-type=1", "-g", "4"), Frames: 5) { Inter = true, Requires = ["av1.frame_types=intra_only", InterFrames, Hidden] },

            // ---------------------------------------------------------------------------------------------------------
            // Longer and larger streams: whole groups of pictures (several levels of hidden frames, every reference slot in
            // use, reference lists that change from frame to frame), and frames of several superblocks in both directions
            // ---------------------------------------------------------------------------------------------------------
            new("inter-alt-ref-420-8bit-176x144-30-frames", Rotating("176x144", 5), "yuv420p", AomGood(4, 55), Frames: 30) { Inter = true, Requires = [InterFrames, Hidden, ShowExisting, Compound, "av1.frames=30"] },
            new("inter-svt-p6-420-8bit-176x144-17-frames", Rotating("176x144", 5), "yuv420p", SvtInter(6, 55), Frames: 17) { Inter = true, Requires = [InterFrames, Hidden, ShowExisting, Compound, "av1.frames=17"] },
            new("inter-svt-p4-420-8bit-64x64-17-frames", Rotating("64x64"), "yuv420p", SvtInter(4, 50), Frames: 17) { Inter = true, Requires = [InterFrames, Hidden, ShowExisting, Compound, "av1.frames=17"] },
            new("inter-alt-ref-420-8bit-352x288", Rotating("352x288", 7), "yuv420p", AomGood(3, 55), Frames: 4) { Inter = true, Requires = [InterFrames, Hidden, Compound] },
            new("inter-svt-p8-420-8bit-352x288", Rotating("352x288", 7), "yuv420p", SvtInter(8, 55), Frames: 6) { Inter = true, Requires = [InterFrames, Hidden, ShowExisting, Compound] },
            // 128x128 superblocks and 2x2 tiles: motion vectors that cross the tile boundaries
            new("inter-tiles-2x2-sb128-420-8bit-256x192", Rotating("256x192", 6), "yuv420p", AomGood(3, 55, "sb-size=128", "-tile-columns", "1", "-tile-rows", "1"), Frames: 4) { Inter = true, Requires = [InterFrames, "av1.tiles=2x2", "av1.superblock=128"] },

            // ---------------------------------------------------------------------------------------------------------
            // More SVT-AV1 presets (each one enables other tools and other reference structures)
            // ---------------------------------------------------------------------------------------------------------
            new("inter-svt-p2-420-8bit-64x64", Rotating("64x64"), "yuv420p", SvtInter(2, 45), Frames: 9) { Inter = true, Requires = [InterFrames, Hidden, ShowExisting, Compound] },
            new("inter-svt-p10-420-8bit-96x64", Rotating("96x64"), "yuv420p", SvtInter(10, 45), Frames: 9) { Inter = true, Requires = [InterFrames, Hidden, ShowExisting] },
            new("inter-svt-p12-420-8bit-96x64", Rotating("96x64"), "yuv420p", SvtInter(12, 45), Frames: 9) { Inter = true, Requires = [InterFrames, Hidden, ShowExisting] },
            new("inter-svt-delta-q-y-dc-420-10bit-64x64", Rotating("64x64"), "yuv420p10le", SvtInter(6, 40, "luma-y-dc-qindex-offset=-12"), Frames: 5) { Inter = true, Requires = [InterFrames, "av1.delta_q.y_dc", "av1.bitDepth=10"] },

            // ---------------------------------------------------------------------------------------------------------
            // libaom, alternate reference frames: the subsamplings and bit depths the first vectors left out
            // ---------------------------------------------------------------------------------------------------------
            new("inter-alt-ref-422-8bit-64x64", Rotating("64x64"), "yuv422p", AomGood(1, 45), Frames: 8) { Inter = true, Requires = [InterFrames, Hidden, ShowExisting, "av1.subsampling=4:2:2", "av1.bitDepth=8", "av1.interpolation_filter=switchable"] },
            new("inter-alt-ref-444-8bit-64x64", Rotating("64x64"), "yuv444p", AomGood(1, 45), Frames: 8) { Inter = true, Requires = [InterFrames, Hidden, ShowExisting, "av1.subsampling=4:4:4", "av1.bitDepth=8", "av1.interpolation_filter=switchable"] },
            new("inter-alt-ref-444-10bit-64x64", Rotating("64x64"), "yuv444p10le", AomGood(2, 45), Frames: 6) { Inter = true, Requires = [InterFrames, Hidden, ShowExisting, "av1.subsampling=4:4:4", "av1.bitDepth=10"] },
            new("inter-alt-ref-444-12bit-64x64", Rotating("64x64"), "yuv444p12le", AomGood(3, 45), Frames: 6) { Inter = true, Requires = [InterFrames, Hidden, ShowExisting, "av1.subsampling=4:4:4", "av1.bitDepth=12"] },
            new("inter-alt-ref-422-12bit-64x64", Rotating("64x64"), "yuv422p12le", AomGood(3, 45), Frames: 6) { Inter = true, Requires = [InterFrames, Hidden, ShowExisting, "av1.subsampling=4:2:2", "av1.bitDepth=12"] },
            new("inter-alt-ref-400-10bit-64x64", Rotating("64x64"), "gray10le", AomGood(3, 45), Frames: 6) { Inter = true, Requires = [InterFrames, Hidden, ShowExisting, "av1.subsampling=4:0:0", "av1.bitDepth=10"] },

            // ---------------------------------------------------------------------------------------------------------
            // Film grain on inter frames: more of the libaom parameter sets, every subsampling and bit depth, grain
            // parameters estimated by the encoders, and parameters taken from a reference frame (update_grain = 0, which
            // libaom codes for its test vector 9)
            // ---------------------------------------------------------------------------------------------------------
            new("inter-film-grain-test1-420-8bit-64x64", grainy, "yuv420p", AomGood(4, 40, "film-grain-test=1"), Frames: 5) { Inter = true, Requires = [InterFrames, ShowExisting, "av1.film_grain"] },
            new("inter-film-grain-test4-420-8bit-64x64", grainy, "yuv420p", AomGood(4, 40, "film-grain-test=4"), Frames: 5) { Inter = true, Requires = [InterFrames, "av1.film_grain"] },
            new("inter-film-grain-test8-420-10bit-64x64", grainy, "yuv420p10le", AomGood(4, 40, "film-grain-test=8"), Frames: 5) { Inter = true, Requires = [InterFrames, "av1.film_grain", "av1.bitDepth=10"] },
            new("inter-film-grain-from-reference-422-10bit-64x64", grainy, "yuv422p10le", AomGood(4, 40, "film-grain-test=9"), Frames: 5) { Inter = true, Requires = [InterFrames, "av1.film_grain.from_reference", "av1.subsampling=4:2:2", "av1.bitDepth=10"] },
            new("inter-film-grain-test13-400-12bit-64x64", grainy, "gray12le", AomGood(4, 40, "film-grain-test=13"), Frames: 5) { Inter = true, Requires = [InterFrames, "av1.film_grain", "av1.subsampling=4:0:0", "av1.bitDepth=12"] },
            new("inter-film-grain-test14-420-12bit-64x64", grainy, "yuv420p12le", AomGood(4, 40, "film-grain-test=14"), Frames: 5) { Inter = true, Requires = [InterFrames, "av1.film_grain", "av1.bitDepth=12"] },
            new("inter-film-grain-test15-444-12bit-64x64", grainy, "yuv444p12le", AomGood(4, 40, "film-grain-test=15"), Frames: 5) { Inter = true, Requires = [InterFrames, "av1.film_grain", "av1.subsampling=4:4:4", "av1.bitDepth=12"] },
            new("inter-film-grain-estimated-420-8bit-64x64", grainy, "yuv420p", AomGood(4, 40, null, "-denoise-noise-level", "30", "-denoise-block-size", "16"), Frames: 6) { Inter = true, Requires = [InterFrames, "av1.film_grain"] },
            new("inter-svt-film-grain-420-10bit-64x64", grainy, "yuv420p10le", SvtInter(8, 45, "film-grain=20"), Frames: 9) { Inter = true, Requires = [InterFrames, ShowExisting, "av1.film_grain", "av1.bitDepth=10"] },

            // ---------------------------------------------------------------------------------------------------------
            // Intra block copy at 10 and 12 bits (two bytes per sample in the copy) and with 4:4:4 and 4:2:2 chroma
            // ---------------------------------------------------------------------------------------------------------
            new("intrabc-420-10bit-176x144", TestSrc("176x144"), "yuv420p10le", AomIntra(4, 40, "tune-content=screen")) { Requires = [IntraBlockCopy, "av1.subsampling=4:2:0", "av1.bitDepth=10"] },
            new("intrabc-444-10bit-176x144", TestSrc("176x144"), "yuv444p10le", AomIntra(4, 40, "tune-content=screen")) { Requires = [IntraBlockCopy, "av1.subsampling=4:4:4", "av1.bitDepth=10"] },
            new("intrabc-422-12bit-176x144", TestSrc("176x144"), "yuv422p12le", AomIntra(4, 40, "tune-content=screen")) { Requires = [IntraBlockCopy, "av1.subsampling=4:2:2", "av1.bitDepth=12"] },

            // ---------------------------------------------------------------------------------------------------------
            // SVT-AV1 reference scaling and super-resolution: more sizes, longer streams, 10-bit, both tools at once
            // ---------------------------------------------------------------------------------------------------------
            new("inter-svt-resize-fixed-420-8bit-176x144", Rotating("176x144", 4), "yuv420p", SvtInter(8, 50, "resize-mode=1:resize-denom=16:resize-kf-denom=9"), Frames: 9) { Inter = true, Requires = [InterFrames, "av1.frame_sizes", "av1.found_ref"] },
            new("inter-svt-resize-fixed-420-8bit-64x64", Rotating("64x64", 4), "yuv420p", SvtInter(8, 50, "resize-mode=1:resize-denom=13:resize-kf-denom=8"), Frames: 5) { Inter = true, Requires = [InterFrames, "av1.frame_sizes", "av1.found_ref"] },
            new("inter-svt-resize-random-420-8bit-176x144", Rotating("176x144", 4), "yuv420p", SvtInter(8, 50, "resize-mode=2"), Frames: 17) { Inter = true, Requires = [InterFrames, "av1.frame_sizes", "av1.found_ref", "av1.frames=17"] },
            new("inter-svt-resize-random-420-8bit-128x96", Rotating("128x96", 4), "yuv420p", SvtInter(6, 50, "resize-mode=2"), Frames: 17) { Inter = true, Requires = [InterFrames, "av1.frame_sizes", "av1.found_ref"] },
            new("inter-svt-resize-random-420-10bit-256x192", Rotating("256x192", 4), "yuv420p10le", SvtInter(8, 55, "resize-mode=2"), Frames: 9) { Inter = true, Requires = [InterFrames, "av1.frame_sizes", "av1.found_ref", "av1.bitDepth=10"] },
            new("inter-svt-superres-random-420-8bit-176x144", Rotating("176x144", 4), "yuv420p", SvtInter(8, 50, "superres-mode=2"), Frames: 9) { Inter = true, Requires = [InterFrames, "av1.superres.denominator"] },
            new("inter-svt-superres-420-10bit-128x96", Rotating("128x96", 4), "yuv420p10le", SvtInter(6, 50, "superres-mode=1:superres-denom=14:superres-kf-denom=10:enable-restoration=1"), Frames: 9) { Inter = true, Requires = [InterFrames, "av1.superres.denominator=14", "av1.bitDepth=10"] },
            new("inter-svt-superres-resize-420-8bit-176x144", Rotating("176x144", 4), "yuv420p", SvtInter(8, 50, "superres-mode=1:superres-denom=12:superres-kf-denom=12:resize-mode=1:resize-denom=12:resize-kf-denom=12"), Frames: 9) { Inter = true, Requires = [InterFrames, "av1.superres.denominator=12", "av1.frame_size_override_flag"] },
        ];
    }
}
