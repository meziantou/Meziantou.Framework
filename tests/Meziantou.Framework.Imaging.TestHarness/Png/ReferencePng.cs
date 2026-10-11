using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.TestHarness.Png;

/// <summary>
/// A small reference PNG reader written from the W3C PNG specification for encoder tests. It never
/// uses the library under test: it checks the container strictly (signature, chunk lengths, a bitwise CRC-32 of every chunk,
/// IHDR first with legal fields, consecutive IDAT chunks, IEND last with nothing after it, critical chunks limited to IHDR,
/// IDAT and IEND), and decodes the non-palette 8- and 16-bit color types (inflation by the BCL, the five filters, Adam7) to
/// raw buffers in explicit layouts. Metadata fields are read with <see cref="Fixtures.EncodedFieldInspector"/>.
/// </summary>
/// <remarks>
/// APNG files (an <c>acTL</c> chunk) are validated against the APNG rules of the PNG specification (third edition, section
/// 11.3.6 and the APNG frame rules): one <c>acTL</c> before the first <c>IDAT</c> with a frame count of 1 to 2^31 - 1 and a
/// play count of at most 2^31 - 1; <c>fcTL</c> and <c>fdAT</c> sequence numbers consecutive from 0 across both chunk types;
/// at most one <c>fcTL</c> before <c>IDAT</c>, covering the canvas at offset 0; frame regions non-empty and inside the
/// canvas; legal dispose and blend operations; every <c>fcTL</c> followed by its image data (the <c>IDAT</c> run before
/// <c>IDAT</c>, at least one <c>fdAT</c> after it); no <c>fdAT</c> before <c>IDAT</c>; exactly <c>num_frames</c> frames.
/// Each frame datastream is decoded independently at its region size (<see cref="DecodeFrame"/>). Displayed frames are only
/// produced for full-canvas <c>SOURCE</c> frames (<see cref="DecodeDisplayedFrame"/>), whose displayed image is the frame
/// data whatever the previous frames and disposals: the reader deliberately has no compositor, so it checks encoder
/// output without depending on compositing.
/// </remarks>
public sealed class ReferencePng
{
    private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly (int X0, int Y0, int Dx, int Dy)[] Adam7 = [(0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2)];

    private ReferencePng(int width, int height, byte bitDepth, byte colorType, byte interlaceMethod, IReadOnlyList<ReferencePngChunk> chunks, ReferenceApngAnimation? animation)
    {
        Width = width;
        Height = height;
        BitDepth = bitDepth;
        ColorType = colorType;
        InterlaceMethod = interlaceMethod;
        Chunks = chunks;
        Animation = animation;
    }

    /// <summary>Gets the validated APNG control data, or <see langword="null"/> for a static PNG (no <c>acTL</c>).</summary>
    public ReferenceApngAnimation? Animation { get; }

    public int Width { get; }

    public int Height { get; }

    public byte BitDepth { get; }

    /// <summary>Gets the IHDR color type: 0 gray, 2 RGB, 4 gray+alpha, 6 RGBA.</summary>
    public byte ColorType { get; }

    /// <summary>Gets the IHDR interlace method: 0 none, 1 Adam7.</summary>
    public byte InterlaceMethod { get; }

    /// <summary>Gets every chunk in file order (IHDR and IEND included).</summary>
    public IReadOnlyList<ReferencePngChunk> Chunks { get; }

    /// <summary>Gets the chunk types in file order.</summary>
    public IReadOnlyList<string> ChunkTypes => [.. Chunks.Select(chunk => chunk.Type)];

    /// <summary>Gets the raw layout <see cref="DecodePixels"/> produces.</summary>
    public RawPixelLayout Layout => (ColorType, BitDepth) switch
    {
        (0, 8) => RawPixelLayout.Gray8,
        (0, 16) => RawPixelLayout.Gray16Le,
        (2, 8) => RawPixelLayout.Rgb8,
        (4, 8) => RawPixelLayout.GrayAlpha8,
        (4, 16) => RawPixelLayout.GrayAlpha16Le,
        (6, 8) => RawPixelLayout.Rgba8,
        (6, 16) => RawPixelLayout.Rgba16Le,
        _ => throw new NotSupportedException($"The reference reader does not decode color type {ColorType} with bit depth {BitDepth}."),
    };

    /// <summary>Parses and validates the container structure of a PNG file.</summary>
    /// <param name="data">The file.</param>
    /// <returns>The parsed file.</returns>
    /// <exception cref="InvalidDataException">The structure is invalid.</exception>
    public static ReferencePng Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8 || !data[..8].SequenceEqual(Signature))
            throw new InvalidDataException("Missing PNG signature.");

        var chunks = new List<ReferencePngChunk>();
        var offset = 8;
        var idatEnded = false;
        while (true)
        {
            if (data.Length - offset < 12)
                throw new InvalidDataException($"Truncated chunk at offset {offset}.");

            var length = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
            if (length > int.MaxValue || length > data.Length - offset - 12)
                throw new InvalidDataException($"Invalid chunk length {length} at offset {offset}.");

            var typeBytes = data.Slice(offset + 4, 4);
            foreach (var c in typeBytes)
            {
                if (c is not ((>= (byte)'A' and <= (byte)'Z') or (>= (byte)'a' and <= (byte)'z')))
                    throw new InvalidDataException($"Invalid chunk type at offset {offset}.");
            }

            var type = Encoding.ASCII.GetString(typeBytes);
            var body = data.Slice(offset + 8, (int)length);
            var crc = BinaryPrimitives.ReadUInt32BigEndian(data[(offset + 8 + (int)length)..]);
            if (crc != ComputeCrc(data.Slice(offset + 4, 4 + (int)length)))
                throw new InvalidDataException($"CRC mismatch in chunk '{type}' at offset {offset}.");

            if (chunks.Count == 0 && type != "IHDR")
                throw new InvalidDataException("The first chunk is not IHDR.");

            if (char.IsUpper(type[0]) && type is not ("IHDR" or "IDAT" or "IEND"))
                throw new InvalidDataException($"Unexpected critical chunk '{type}'.");

            if (type == "IHDR" && chunks.Count != 0)
                throw new InvalidDataException("Duplicate IHDR.");

            if (type == "IDAT")
            {
                if (idatEnded)
                    throw new InvalidDataException("The IDAT chunks are not consecutive.");
            }
            else if (chunks.Count > 0 && chunks[^1].Type == "IDAT")
            {
                idatEnded = true;
            }

            chunks.Add(new ReferencePngChunk(type, body.ToArray()));
            offset += 12 + (int)length;
            if (type == "IEND")
            {
                if (length != 0)
                    throw new InvalidDataException("IEND has data.");

                if (offset != data.Length)
                    throw new InvalidDataException($"{data.Length - offset} bytes follow IEND.");

                break;
            }
        }

        if (!chunks.Any(chunk => chunk.Type == "IDAT"))
            throw new InvalidDataException("No IDAT chunk.");

        var header = chunks[0].Data.Span;
        if (header.Length != 13)
            throw new InvalidDataException("IHDR must be 13 bytes long.");

        var width = BinaryPrimitives.ReadUInt32BigEndian(header);
        var height = BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
        if (width is 0 or > int.MaxValue || height is 0 or > int.MaxValue)
            throw new InvalidDataException("Invalid dimensions.");

        var legal = header[9] switch
        {
            0 => header[8] is 1 or 2 or 4 or 8 or 16,
            2 or 4 or 6 => header[8] is 8 or 16,
            3 => header[8] is 1 or 2 or 4 or 8,
            _ => false,
        };

        if (!legal || header[10] != 0 || header[11] != 0 || header[12] > 1)
            throw new InvalidDataException("Invalid IHDR fields.");

        var animation = ParseAnimation(chunks, (int)width, (int)height);
        return new ReferencePng((int)width, (int)height, header[8], header[9], header[12], chunks, animation);
    }

    /// <summary>
    /// Decodes the datastream of an animation frame at its region size (<see cref="ReferenceApngFrame.Width"/> x
    /// <see cref="ReferenceApngFrame.Height"/>), in <see cref="Layout"/>.
    /// </summary>
    /// <param name="index">The frame index in playback order.</param>
    /// <returns>The region pixels.</returns>
    public RawPixelBuffer DecodeFrame(int index)
    {
        var animation = Animation ?? throw new InvalidOperationException("The file is not an APNG.");
        var frame = animation.Frames[index];
        return DecodeDatastream(frame.Datastream.ToArray(), frame.Width, frame.Height);
    }

    /// <summary>
    /// Decodes a displayed frame of an animation whose frame is a full-canvas <c>SOURCE</c> frame: its displayed image is its
    /// own data, independently of the previous frames, their disposal and any compositing.
    /// </summary>
    /// <param name="index">The frame index in playback order.</param>
    /// <returns>The displayed canvas.</returns>
    /// <exception cref="NotSupportedException">The frame is partial or blended with OVER (the reader has no compositor).</exception>
    public RawPixelBuffer DecodeDisplayedFrame(int index)
    {
        var animation = Animation ?? throw new InvalidOperationException("The file is not an APNG.");
        var frame = animation.Frames[index];
        if (!frame.IsFullCanvasSource(Width, Height))
            throw new NotSupportedException($"Frame {index} is not a full-canvas SOURCE frame; the reference reader does not composite.");

        return DecodeFrame(index);
    }

    /// <summary>Decodes the separate poster (the <c>IDAT</c> image of an APNG without an <c>fcTL</c> before <c>IDAT</c>).</summary>
    /// <returns>The poster.</returns>
    /// <exception cref="InvalidOperationException">The file has no separate poster.</exception>
    public RawPixelBuffer DecodePoster()
    {
        if (Animation is not { HasSeparatePoster: true })
            throw new InvalidOperationException("The file has no separate poster: its IDAT image is frame zero, or it is a static PNG.");

        return DecodePixels();
    }

    private static ReferenceApngAnimation? ParseAnimation(List<ReferencePngChunk> chunks, int width, int height)
    {
        var actl = chunks.Where(chunk => chunk.Type == "acTL").ToList();
        if (actl.Count == 0)
        {
            if (chunks.Any(chunk => chunk.Type is "fcTL" or "fdAT"))
                throw new InvalidDataException("fcTL or fdAT without acTL.");

            return null;
        }

        if (actl.Count > 1)
            throw new InvalidDataException("Duplicate acTL.");

        var firstIdat = chunks.FindIndex(chunk => chunk.Type == "IDAT");
        if (chunks.IndexOf(actl[0]) > firstIdat)
            throw new InvalidDataException("acTL after the first IDAT.");

        if (actl[0].Data.Length != 8)
            throw new InvalidDataException("acTL must be 8 bytes long.");

        var numFrames = BinaryPrimitives.ReadUInt32BigEndian(actl[0].Data.Span);
        var numPlays = BinaryPrimitives.ReadUInt32BigEndian(actl[0].Data.Span[4..]);
        if (numFrames is 0 or > int.MaxValue)
            throw new InvalidDataException($"Invalid acTL num_frames {numFrames}.");

        if (numPlays > int.MaxValue)
            throw new InvalidDataException($"Invalid acTL num_plays {numPlays}.");

        var frames = new List<ReferenceApngFrame>();
        uint expectedSequence = 0;
        var hasSeparatePoster = true;
        ReferenceApngFrame? current = null;
        List<uint>? dataSequences = null;
        MemoryStream? datastream = null;
        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            var body = chunk.Data.Span;
            switch (chunk.Type)
            {
                case "fcTL":
                    if (body.Length != 26)
                        throw new InvalidDataException("fcTL must be 26 bytes long.");

                    CheckSequence(BinaryPrimitives.ReadUInt32BigEndian(body), ref expectedSequence, "fcTL");
                    FinishFrame();
                    current = new ReferenceApngFrame(
                        SequenceNumber: BinaryPrimitives.ReadUInt32BigEndian(body),
                        Width: checked((int)BinaryPrimitives.ReadUInt32BigEndian(body[4..])),
                        Height: checked((int)BinaryPrimitives.ReadUInt32BigEndian(body[8..])),
                        XOffset: checked((int)BinaryPrimitives.ReadUInt32BigEndian(body[12..])),
                        YOffset: checked((int)BinaryPrimitives.ReadUInt32BigEndian(body[16..])),
                        DelayNumerator: BinaryPrimitives.ReadUInt16BigEndian(body[20..]),
                        DelayDenominator: BinaryPrimitives.ReadUInt16BigEndian(body[22..]),
                        DisposeOp: body[24],
                        BlendOp: body[25],
                        UsesIdat: i < firstIdat,
                        DataSequenceNumbers: [],
                        Datastream: ReadOnlyMemory<byte>.Empty);
                    if (current.Width <= 0 || current.Height <= 0 || (long)current.XOffset + current.Width > width || (long)current.YOffset + current.Height > height)
                        throw new InvalidDataException($"fcTL {current.SequenceNumber} region is empty or outside the canvas.");

                    if (current.DisposeOp > 2 || current.BlendOp > 1)
                        throw new InvalidDataException($"fcTL {current.SequenceNumber} has invalid dispose/blend operations.");

                    if (current.UsesIdat)
                    {
                        if (frames.Count > 0)
                            throw new InvalidDataException("Several fcTL chunks before IDAT.");

                        if (current.XOffset != 0 || current.YOffset != 0 || current.Width != width || current.Height != height)
                            throw new InvalidDataException("The fcTL of the default image does not cover the canvas.");

                        hasSeparatePoster = false;
                    }

                    dataSequences = [];
                    datastream = new MemoryStream();
                    break;

                case "IDAT":
                    if (current is { UsesIdat: true })
                    {
                        datastream!.Write(body);
                    }

                    break;

                case "fdAT":
                    if (i < firstIdat)
                        throw new InvalidDataException("fdAT before IDAT.");

                    if (current is null || current.UsesIdat)
                        throw new InvalidDataException("fdAT without a preceding fcTL.");

                    if (body.Length <= 4)
                        throw new InvalidDataException("fdAT without data.");

                    var sequence = BinaryPrimitives.ReadUInt32BigEndian(body);
                    CheckSequence(sequence, ref expectedSequence, "fdAT");
                    dataSequences!.Add(sequence);
                    datastream!.Write(body[4..]);
                    break;
            }
        }

        FinishFrame();
        if (frames.Count != numFrames)
            throw new InvalidDataException($"acTL declares {numFrames} frames but the file has {frames.Count}.");

        return new ReferenceApngAnimation((int)numFrames, (int)numPlays, hasSeparatePoster, frames);

        void FinishFrame()
        {
            if (current is null)
                return;

            if (datastream!.Length == 0)
                throw new InvalidDataException($"fcTL {current.SequenceNumber} has no image data.");

            frames.Add(current with { DataSequenceNumbers = dataSequences!, Datastream = datastream.ToArray() });
            current = null;
        }

        static void CheckSequence(uint actual, ref uint expected, string type)
        {
            if (actual != expected || actual > int.MaxValue)
                throw new InvalidDataException($"{type} sequence number {actual}, {expected} expected.");

            expected++;
        }
    }

    /// <summary>
    /// Returns the file a decoder without APNG support sees: the same bytes without the <c>acTL</c>, <c>fcTL</c> and
    /// <c>fdAT</c> chunks (every other chunk is copied unchanged, CRC included). Its image is the <c>IDAT</c> image: the
    /// separate poster, or frame zero.
    /// </summary>
    /// <param name="data">The APNG file.</param>
    /// <returns>The static PNG file.</returns>
    public static byte[] RemoveAnimationChunks(ReadOnlySpan<byte> data)
    {
        _ = Parse(data); // validated first
        var output = new MemoryStream();
        output.Write(data[..8]);
        var offset = 8;
        while (offset < data.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
            var type = Encoding.ASCII.GetString(data.Slice(offset + 4, 4));
            if (type is not ("acTL" or "fcTL" or "fdAT"))
            {
                output.Write(data.Slice(offset, 12 + length));
            }

            offset += 12 + length;
        }

        return output.ToArray();
    }

    /// <summary>Gets the chunks of a type.</summary>
    public IEnumerable<ReferencePngChunk> GetChunks(string type) => Chunks.Where(chunk => chunk.Type == type);

    /// <summary>Decodes the pixels to a raw buffer in <see cref="Layout"/> (16-bit samples little-endian).</summary>
    /// <returns>The pixels.</returns>
    /// <exception cref="InvalidDataException">The image data is invalid (zlib error, wrong length, invalid filter type).</exception>
    public RawPixelBuffer DecodePixels() => DecodeDatastream(GetImageData(), Width, Height);

    private RawPixelBuffer DecodeDatastream(byte[] zlib, int width, int height)
    {
        var layout = Layout;
        var bytesPerPixel = layout.BytesPerPixel;
        var inflated = Inflate(zlib);
        var pixels = new byte[width * height * bytesPerPixel];
        var offset = 0;
        var passes = InterlaceMethod == 0 ? [(0, 0, 1, 1)] : Adam7;
        foreach (var (x0, y0, dx, dy) in passes)
        {
            var passWidth = width > x0 ? ((width - x0 - 1) / dx) + 1 : 0;
            var passHeight = height > y0 ? ((height - y0 - 1) / dy) + 1 : 0;
            if (passWidth == 0 || passHeight == 0)
                continue;

            var rowLength = passWidth * bytesPerPixel;
            var previous = new byte[rowLength];
            for (var row = 0; row < passHeight; row++)
            {
                if (offset + 1 + rowLength > inflated.Length)
                    throw new InvalidDataException("The image data is too short.");

                var filter = inflated[offset];
                var current = inflated.AsSpan(offset + 1, rowLength).ToArray();
                offset += 1 + rowLength;
                Unfilter(filter, current, previous, bytesPerPixel);
                for (var i = 0; i < passWidth; i++)
                {
                    var target = ((((y0 + (row * dy)) * width) + x0 + (i * dx)) * bytesPerPixel);
                    current.AsSpan(i * bytesPerPixel, bytesPerPixel).CopyTo(pixels.AsSpan(target));
                }

                previous = current;
            }
        }

        if (offset != inflated.Length)
            throw new InvalidDataException($"The image data has {inflated.Length - offset} extra bytes.");

        if (BitDepth == 16)
        {
            // Big-endian samples to the little-endian raw layouts
            for (var i = 0; i < pixels.Length; i += 2)
            {
                (pixels[i], pixels[i + 1]) = (pixels[i + 1], pixels[i]);
            }
        }

        return RawPixelBuffer.Create(width, height, layout, pixels);
    }

    /// <summary>Gets the filter type byte of every scanline, in datastream order.</summary>
    /// <returns>The filter types.</returns>
    public IReadOnlyList<byte> GetFilterTypes()
    {
        var bytesPerPixel = Layout.BytesPerPixel;
        var inflated = Inflate(GetImageData());
        var result = new List<byte>();
        var offset = 0;
        var passes = InterlaceMethod == 0 ? [(0, 0, 1, 1)] : Adam7;
        foreach (var (x0, y0, dx, dy) in passes)
        {
            var passWidth = Width > x0 ? ((Width - x0 - 1) / dx) + 1 : 0;
            var passHeight = Height > y0 ? ((Height - y0 - 1) / dy) + 1 : 0;
            if (passWidth == 0 || passHeight == 0)
                continue;

            for (var row = 0; row < passHeight; row++)
            {
                result.Add(inflated[offset]);
                offset += 1 + (passWidth * bytesPerPixel);
            }
        }

        return result;
    }

    private static void Unfilter(byte filter, byte[] current, byte[] previous, int bytesPerPixel)
    {
        for (var i = 0; i < current.Length; i++)
        {
            int left = i >= bytesPerPixel ? current[i - bytesPerPixel] : 0;
            int up = previous[i];
            int upperLeft = i >= bytesPerPixel ? previous[i - bytesPerPixel] : 0;
            var predictor = filter switch
            {
                0 => 0,
                1 => left,
                2 => up,
                3 => (left + up) / 2,
                4 => Paeth(left, up, upperLeft),
                _ => throw new InvalidDataException($"Invalid filter type {filter}."),
            };

            current[i] = (byte)(current[i] + predictor);
        }

        static int Paeth(int a, int b, int c)
        {
            var p = a + b - c;
            var pa = Math.Abs(p - a);
            var pb = Math.Abs(p - b);
            var pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }
    }

    private static uint ComputeCrc(ReadOnlySpan<byte> data)
    {
        // Bitwise CRC-32 (ISO 3309, reflected polynomial 0xEDB88320), deliberately not table-driven
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }

    private byte[] GetImageData()
    {
        using var compressed = new MemoryStream();
        foreach (var chunk in GetChunks("IDAT"))
        {
            compressed.Write(chunk.Data.Span);
        }

        return compressed.ToArray();
    }

    private static byte[] Inflate(byte[] zlib)
    {
        if (zlib.Length < 6)
            throw new InvalidDataException("The zlib datastream is too short.");

        // RFC 1950 header: deflate, 32 KiB window or less, check bits, no preset dictionary
        if ((zlib[0] & 0x0F) != 8 || (zlib[0] >> 4) > 7 || ((zlib[0] << 8) | zlib[1]) % 31 != 0 || (zlib[1] & 0x20) != 0)
            throw new InvalidDataException("Invalid zlib header.");

        using var output = new MemoryStream();
        using (var inflater = new ZLibStream(new MemoryStream(zlib), CompressionMode.Decompress))
        {
            inflater.CopyTo(output);
        }

        var result = output.ToArray();
        var adler = BinaryPrimitives.ReadUInt32BigEndian(zlib.AsSpan(zlib.Length - 4));
        if (adler != ComputeAdler32(result))
            throw new InvalidDataException("The zlib datastream does not end with the Adler-32 of its data.");

        return result;
    }

    private static uint ComputeAdler32(ReadOnlySpan<byte> data)
    {
        uint a = 1, b = 0;
        foreach (var value in data)
        {
            a = (a + value) % 65521;
            b = (b + a) % 65521;
        }

        return (b << 16) | a;
    }
}
