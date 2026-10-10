using System.Buffers.Binary;
using System.Text;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.TestHarness.WebP;

/// <summary>
/// A small reference WebP reader written from the WebP container specification (RFC 9649, section 2) for encoder tests
///. It never uses the library under test. It checks the container strictly: the RIFF size equals the
/// file length, every chunk fits and odd-sized chunks are followed by a zero padding byte; a simple file is one <c>VP8 </c>
/// or <c>VP8L</c> chunk; an extended file starts with <c>VP8X</c> (reserved bits clear, canvas of at most 2^32 - 1 pixels)
/// followed in order by an optional <c>ICCP</c>, an <c>ANIM</c> chunk exactly when the animation flag is set, the image
/// (<c>ANMF</c> frames, or an optional <c>ALPH</c> and the bitstream), then optional <c>EXIF</c> and <c>XMP </c> chunks; the
/// ICC, EXIF, XMP and animation flags match the chunks present; no unknown chunk; frame rectangles are inside the canvas
/// with even offsets; bitstream headers (VP8 key frame tag and start code, VP8L signature and version) declare the image
/// size. Lossless images and alpha planes are decoded with <see cref="ReferenceVp8L"/>; lossy (VP8) color is not decoded.
/// </summary>
/// <remarks>
/// The reader has no compositor: <see cref="DecodeFrame"/> decodes a frame rectangle, which is the displayed frame only for
/// full-canvas frames drawn without blending (the frames written by the library encoder).
/// </remarks>
public sealed class ReferenceWebP
{
    private ReferenceWebP(int width, int height, bool isExtended, byte flags, IReadOnlyList<ReferenceWebPChunk> chunks, IReadOnlyList<ReferenceWebPFrame> frames, ReferenceWebPAnimation? animation)
    {
        Width = width;
        Height = height;
        IsExtended = isExtended;
        Flags = flags;
        Chunks = chunks;
        Frames = frames;
        Animation = animation;
    }

    /// <summary>Gets the canvas width.</summary>
    public int Width { get; }

    /// <summary>Gets the canvas height.</summary>
    public int Height { get; }

    /// <summary>Gets a value indicating whether the file uses the extended layout (<c>VP8X</c>).</summary>
    public bool IsExtended { get; }

    /// <summary>Gets the <c>VP8X</c> flags (0 for a simple file): 0x20 ICC, 0x10 alpha, 0x08 EXIF, 0x04 XMP, 0x02 animation.</summary>
    public byte Flags { get; }

    /// <summary>Gets a value indicating whether the <c>VP8X</c> alpha flag is set.</summary>
    public bool HasAlphaFlag => (Flags & 0x10) != 0;

    /// <summary>Gets every top-level chunk in file order.</summary>
    public IReadOnlyList<ReferenceWebPChunk> Chunks { get; }

    /// <summary>Gets the still image (one entry) or the animation frames.</summary>
    public IReadOnlyList<ReferenceWebPFrame> Frames { get; }

    /// <summary>Gets the animation parameters, or <see langword="null"/> for a still image.</summary>
    public ReferenceWebPAnimation? Animation { get; }

    /// <summary>Gets the <c>ICCP</c> payload, or <see langword="null"/>.</summary>
    public ReadOnlyMemory<byte>? Icc => Find("ICCP");

    /// <summary>Gets the <c>EXIF</c> payload, or <see langword="null"/>.</summary>
    public ReadOnlyMemory<byte>? Exif => Find("EXIF");

    /// <summary>Gets the <c>XMP </c> payload, or <see langword="null"/>.</summary>
    public ReadOnlyMemory<byte>? Xmp => Find("XMP ");

    /// <summary>Parses and validates a WebP file.</summary>
    /// <param name="data">The file.</param>
    /// <returns>The reader.</returns>
    /// <exception cref="InvalidDataException">The file violates the container specification.</exception>
    public static ReferenceWebP Parse(ReadOnlySpan<byte> data)
    {
        Require(data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WEBP"u8), "missing RIFF/WEBP header");
        Require(BinaryPrimitives.ReadUInt32LittleEndian(data[4..]) == data.Length - 8, "the RIFF size is not the file length minus 8");
        var copy = data.ToArray();
        var chunks = ReadChunks(copy, 12, copy.Length);
        Require(chunks.Count > 0, "no chunk");
        if (chunks[0].FourCC != "VP8X")
        {
            Require(chunks.Count == 1 && chunks[0].FourCC is "VP8 " or "VP8L", "a simple file is a single VP8 or VP8L chunk");
            var image = ReadImage(chunks, x: 0, y: 0, durationMilliseconds: 0, alphaBlend: false, dispose: false);
            return new ReferenceWebP(image.Width, image.Height, isExtended: false, flags: 0, chunks, [image], animation: null);
        }

        var header = chunks[0].Data.Span;
        Require(header.Length == 10, "VP8X is not 10 bytes");
        var flags = header[0];
        Require((flags & 0xC1) == 0 && header[1] == 0 && header[2] == 0 && header[3] == 0, "VP8X reserved bits are set");
        var width = ReadUInt24(header[4..]) + 1;
        var height = ReadUInt24(header[7..]) + 1;
        Require((long)width * height <= uint.MaxValue, "the canvas has more than 2^32 - 1 pixels");

        var index = 1;
        var names = chunks.Select(chunk => chunk.FourCC).ToList();
        Require(names.Skip(1).All(name => name is "ICCP" or "ANIM" or "ANMF" or "ALPH" or "VP8 " or "VP8L" or "EXIF" or "XMP "), "unknown chunk");
        Require(names.Count(name => name == "ICCP") == ((flags & 0x20) != 0 ? 1 : 0), "the ICC flag does not match the ICCP chunk");
        Require(names.Count(name => name == "EXIF") == ((flags & 0x08) != 0 ? 1 : 0), "the EXIF flag does not match the EXIF chunk");
        Require(names.Count(name => name == "XMP ") == ((flags & 0x04) != 0 ? 1 : 0), "the XMP flag does not match the XMP chunk");
        if (index < chunks.Count && chunks[index].FourCC == "ICCP")
        {
            index++;
        }

        ReferenceWebPAnimation? animation = null;
        var frames = new List<ReferenceWebPFrame>();
        if ((flags & 0x02) != 0)
        {
            Require(index < chunks.Count && chunks[index].FourCC == "ANIM" && chunks[index].Data.Length == 6, "an animation needs a 6-byte ANIM chunk after ICCP");
            var anim = chunks[index].Data.Span;
            animation = new ReferenceWebPAnimation(BinaryPrimitives.ReadUInt32LittleEndian(anim), BinaryPrimitives.ReadUInt16LittleEndian(anim[4..]));
            index++;
            while (index < chunks.Count && chunks[index].FourCC == "ANMF")
            {
                var frame = chunks[index].Data;
                var span = frame.Span;
                Require(span.Length >= 16, "ANMF is shorter than 16 bytes");
                var x = 2 * ReadUInt24(span);
                var y = 2 * ReadUInt24(span[3..]);
                var frameWidth = ReadUInt24(span[6..]) + 1;
                var frameHeight = ReadUInt24(span[9..]) + 1;
                var duration = ReadUInt24(span[12..]);
                var frameFlags = span[15];
                Require((frameFlags & 0xFC) == 0, "ANMF reserved bits are set");
                Require(x + frameWidth <= width && y + frameHeight <= height, "a frame extends outside the canvas");
                var image = ReadImage(ReadChunks(copy, chunks[index].Offset + 8 + 16, chunks[index].Offset + 8 + frame.Length), x, y, duration, alphaBlend: (frameFlags & 0x02) == 0, dispose: (frameFlags & 0x01) != 0);
                Require(image.Width == frameWidth && image.Height == frameHeight, "the frame bitstream size differs from the ANMF size");
                frames.Add(image);
                index++;
            }

            Require(frames.Count > 0, "an animation has no frame");
            Require(names.All(name => name is not ("ALPH" or "VP8 " or "VP8L")), "an animation has an image chunk outside ANMF");
        }
        else
        {
            Require(!names.Contains("ANIM") && !names.Contains("ANMF"), "ANIM/ANMF without the animation flag");
            var start = index;
            if (index < chunks.Count && chunks[index].FourCC == "ALPH")
            {
                index++;
            }

            Require(index < chunks.Count && chunks[index].FourCC is "VP8 " or "VP8L", "the image chunk is missing or misplaced");
            index++;
            var image = ReadImage([.. chunks.Skip(start).Take(index - start)], x: 0, y: 0, durationMilliseconds: 0, alphaBlend: false, dispose: false);
            Require(image.Width == width && image.Height == height, "the bitstream size differs from the VP8X canvas");
            frames.Add(image);
        }

        if (index < chunks.Count && chunks[index].FourCC == "EXIF")
        {
            index++;
        }

        if (index < chunks.Count && chunks[index].FourCC == "XMP ")
        {
            index++;
        }

        Require(index == chunks.Count, "chunks are out of order (VP8X, ICCP, ANIM, image, EXIF, XMP)");
        return new ReferenceWebP(width, height, isExtended: true, flags, chunks, frames, animation);
    }

    /// <summary>Decodes the rectangle of a lossless frame (or of the still image) as straight RGBA.</summary>
    /// <param name="index">The frame index.</param>
    /// <returns>The pixels.</returns>
    /// <exception cref="NotSupportedException">The frame is lossy.</exception>
    public RawPixelBuffer DecodeFrame(int index)
    {
        var frame = Frames[index];
        if (!frame.IsLossless)
            throw new NotSupportedException("The reference reader does not decode lossy (VP8) color.");

        var (width, height, _, argb) = ReferenceVp8L.Decode(frame.Bitstream.Span);
        var rgba = new byte[width * height * 4];
        for (var i = 0; i < argb.Length; i++)
        {
            rgba[(4 * i) + 0] = (byte)(argb[i] >> 16);
            rgba[(4 * i) + 1] = (byte)(argb[i] >> 8);
            rgba[(4 * i) + 2] = (byte)argb[i];
            rgba[(4 * i) + 3] = (byte)(argb[i] >> 24);
        }

        return RawPixelBuffer.Create(width, height, RawPixelLayout.Rgba8, rgba);
    }

    /// <summary>Gets the VP8L alpha hint of a lossless frame.</summary>
    /// <param name="index">The frame index.</param>
    /// <returns>The hint.</returns>
    public bool GetLosslessAlphaHint(int index)
    {
        var frame = Frames[index];
        if (!frame.IsLossless)
            throw new InvalidOperationException("The frame is lossy.");

        return ((frame.Bitstream.Span[4] >> 4) & 1) == 1;
    }

    /// <summary>
    /// Decodes the alpha plane of a frame: the <c>ALPH</c> chunk of a lossy image (all 255 without one), or the alpha
    /// channel of a lossless image.
    /// </summary>
    /// <param name="index">The frame index.</param>
    /// <returns>One alpha value per pixel, row-major.</returns>
    public byte[] DecodeAlpha(int index)
    {
        var frame = Frames[index];
        if (frame.IsLossless)
        {
            var pixels = DecodeFrame(index).Span;
            var alpha = new byte[frame.Width * frame.Height];
            for (var i = 0; i < alpha.Length; i++)
            {
                alpha[i] = pixels[(4 * i) + 3];
            }

            return alpha;
        }

        if (frame.Alpha is not { } chunk)
            return Enumerable.Repeat(byte.MaxValue, frame.Width * frame.Height).ToArray();

        return DecodeAlphaChunk(chunk.Span, frame.Width, frame.Height);
    }

    /// <summary>Gets the <c>ALPH</c> header fields of a lossy frame: compression (0 none, 1 lossless), filter (0-3), preprocessing.</summary>
    /// <param name="index">The frame index.</param>
    /// <returns>The fields, or <see langword="null"/> without <c>ALPH</c> chunk.</returns>
    public (int Compression, int Filter, int Preprocessing)? GetAlphaHeader(int index)
        => Frames[index].Alpha is { } chunk ? (chunk.Span[0] & 3, (chunk.Span[0] >> 2) & 3, (chunk.Span[0] >> 4) & 3) : null;

    /// <summary>Decodes an <c>ALPH</c> payload (RFC 9649 section 2.7.1.2: header byte, raw or VP8L-compressed data, prediction filter).</summary>
    /// <param name="data">The chunk payload.</param>
    /// <param name="width">The image width.</param>
    /// <param name="height">The image height.</param>
    /// <returns>The alpha plane.</returns>
    public static byte[] DecodeAlphaChunk(ReadOnlySpan<byte> data, int width, int height)
    {
        Require(data.Length >= 1, "empty ALPH chunk");
        var header = data[0];
        Require((header >> 6) == 0, "ALPH reserved bits are set");
        var compression = header & 3;
        var filter = (header >> 2) & 3;
        byte[] values;
        if (compression == 0)
        {
            Require(data.Length - 1 >= width * height, "ALPH raw data is truncated");
            values = data.Slice(1, width * height).ToArray();
        }
        else
        {
            Require(compression == 1, "ALPH compression method is not 0 or 1");
            var argb = ReferenceVp8L.DecodeImageStream(data[1..], width, height);
            values = [.. argb.Select(pixel => (byte)(pixel >> 8))];
        }

        // Unfilter in raster order: each value is the residual plus its predictor (left, top or gradient)
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = (y * width) + x;
                int predictor;
                if (filter == 0)
                {
                    predictor = 0;
                }
                else if (x == 0 && y == 0)
                {
                    predictor = 0;
                }
                else if (y == 0)
                {
                    predictor = values[index - 1]; // the top row is predicted from the left for every filter
                }
                else if (x == 0)
                {
                    predictor = values[index - width]; // the left column is predicted from above for every filter
                }
                else
                {
                    var left = values[index - 1];
                    var top = values[index - width];
                    var topLeft = values[index - width - 1];
                    predictor = filter switch
                    {
                        1 => left,
                        2 => top,
                        _ => Math.Clamp(left + top - topLeft, 0, 255),
                    };
                }

                values[index] = (byte)(values[index] + predictor);
            }
        }

        return values;
    }

    private ReadOnlyMemory<byte>? Find(string fourCC) => Chunks.FirstOrDefault(chunk => chunk.FourCC == fourCC)?.Data;

    private static ReferenceWebPFrame ReadImage(List<ReferenceWebPChunk> chunks, int x, int y, int durationMilliseconds, bool alphaBlend, bool dispose)
    {
        ReadOnlyMemory<byte>? alpha = null;
        var index = 0;
        if (chunks.Count > 0 && chunks[0].FourCC == "ALPH")
        {
            alpha = chunks[0].Data;
            index++;
        }

        Require(index == chunks.Count - 1 && chunks[index].FourCC is "VP8 " or "VP8L", "an image is an optional ALPH chunk and one VP8 or VP8L chunk");
        var bitstream = chunks[index].Data;
        var span = bitstream.Span;
        int width;
        int height;
        if (chunks[index].FourCC == "VP8L")
        {
            Require(alpha is null, "a VP8L image has no ALPH chunk");
            Require(span.Length >= 5 && span[0] == 0x2F, "bad VP8L signature");
            var bits = BinaryPrimitives.ReadUInt32LittleEndian(span[1..]);
            width = (int)(bits & 0x3FFF) + 1;
            height = (int)((bits >> 14) & 0x3FFF) + 1;
            Require((bits >> 29) == 0, "the VP8L version is not 0");
        }
        else
        {
            Require(span.Length >= 10, "the VP8 chunk is shorter than its frame header");
            var tag = span[0] | (span[1] << 8) | (span[2] << 16);
            Require((tag & 1) == 0, "the VP8 frame is not a key frame");
            Require(((tag >> 1) & 7) <= 3, "the VP8 version is above 3");
            Require(((tag >> 4) & 1) == 1, "the VP8 frame is not shown");
            Require((tag >> 5) <= span.Length - 10, "the VP8 first partition extends past the chunk");
            Require(span[3] == 0x9D && span[4] == 0x01 && span[5] == 0x2A, "bad VP8 start code");
            var w = BinaryPrimitives.ReadUInt16LittleEndian(span[6..]);
            var h = BinaryPrimitives.ReadUInt16LittleEndian(span[8..]);
            Require((w >> 14) == 0 && (h >> 14) == 0, "VP8 upscaling is not used by WebP");
            width = w & 0x3FFF;
            height = h & 0x3FFF;
            Require(width > 0 && height > 0, "empty VP8 frame");
        }

        return new ReferenceWebPFrame(x, y, width, height, durationMilliseconds, alphaBlend, dispose, chunks[index].FourCC == "VP8L", alpha, bitstream);
    }

    private static List<ReferenceWebPChunk> ReadChunks(byte[] data, int offset, int end)
    {
        var chunks = new List<ReferenceWebPChunk>();
        while (offset < end)
        {
            Require(end - offset >= 8, "truncated chunk header");
            var fourCC = Encoding.ASCII.GetString(data, offset, 4);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 4));
            Require(size <= (uint)(end - offset - 8), $"chunk '{fourCC}' extends past its container");
            chunks.Add(new ReferenceWebPChunk(fourCC, offset, data.AsMemory(offset + 8, (int)size)));
            offset += 8 + (int)size;
            if ((size & 1) != 0)
            {
                Require(offset < end && data[offset] == 0, $"chunk '{fourCC}' is not followed by a zero padding byte");
                offset++;
            }
        }

        return chunks;
    }

    private static int ReadUInt24(ReadOnlySpan<byte> data) => data[0] | (data[1] << 8) | (data[2] << 16);

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidDataException("WebP: " + message + ".");
    }
}
