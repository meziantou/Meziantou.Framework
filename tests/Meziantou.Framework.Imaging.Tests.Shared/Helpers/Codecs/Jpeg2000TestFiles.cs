namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Small JPEG 2000 codestreams and files written by the reference tools (OpenJPEG 2.5.4 and OpenJPH 0.32.0, the versions
/// pinned by Meziantou.Prebuilt 3.6.0), with the command that produced each of them. Their content is the input given to
/// the tool: the tests never derive an expectation from the library.
/// </summary>
internal static class Jpeg2000TestFiles
{
    /// <summary>Gets the 8-bit samples given to the tools: sample <c>i</c> is <c>(i * 37 + 11) mod 256</c>.</summary>
    public static byte[] GetPattern8(int count)
    {
        var samples = new byte[count];
        for (var i = 0; i < count; i++)
        {
            samples[i] = (byte)((i * 37) + 11);
        }

        return samples;
    }

    /// <summary>Gets the 16-bit samples given to the tools: sample <c>i</c> is <c>(i * 3701 + 11) mod 65536</c>.</summary>
    public static ushort[] GetPattern16(int count)
    {
        var samples = new ushort[count];
        for (var i = 0; i < count; i++)
        {
            samples[i] = (ushort)((i * 3701) + 11);
        }

        return samples;
    }

    /// <summary>
    /// A codestream: 5x3, 8-bit grayscale, reversible, two decomposition levels. <c>opj_compress -i gray.pgm -o gray.j2k -n 2</c>,
    /// where sample <c>i</c> of the PGM file is <c>(i * 37 + 11) mod 256</c>.
    /// </summary>
    public static byte[] Gray => Convert.FromBase64String(
        "/0//UQApAAAAAAAFAAAAAwAAAAAAAAAAAAAABQAAAAMAAAAAAAAAAAABBwEB/1IADAAAAAEAAQQEAAH/XAAHQEBISFD/ZAAlAAFDcmVhdGVkIGJ5IE9wZW5KUEVHIHZlcnNpb24gMi41LjT/kAAKAA" +
        "AAAAAqAAH/k8+0HApleb99J1/H2gc/AEj8AIACHTUDIUl/AwP/2Q==");

    /// <summary>
    /// A codestream: 5x3, 8-bit RGB, reversible, with the reversible component transformation. <c>opj_compress -i rgb.ppm
    /// -o rgb.j2k -n 2</c>, where sample <c>i</c> of the PPM file (row by row, R then G then B) is <c>(i * 37 + 11) mod 256</c>.
    /// </summary>
    public static byte[] Rgb => Convert.FromBase64String(
        "/0//UQAvAAAAAAAFAAAAAwAAAAAAAAAAAAAABQAAAAMAAAAAAAAAAAADBwEBBwEBBwEB/1IADAAAAAEBAQQEAAH/XAAHQEBISFD/ZAAlAAFDcmVhdGVkIGJ5IE9wZW5KUEVHIHZlcnNpb24gMi41Lj" +
        "T/kAAKAAAAAABjAAH/k8fUDApay/rSoM+0HAOqbRIq6bXPtBwFbGo7VZw3z8AWD5wcH1AQDtCQjo8CsL8Bh8/ADj7QOPwAgAxKKQLVbwMDz8AWfgCQ+0CADjc1jX8LoK9/AS//2Q==");

    /// <summary>
    /// A JP2 file: 5x3, 8-bit RGB with an opacity, reversible. <c>opj_compress -i rgba.pam -o rgba.jp2 -n 2</c>, where sample
    /// <c>i</c> of the PAM file (R, G, B, A) is <c>(i * 37 + 11) mod 256</c>. The file has no channel definition box: its four
    /// channels are the three colors of sRGB and one more.
    /// </summary>
    public static byte[] RgbaFile => Convert.FromBase64String(
        "AAAADGpQICANCocKAAAAFGZ0eXBqcDIgAAAAAGpwMiAAAAAtanAyaAAAABZpaGRyAAAAAwAAAAUABAcHAAAAAAAPY29scgEAAAAAABAAAAD8anAyY/9P/1EAMgAAAAAABQAAAAMAAAAAAAAAAAAAAA" +
        "UAAAADAAAAAAAAAAAABAcBAQcBAQcBAQcBAf9SAAwAAAABAQEEBAAB/1wAB0BASEhQ/2QAJQABQ3JlYXRlZCBieSBPcGVuSlBFRyB2ZXJzaW9uIDIuNS40/5AACgAAAAAAfgAB/5PH1A4LRkwyRN6o" +
        "z7QcALftKegwf8+0HAK8rsZ2PSfH1AwKNz9EwFPPwBYPnBwfUBAE9/Pp2gVonwI3z8AOPtA4/ACADmjKAsqvCo3PwBJ+AJD7QIAMMMUIAADgHwoPz8ASPtA4/ACAClq2nwLEPwEv/9k=");

    /// <summary>
    /// A codestream of two components: 5x3, an 8-bit gray level and its opacity, reversible. <c>opj_compress -i graya.pam
    /// -o graya.j2k -n 2</c>, where sample <c>i</c> of the PAM file (gray, alpha) is <c>(i * 37 + 11) mod 256</c>.
    /// </summary>
    public static byte[] GrayAlpha => Convert.FromBase64String(
        "/0//UQAsAAAAAAAFAAAAAwAAAAAAAAAAAAAABQAAAAMAAAAAAAAAAAACBwEBBwEB/1IADAAAAAEAAQQEAAH/XAAHQEBISFD/ZAAlAAFDcmVhdGVkIGJ5IE9wZW5KUEVHIHZlcnNpb24gMi41LjT/kA" +
        "AKAAAAAABCAAH/k8+0HAnrqsQT+ofH1AwSGJtSvn/H2gcfaBx+AEADh68AuVsAL8/ADn4AcfgAgA3AXwLxPwT/2Q==");

    /// <summary>
    /// A codestream: 5x3, 16-bit grayscale, reversible. <c>opj_compress -i gray16.pgm -o gray16.j2k -n 2</c>, where sample
    /// <c>i</c> of the PGM file is <c>(i * 3701 + 11) mod 65536</c>.
    /// </summary>
    public static byte[] Gray16 => Convert.FromBase64String(
        "/0//UQApAAAAAAAFAAAAAwAAAAAAAAAAAAAABQAAAAMAAAAAAAAAAAABDwEB/1IADAAAAAEAAQQEAAH/XAAHQICIiJD/ZAAlAAFDcmVhdGVkIGJ5IE9wZW5KUEVHIHZlcnNpb24gMi41LjT/kAAKAA" +
        "AAAAAhAAH/k8/8MDgGdprdkrrg47rZOto+L4D/2Q==");

    /// <summary>
    /// A JP2 file: 5x3, 16-bit RGB, reversible. <c>opj_compress -i rgb16.ppm -o rgb16.jp2 -n 2</c>, where sample <c>i</c> of the
    /// PPM file is <c>(i * 3701 + 11) mod 65536</c>.
    /// </summary>
    public static byte[] Rgb16File => Convert.FromBase64String(
        "AAAADGpQICANCocKAAAAFGZ0eXBqcDIgAAAAAGpwMiAAAAAtanAyaAAAABZpaGRyAAAAAwAAAAUAAw8HAAAAAAAPY29scgEAAAAAABAAAADNanAyY/9P/1EALwAAAAAABQAAAAMAAAAAAAAAAAAAAA" +
        "UAAAADAAAAAAAAAAAAAw8BAQ8BAQ8BAf9SAAwAAAABAQEEBAAB/1wAB0CAiIiQ/2QAJQABQ3JlYXRlZCBieSBPcGVuSlBFRyB2ZXJzaW9uIDIuNS40/5AACgAAAAAAUgAB/5Pf+JBwC4qvwVo8FxR3" +
        "mLdzrM/B/gKAAdg4TqUgWkWYd8H+AoAIkExOpSBaRZh3x/4YBw/8GA4/8SAgB2+7ABrfBpeAgP/Z");

    /// <summary>
    /// A codestream coded with the irreversible filter and component transformation, at its full rate: 5x3, 8-bit RGB.
    /// <c>opj_compress -i rgb.ppm -o rgb-irreversible.j2k -n 2 -I</c>, with the PPM file of <see cref="Rgb"/>.
    /// </summary>
    public static byte[] RgbIrreversible => Convert.FromBase64String(
        "/0//UQAvAAAAAAAFAAAAAwAAAAAAAAAAAAAABQAAAAMAAAAAAAAAAAADBwEBBwEBBwEB/1IADAAAAAEBAQQEAAD/XAALQkgkV9NX01di/2QAJQABQ3JlYXRlZCBieSBPcGVuSlBFRyB2ZXJzaW9uID" +
        "IuNS40/5AACgAAAAAAaQAB/5PH2g4LSpbrMzMrx9oOBLX1b1+2ccfaDhBkQc1GjgvH4A0D5wkD5wQO0FO8at8L1sK/ApLD7QWD6gOB84ILrZYvtwOWFgK5w+0Gh9oJA+cEDjdAck9/DI4ddwEY/9k=");

    /// <summary>
    /// A codestream that uses most of the syntax: 13x11, 8-bit RGB, reversible, in four tiles of 8x8 whose tile-parts hold
    /// one resolution level each, two layers in the RPCL order, precincts of 8x8 and 4x4, code-blocks of 4x4 with every
    /// code-block style, SOP and EPH markers, PLT and TLM marker segments. <c>opj_compress -i tiled.ppm -o tiled.j2k -n 2
    /// -t 8,8 -TP R -p RPCL -r 4,1 -c [8,8],[4,4] -b 4,4 -SOP -EPH -PLT -TLM -M 63</c>, where sample <c>i</c> of the PPM
    /// file is <c>(i * 37 + 11) mod 256</c>.
    /// </summary>
    public static byte[] Tiled => Convert.FromBase64String(
        "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" +
        "YVtv/Z");

    /// <summary>
    /// A codestream of three components whose last two are sub-sampled by two in both directions: 6x4, 8 bits, reversible.
    /// <c>opj_compress -i ycc.raw -o ycc.j2k -F 6,4,3,8,u@1x1:2x2:2x2 -n 2 -mct 0</c>, where the raw file holds the 24 samples
    /// <c>(i * 37 + 11) mod 256</c> of the first component, then the 6 samples <c>(i * 53 + 7) mod 256</c> of the second one
    /// and the 6 samples <c>(i * 71 + 19) mod 256</c> of the third one.
    /// </summary>
    public static byte[] Subsampled => Convert.FromBase64String(
        "/0//UQAvAAAAAAAGAAAABAAAAAAAAAAAAAAABgAAAAQAAAAAAAAAAAADBwEBBwICBwIC/1IADAAAAAEAAQQEAAH/XAAHQEBISFD/ZAAlAAFDcmVhdGVkIGJ5IE9wZW5KUEVHIHZlcnNpb24gMi41Lj" +
        "T/kAAKAAAAAABbAAH/k8+0IAm48WWvf3VHw+cGC8Wfx9QGBf1fx9oNPwB4/AEAB4yYW5PjD6T1vr4yywjibxHH2gM/ADj8AEAEA4YYBMfaAz8AOPwAQAcC8iMH/9k=");

    /// <summary>
    /// A codestream: 5x3, 4-bit grayscale, reversible. <c>opj_compress -i gray4.raw -o gray4.j2k -F 5,3,1,4,u -n 2</c>, where
    /// sample <c>i</c> of the raw file is <c>(i * 7 + 3) mod 16</c>.
    /// </summary>
    public static byte[] Gray4 => Convert.FromBase64String(
        "/0//UQApAAAAAAAFAAAAAwAAAAAAAAAAAAAABQAAAAMAAAAAAAAAAAABAwEB/1IADAAAAAEAAQQEAAH/XAAHQCAoKDD/ZAAlAAFDcmVhdGVkIGJ5IE9wZW5KUEVHIHZlcnNpb24gMi41LjT/kAAKAA" +
        "AAAAAmAAH/k8+EUBFe1f9/z5AcfCHHyAgEPG4CuH8DT//Z");

    /// <summary>
    /// A codestream: 5x3, 12-bit grayscale, reversible. <c>opj_compress -i gray12.raw -o gray12.j2k -F 5,3,1,12,u -n 2</c>,
    /// where sample <c>i</c> of the raw file (big-endian) is <c>(i * 331 + 5) mod 4096</c>.
    /// </summary>
    public static byte[] Gray12 => Convert.FromBase64String(
        "/0//UQApAAAAAAAFAAAAAwAAAAAAAAAAAAAABQAAAAMAAAAAAAAAAAABCwEB/1IADAAAAAEAAQQEAAH/XAAHQGBoaHD/ZAAlAAFDcmVhdGVkIGJ5IE9wZW5KUEVHIHZlcnNpb24gMi41LjT/kAAKAA" +
        "AAAAAuAAH/k8/kLAmfjyo3CpiaWAwfx/IHP8AkP5AgEIvrDYPlLwuL/9k=");

    /// <summary>
    /// A codestream of signed samples: 5x3, 8 bits, reversible. <c>opj_compress -i signed.raw -o signed.j2k -F 5,3,1,8,s
    /// -n 2</c>.
    /// </summary>
    public static byte[] Signed => Convert.FromBase64String(
        "/0//UQApAAAAAAAFAAAAAwAAAAAAAAAAAAAABQAAAAMAAAAAAAAAAAABhwEB/1IADAAAAAEAAQQEAAH/XAAHQEBISFD/ZAAlAAFDcmVhdGVkIGJ5IE9wZW5KUEVHIHZlcnNpb24gMi41LjT/kAAKAA" +
        "AAAAArAAH/k8+0IAyPhk4qF/y/x9oJH2gcPtAgC6ZfTwVwHwEv/9k=");

    /// <summary>
    /// A codestream of 2-bit samples, to be used as the indices of a palette: 5x3, reversible. <c>opj_compress -i index.raw
    /// -o index.j2k -F 5,3,1,2,u -n 2</c>, where sample <c>i</c> of the raw file is <c>(i * 3 + 1) mod 4</c>.
    /// </summary>
    public static byte[] Index => Convert.FromBase64String(
        "/0//UQApAAAAAAAFAAAAAwAAAAAAAAAAAAAABQAAAAMAAAAAAAAAAAABAQEB/1IADAAAAAEAAQQEAAH/XAAHQBAYGCD/ZAAlAAFDcmVhdGVkIGJ5IE9wZW5KUEVHIHZlcnNpb24gMi41LjT/kAAKAA" +
        "AAAAAeAAH/k90MB4bfzoURR0ILewp/Ax//2Q==");

    /// <summary>
    /// A codestream of four 8-bit components without component transformation: 5x3, reversible. <c>opj_compress -i cmyk.raw
    /// -o cmyk.j2k -F 5,3,4,8,u -n 2 -mct 0</c>, where sample <c>i</c> of each component of the raw file is
    /// <c>(i * 37 + 11) mod 256</c>, <c>(i * 41 + 3) mod 256</c>, <c>(i * 43 + 5) mod 256</c> and <c>(i * 13 + 2) mod 256</c>.
    /// </summary>
    public static byte[] FourComponents => Convert.FromBase64String(
        "/0//UQAyAAAAAAAFAAAAAwAAAAAAAAAAAAAABQAAAAMAAAAAAAAAAAAEBwEBBwEBBwEBBwEB/1IADAAAAAEAAQQEAAH/XAAHQEBISFD/ZAAlAAFDcmVhdGVkIGJ5IE9wZW5KUEVHIHZlcnNpb24gMi" +
        "41LjT/kAAKAAAAAABsAAH/k8+0HApleb99J1/PtBwKamFoO04f34BAC4q0TYnj3O/PtBwGhbAvzslDx9oHPwBI/ACAAh01AyFJfwMDx9oHPwA4/ACAAg0VAyW9A1fH2gcPqA4/ACAHb7cAGr8Gj4D/" +
        "2Q==");

    /// <summary>
    /// A codestream coded with the high-throughput block coder: 5x3, 8-bit RGB, reversible. <c>ojph_compress -i ht.ppm
    /// -o ht.j2c -reversible true -num_decomps 2</c>, with the PPM file of <see cref="Rgb"/>.
    /// </summary>
    public static byte[] HighThroughputRgb => Convert.FromBase64String(
        "/0//UQAvQAAAAAAFAAAAAwAAAAAAAAAAAAAABQAAAAMAAAAAAAAAAAADBwEBBwEBBwEB/1AACAACAAAAA/9SAAwAAgABAQIEBEAB/1wACiBQWFhYUFBQ/2QAFwABT3BlbkpQSCBWZXIgMC4zMi4wLv" +
        "+QAAoAAAAAALgAAf+TwBIA5AozAMATAAj9AAF0AMATAGnqACI0AMAJYATQAlDvAAd0AKrZAQF0AP4AB3QAwAlgBNACQP4AB3QAf/4BgXQAAQd0AKAE0AJA/38AIjQAAYd0AMAVEATwBKDurh4eAGOU" +
        "AO7+AFPRdQCeAIF0AMAVMAVEASh+/nx//gBhlAC+5/sBE9F1AP4BAXQAwBUQBUwBKL8+Pz8AYpQAPn/fAAgA9jYA/gAiNAD/2Q==");

    /// <summary>
    /// A codestream coded with the high-throughput block coder: 5x3, 8-bit grayscale, reversible. <c>ojph_compress
    /// -i htgray.pgm -o htgray.j2c -reversible true -num_decomps 1</c>, with the PGM file of <see cref="Gray"/>.
    /// </summary>
    public static byte[] HighThroughputGray => Convert.FromBase64String(
        "/0//UQApQAAAAAAFAAAAAwAAAAAAAAAAAAAABQAAAAMAAAAAAAAAAAABBwEB/1AACAACAAAAAf9SAAwAAgABAAEEBEAB/1wAByBISEhI/2QAFwABT3BlbkpQSCBWZXIgMC4zMi4wLv+QAAoAAAAAAD" +
        "UAAf+TwCqAKbgil+sgg381AMAqIBPAJX7/T+8AQ5QAft8Bg9F1AP4BAXQA/9k=");

    // The codestreams below have one code-block of the high-throughput block coder, one gray component of 8 bits and
    // no wavelet transformation, so that a sample is its coefficient plus 128. No encoder writes their coding passes:
    // their HT cleanup segment is the one "ojph_compress -i half.pgm -o half.j2c -reversible true -num_decomps 0"
    // writes for the image of the coefficients halved toward zero (plus 128), and their packet headers and their HT
    // refinement segment are written by hand from Rec. ITU-T T.814 (Annex B, 7.1.5, 7.1.6, 7.4 and 7.5). The
    // coefficients that were coded are given with each codestream; the decoders that read them are named.

    /// <summary>
    /// 7x6, an HT cleanup pass, an HT SigProp pass and an HT MagRef pass in one packet. Decoded to
    /// <see cref="HighThroughputRefinedSamples"/> by ojph_expand 0.32.0, OpenJPEG 2.5.4 and FFmpeg 9.0.2.
    /// </summary>
    public static byte[] HighThroughputRefined => Convert.FromBase64String(
        "/0//UQApQAAAAAAHAAAABgAAAAAAAAAAAAAABwAAAAYAAAAAAAAAAAABBwEB/1AACAACAAAAAP9SAAwAAgABAAAEBEAB/1wABCA4/2QAFwABT3BlbkpQSCBWZXIgMC4zMi4wLv+QAAoAAAAAACkAAf" +
        "+TwcvSIhNljQUj8fC6e4d13nsA50uQKQVcAQAP/9k=");

    /// <summary>The coefficients coded in <see cref="HighThroughputRefined"/>, plus 128.</summary>
    public static byte[] HighThroughputRefinedSamples => ToSamples(
        [-1, 1, 0, 1, 0, 0, 0, 13, 7, 30, 1, 22, 1, 0, 7, 0, -1, 1, -1, 1, -1, 1, -11, 1, 0, 0, -6, 0, 0, 0, 0, -18, 0, -1, -1, 1, -14, 0, 0, 0, 1, 0]);

    /// <summary>
    /// 7x6, an HT cleanup pass and an HT SigProp pass: the last bit of the coefficients the HT cleanup pass coded is not
    /// in the codestream. ojph_expand 0.32.0, OpenJPEG 2.5.4 and FFmpeg 9.0.2 decode them to the middle of the two
    /// possible values, away from zero: <see cref="HighThroughputSignificancePropagationSamples"/>.
    /// </summary>
    public static byte[] HighThroughputSignificancePropagation => Convert.FromBase64String(
        "/0//UQApQAAAAAAHAAAABgAAAAAAAAAAAAAABwAAAAYAAAAAAAAAAAABBwEB/1AACAACAAAAAP9SAAwAAgABAAAEBEAB/1wABCA4/2QAFwABT3BlbkpQSCBWZXIgMC4zMi4wLv+QAAoAAAAAACsAAf" +
        "+TwbWIHPIkbzm2GguOGX/eqbl/7/RcI8PdAFFqgAf/2Q==");

    /// <summary>
    /// The coefficients coded in <see cref="HighThroughputSignificancePropagation"/>, plus 128: those of a magnitude of 2
    /// or more have their last bit replaced by the middle of its range (an even magnitude becomes the next odd one).
    /// </summary>
    public static byte[] HighThroughputSignificancePropagationSamples => ToSamples(
        [-1, 0, 0, -1, -3, 7, 0, 0, 31, 1, 0, 0, 1, 0, -11, 21, -31, 21, 1, 0, -5, -25, 31, 0, 0, -21, 25, 0, 0, -7, 11, -1, 3, 0, 31, 0, -15, -1, -7, -25, 0, 0]);

    /// <summary>
    /// 5x9, the three coding passes with the vertically causal contexts (the bit 3 of the code-block style): the
    /// significance of the next stripe is not used by the HT SigProp pass, which reads other bits without it. Decoded to
    /// <see cref="HighThroughputCausalSamples"/> by ojph_expand 0.32.0, OpenJPEG 2.5.4 and FFmpeg 9.0.2.
    /// </summary>
    public static byte[] HighThroughputCausal => Convert.FromBase64String(
        "/0//UQApQAAAAAAFAAAACQAAAAAAAAAAAAAABQAAAAkAAAAAAAAAAAABBwEB/1AACAACAAAAAP9SAAwAAgABAAAEBEgB/1wABCA4/2QAFwABT3BlbkpQSCBWZXIgMC4zMi4wLv+QAAoAAAAAADgAAf" +
        "+Twc3iALLqdtqkTeqRWgTr6Rn9kTjx/nmF13b4XTup3c0yATBQ334AEnZZ/9k=");

    /// <summary>The coefficients coded in <see cref="HighThroughputCausal"/>, plus 128.</summary>
    public static byte[] HighThroughputCausalSamples => ToSamples(
        [0, 12, 25, 0, 0, 0, 0, -23, 0, 16, 0, 1, 22, 0, 20, -15, 16, 1, -15, 1, -1, 25, -1, -9, 20, -15, 4, -23, -1, -1, 0, -1, -1, -6, 5, 0, 1, -23, 0, 30, 1, 28, -17, 0, 22]);

    /// <summary>
    /// 7x6, six placeholder passes before the three coding passes: the packet header gives nine coding passes and two
    /// bit-planes less than <see cref="HighThroughputRefined"/> would. Decoded to <see cref="HighThroughputPlaceholdersSamples"/>
    /// by ojph_expand 0.32.0 and FFmpeg 9.0.2; OpenJPEG 2.5.4 refuses more than three coding passes.
    /// </summary>
    public static byte[] HighThroughputPlaceholders => Convert.FromBase64String(
        "/0//UQApQAAAAAAHAAAABgAAAAAAAAAAAAAABwAAAAYAAAAAAAAAAAABBwEB/1AACAACAAAAAP9SAAwAAgABAAAEBEAB/1wABCA4/2QAFwABT3BlbkpQSCBWZXIgMC4zMi4wLv+QAAoAAAAAAC4AAf" +
        "+Tx8akAIVBu6dUL+kJOMpjpxSPXQv4fX0AlqHJZmYCAUj/2Q==");

    /// <summary>The coefficients coded in <see cref="HighThroughputPlaceholders"/>, plus 128.</summary>
    public static byte[] HighThroughputPlaceholdersSamples => ToSamples(
        [0, 10, 1, 0, 0, 11, -1, -6, 1, 0, -11, -1, 1, 0, -14, 0, -24, 1, 0, 0, -6, -1, -25, 12, 6, -1, 0, 0, 1, -14, 1, -1, 1, 0, -14, 0, 0, -24, 6, 0, 1, -1]);

    /// <summary>
    /// 7x6, three layers: the HT cleanup pass, the HT SigProp pass and the HT MagRef pass are each in a packet, so that
    /// the HT refinement segment is in two pieces. Decoded to <see cref="HighThroughputLayersSamples"/> by FFmpeg 9.0.2;
    /// ojph_expand 0.32.0 refuses several layers and OpenJPEG 2.5.4 does not read the HT refinement segment of a later
    /// packet.
    /// </summary>
    public static byte[] HighThroughputLayers => Convert.FromBase64String(
        "/0//UQApQAAAAAAHAAAABgAAAAAAAAAAAAAABwAAAAYAAAAAAAAAAAABBwEB/1AACAACAAAAAP9SAAwAAgADAAAEBEAB/1wABCA4/2QAFwABT3BlbkpQSCBWZXIgMC4zMi4wLv+QAAoAAAAAADIAAf" +
        "+TwWqA3XrZUQUe+CAckc9Iv+If8DRqN34AwwC0g2J3OADBAB0B/9k=");

    /// <summary>The coefficients coded in <see cref="HighThroughputLayers"/>, plus 128.</summary>
    public static byte[] HighThroughputLayersSamples => ToSamples(
        [-31, 1, 0, -30, 0, -22, -8, 0, 0, -14, -1, 0, 24, -1, 0, 1, -1, 0, 0, -1, -1, 10, 1, 1, 0, 1, 0, 1, -6, 0, -1, 1, 0, -9, 0, 0, 0, -7, -10, 19, 0, 19]);

    /// <summary>
    /// 7x6, two layers: the second packet holds the HT SigProp and HT MagRef passes, then the HT cleanup pass of a second
    /// HT set, whose segment is empty (T.814, B.3, NOTE 2).
    /// </summary>
    public static byte[] HighThroughputSeveralSets => Convert.FromBase64String(
        "/0//UQApQAAAAAAHAAAABgAAAAAAAAAAAAAABwAAAAYAAAAAAAAAAAABBwEB/1AACAACAAAAAP9SAAwAAgACAAAEBEAB/1wABCA4/2QAFwABT3BlbkpQSCBWZXIgMC4zMi4wLv+QAAoAAAAAADEAAf" +
        "+TwWsAGHuXxwxo10j+EP8/7ZPS/176/3fdAPA4AAk3qGYaFp7/2Q==");

    /// <summary>
    /// 7x6, two layers: the first packet holds three placeholder passes and no byte, the second one the HT cleanup pass
    /// (T.814, B.3, NOTE 4).
    /// </summary>
    public static byte[] HighThroughputPlaceholdersAlone => Convert.FromBase64String(
        "/0//UQApQAAAAAAHAAAABgAAAAAAAAAAAAAABwAAAAYAAAAAAAAAAAABBwEB/1AACAACAAAAAP9SAAwAAgACAAAEBEAB/1wABCA4/2QAFwABT3BlbkpQSCBWZXIgMC4zMi4wLv+QAAoAAAAAACgAAf" +
        "+TwcAA2qAT1D2DzL7x9BHOkbx5vn+ans/tPQD/2Q==");

    private static byte[] ToSamples(ReadOnlySpan<int> coefficients)
    {
        var samples = new byte[coefficients.Length];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (byte)(coefficients[i] + 128);
        }

        return samples;
    }
}
