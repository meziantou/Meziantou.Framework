using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Small JPEG XL files written by the reference tools (libjxl 0.12.0, the version pinned by Meziantou.Prebuilt 3.3.0), with
/// the command that produced each of them. Their content is described by <c>jxlinfo</c> and by the input given to the
/// tool: the tests never derive an expectation from the library.
/// </summary>
internal static class JpegXlTestFiles
{
    /// <summary>The XMP packet of the files that have one (38 bytes).</summary>
    public static ReadOnlySpan<byte> XmpPacket => "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"/>\n"u8;

    /// <summary>
    /// A bare codestream: 5x3, 8-bit RGB, lossless Modular. <c>cjxl rgb.ppm rgb.jxl -d 0 --num_threads=0</c>, where sample
    /// <c>i</c> of the PPM file (row by row, R then G then B) is <c>(i * 37 + 11) mod 256</c>.
    /// </summary>
    public static byte[] Rgb => Convert.FromBase64String("/woQUBBQXAgIAAEAHAFLGJOOg4OkuC58v7k/iInS8vaO449OCZkXiAeAwGdWfv3KAgKO8+CkgN2m7TZNQMBxwkkBkYqUgIBz3D+PC4zWW7n63WqpAw==");

    /// <summary>
    /// A container with one <c>jxlc</c> box: 5x3, 8-bit RGB with alpha, lossless. <c>cjxl rgba.pam rgba.jxl -d 0
    /// --container=1 --num_threads=0</c>, where sample <c>i</c> of the PAM file (R, G, B, A) is <c>(i * 37 + 11) mod 256</c>.
    /// </summary>
    public static byte[] RgbaContainer => Convert.FromBase64String(
        "AAAADEpYTCANCocKAAAAFGZ0eXBqeGwgAAAAAGp4bCAAAABpanhsY/8KEFCwoLgQCAAQAEwBSxiTjoODGUiUloAbywOt8wAABGV4rQvIA1C/5wd6Dt41+F3PD/Q0hCGiRFn9cPUwohjCEFGiEGX14d" +
        "WHDWGI1YdXHyYKUaKGMMQQIG7UIBQ3KQA=");

    /// <summary>
    /// A container with a level box (<c>jxll</c>) and one <c>jxlc</c> box: 5x3, 16-bit grayscale, lossless.
    /// <c>cjxl gray16.pgm gray16.jxl -d 0 --num_threads=0</c>, where sample <c>i</c> is <c>(i * 3701 + 11) mod 65536</c>.
    /// </summary>
    public static byte[] Gray16Container => Convert.FromBase64String("AAAADEpYTCANCocKAAAAFGZ0eXBqeGwgAAAAAGp4bCAAAAAJanhsbAoAAAA9anhsY/8KEFD8AMWFCAABAJwASxiLFQESHhh0Y/xg0l1TiE4Z6vw6ukH4S7AZ0ojOLurcrOFrNhYH");

    /// <summary>
    /// A bare codestream written by <c>jxl_from_tree</c>: a 4x4 animation at 10 ticks per second that repeats forever, of
    /// three coded frames. The first one (3 ticks) fills the image with 100; the second one has no duration: it is a layer
    /// that adds 20 to the 2x2 samples at (0, 0); the third one (5 ticks) replaces the 2x2 samples at (2, 2) with 200.
    /// </summary>
    /// <remarks>
    /// <code>
    /// Width 4  Height 4  Bitdepth 8  Animation  AnimationFPS 10 1  Duration 3  NotLast  - Set 100
    /// Width 2  Height 2  FramePos 0 0  BlendMode kAdd  Duration 0  NotLast  - Set 20
    /// FramePos 2 2  BlendMode kReplace  Duration 5  - Set 200
    /// </code>
    /// </remarks>
    public static byte[] LayeredAnimation => Convert.FromBase64String("/woYEEETAIAJCAYOCAAAHABLOChAIhEMCCYAACCAQCEAABwASygWTIRgAAgmBBAggABZEAAcAEs4KUhCIhg=");

    /// <summary>
    /// A container whose codestream is split in two <c>jxlp</c> boxes around an <c>xml </c> box (<see cref="XmpPacket"/>):
    /// the first part holds the image header only. The image is the one of <see cref="Rgb"/>.
    /// <c>cjxl rgb.ppm meta.jxl -d 0 -x xmp=x.xml --compress_boxes=0 --num_threads=0</c>.
    /// </summary>
    public static byte[] SplitCodestreamWithXmp => Convert.FromBase64String(
        "AAAADEpYTCANCocKAAAAFGZ0eXBqeGwgAAAAAGp4bCAAAAAUanhscAAAAAD/ChBQEFBcCAAAAC54bWwgPHg6eG1wbWV0YSB4bWxuczp4PSJhZG9iZTpuczptZXRhLyIvPgoAAABZanhscIAAAAEIAA" +
        "EAHAFLGJOOg4OkuC58v7k/iInS8vaO449OCZkXiAeAwGdWfv3KAgKO8+CkgN2m7TZNQMBxwkkBkYqUgIBz3D+PC4zWW7n63WqpAw==");

    /// <summary>
    /// Like <see cref="SplitCodestreamWithXmp"/> for the image of <see cref="RgbaContainer"/>, the XMP packet being
    /// compressed in a <c>brob</c> box. <c>cjxl rgba.pam rgba-meta.jxl -d 0 -x xmp=x.xml --compress_boxes=1 --num_threads=0</c>.
    /// </summary>
    public static byte[] SplitCodestreamWithCompressedXmp => Convert.FromBase64String(
        "AAAADEpYTCANCocKAAAAFGZ0eXBqeGwgAAAAAGp4bCAAAAAUanhscAAAAAD/ChBQsKC4EAAAADZicm9ieG1sIIsSgDx4OnhtcG1ldGEgeG1sbnM6eD0iYWRvYmU6bnM6bWV0YS8iLz4KAwAAAGVqeG" +
        "xwgAAAAQgAEABMAUsYk46DgxlIlJaAG8sDrfMAAARleK0LyANQv+cHeg7eNfhdzw/0NIQhokRZ/XD1MKIYwhBRohBl9eHVhw1hiNWHVx8mClGihjDEECBu1CAUNykA");

    /// <summary>
    /// A bare codestream written by <c>jxl_from_tree</c>: 3x2, 12-bit RGB with alpha, every sample equal to 7, and the
    /// orientation 6 (rotated 90 degrees clockwise for display).
    /// <c>Width 3  Height 2  Bitdepth 12  Orientation 6  Alpha  - Set 7</c>.
    /// </summary>
    public static byte[] Oriented12Bit => Convert.FromBase64String("/woIQAsKBCYIGBAAGABLKBRIIxg=");

    /// <summary>
    /// The image of <see cref="Rgb"/>, tagged with the color space of Adobe RGB (1998): custom primaries, the D65 white
    /// point and a power function. <c>cjxl rgb.ppm adobe.jxl -d 0 -x "color_space=RGB_D65_0.64;0.33;0.21;0.71;0.15;0.06_Rel_g0.4547" --num_threads=0</c>.
    /// </summary>
    public static byte[] CustomPrimariesAndGamma => Convert.FromBase64String(
        "/woQUBCQIADiBCIhQNEsuGoBPkmAqRM3rCgCCAABABwBSxiTjoODpLgufL+5P4iJ0vL2juOPTgmZF4gHgMBnVn79ygICjvPgpIDdpu02TUDAccJJAZGKlICAc9w/jwuM1lu5+t1qqQM=");

    /// <summary>
    /// 5x3, 8-bit grayscale, lossless, with a custom white point (x = 0.34, y = 0.35), a power function of exponent 0.5 and
    /// the perceptual rendering intent. <c>cjxl gray.pgm gamma-gray.jxl -d 0 -x "color_space=Gra_0.34;0.35_Per_g0.5" --num_threads=0</c>,
    /// where sample <c>i</c> of the PGM file is <c>(i * 37 + 11) mod 256</c>.
    /// </summary>
    public static byte[] GrayCustomWhitePointAndGamma => Convert.FromBase64String("/woQUBAkBASmwFwVaIkJAggAAQCAAEsYixVSXBd0YyG9GJLMJoJwECcihJBl+oTqW10uU28A");

    /// <summary>
    /// The image of <see cref="Rgb"/>, tagged with the P3 primaries, the white point E, the transfer function of BT.709 and
    /// the saturation rendering intent (as <c>jxlinfo</c> reports them).
    /// <c>cjxl rgb.ppm e.jxl -d 0 -x "color_space=RGB_0.3333;0.3333_DCI_Sat_709" --num_threads=0</c>.
    /// </summary>
    public static byte[] WhitePointE => Convert.FromBase64String("/woQUBAgmhIICAABABwBSxiTjoODpLgufL+5P4iJ0vL2juOPTgmZF4gHgMBnVn79ygICjvPgpIDdpu02TUDAccJJAZGKlICAc9w/jwuM1lu5+t1qqQM=");

    /// <summary>
    /// The image of <see cref="Rgb"/>, tagged with the Rec. 2100 primaries, the white point E and the PQ transfer function
    /// (as <c>jxlinfo</c> reports them). <c>cjxl rgb.ppm pq.jxl -d 0 -x "color_space=RGB_0.3333;0.3333_202_Rel_PeQ" --num_threads=0</c>.
    /// </summary>
    public static byte[] PqWithWhitePointE => Convert.FromBase64String("/woQUAEEiB494nAAAAAACAgAAQAcAUsYk46Dg6S4Lny/uT+IidLy9o7jj04JmReIB4DAZ1Z+/coCAo7z4KSA3abtNk1AwHHCSQGRipSAgHPcP48LjNZbufrdaqkD");

    /// <summary>
    /// A bare codestream written by <c>jxl_from_tree</c>: a 4x4 still image of two coded frames. The first one fills the
    /// image with 100 and is not shown; the second one, of 6x6 samples at (-1, -1), adds 20 to it: <c>djxl</c> gives 120
    /// for every sample.
    /// </summary>
    /// <remarks>
    /// <code>
    /// Width 4  Height 4  Bitdepth 8  NotLast  - Set 100
    /// Width 6  Height 6  FramePos -1 -1  BlendMode kAdd  - Set 20
    /// </code>
    /// </remarks>
    public static byte[] FrameLargerThanTheImage => Convert.FromBase64String("/woYEAAJCAYCABwASzgoQCIRDAgmAQRggEEFABwASygWTIRgAA==");

    /// <summary>Wraps a bare codestream in a container, in <c>jxlp</c> boxes that end at the given offsets of the codestream.</summary>
    /// <param name="codestream">The codestream.</param>
    /// <param name="splits">The offsets where a part ends, in increasing order; a last part holds the rest.</param>
    /// <returns>The container.</returns>
    public static byte[] WrapInParts(ReadOnlySpan<byte> codestream, params int[] splits)
    {
        using var stream = new MemoryStream();
        stream.Write(ContainerStart);
        var start = 0;
        for (var i = 0; i <= splits.Length; i++)
        {
            var end = i < splits.Length ? splits[i] : codestream.Length;
            var index = (uint)i | (i == splits.Length ? 0x80000000 : 0);
            Span<byte> payload = new byte[4 + end - start];
            BinaryPrimitives.WriteUInt32BigEndian(payload, index);
            codestream[start..end].CopyTo(payload[4..]);
            stream.Write(Box("jxlp", payload));
            start = end;
        }

        return stream.ToArray();
    }

    /// <summary>Gets the signature box and the file type box that start every container.</summary>
    public static byte[] ContainerStart => [0x00, 0x00, 0x00, 0x0C, 0x4A, 0x58, 0x4C, 0x20, 0x0D, 0x0A, 0x87, 0x0A, .. Box("ftyp", "jxl \0\0\0\0jxl "u8)];

    /// <summary>Builds a box: its size, its type and its payload.</summary>
    public static byte[] Box(string type, ReadOnlySpan<byte> payload)
    {
        var box = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        System.Text.Encoding.ASCII.GetBytes(type, box.AsSpan(4));
        payload.CopyTo(box.AsSpan(8));
        return box;
    }
}
