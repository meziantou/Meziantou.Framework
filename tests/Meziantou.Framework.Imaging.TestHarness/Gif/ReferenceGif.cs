using System.Buffers.Binary;
using System.Text;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.TestHarness.Gif;

/// <summary>
/// A small reference GIF reader written from the GIF89a specification for encoder tests. It never uses the library
/// under test: it parses the whole file in memory (header, logical screen, color tables, Graphic Control, comment and
/// application extensions, image descriptors, data sub-blocks, trailer), decodes each LZW datastream with a straightforward
/// string-table decoder, and composites the displayed frames.
/// </summary>
/// <remarks>
/// <para>
/// Compositing follows the conventions of the library (the browsers and Apple ImageIO): the canvas starts
/// transparent black, image rectangles are clipped to the logical screen, the transparent index keeps the canvas pixel, other
/// indices replace it with their opaque color, disposal 2 clears the rectangle to transparent black (the background color
/// index is never painted), 3 and 4 restore the rectangle saved before the image, 0, 1 and 5-7 keep it.
/// </para>
/// <para>
/// Every structural defect is an <see cref="InvalidDataException"/>: a bad signature or empty screen, an unknown block, a
/// Graphic Control Extension that is not 4 bytes, an image without color table, an LZW minimum code size outside 1-11, an
/// invalid code, fewer indices than the image rectangle, an index outside the color table (unless transparent), a missing
/// trailer, or data after it. Plain-text extensions are rejected the same way (they are outside the reader's scope).
/// </para>
/// </remarks>
public sealed class ReferenceGif
{
    private ReferenceGif(string version, int width, int height, ReadOnlyMemory<byte>? globalColorTable, byte backgroundColorIndex, IReadOnlyList<ReferenceGifImage> images, IReadOnlyList<string> comments, IReadOnlyList<ushort> loopCounts, IReadOnlyList<string> blocks)
    {
        Version = version;
        Width = width;
        Height = height;
        GlobalColorTable = globalColorTable;
        BackgroundColorIndex = backgroundColorIndex;
        Images = images;
        Comments = comments;
        LoopCounts = loopCounts;
        Blocks = blocks;
    }

    /// <summary>Gets the version: <c>87a</c> or <c>89a</c>.</summary>
    public string Version { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Gets the global color table (RGB triplets), or <see langword="null"/>.</summary>
    public ReadOnlyMemory<byte>? GlobalColorTable { get; }

    public byte BackgroundColorIndex { get; }

    /// <summary>Gets the images in file order.</summary>
    public IReadOnlyList<ReferenceGifImage> Images { get; }

    /// <summary>Gets the comment extensions (Latin-1), in file order.</summary>
    public IReadOnlyList<string> Comments { get; }

    /// <summary>Gets the NETSCAPE2.0/ANIMEXTS1.0 loop counts, in file order (the last one applies).</summary>
    public IReadOnlyList<ushort> LoopCounts { get; }

    /// <summary>Gets the block sequence: <c>GCE</c>, <c>Comment</c>, <c>Loop</c>, <c>Application</c>, <c>Extension</c>, <c>Image</c>.</summary>
    public IReadOnlyList<string> Blocks { get; }

    /// <summary>Gets the total number of plays: <c>L + 1</c>, <see langword="null"/> (infinite) for <c>L = 0</c>, 1 without loop extension.</summary>
    public int? TotalPlays => LoopCounts.Count == 0 ? 1 : LoopCounts[^1] == 0 ? null : LoopCounts[^1] + 1;

    /// <summary>Parses and decodes a GIF file.</summary>
    /// <param name="data">The file.</param>
    /// <returns>The parsed file.</returns>
    /// <exception cref="InvalidDataException">The file is malformed.</exception>
    public static ReferenceGif Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 13 || !(data.StartsWith("GIF87a"u8) || data.StartsWith("GIF89a"u8)))
            throw new InvalidDataException("Not a GIF file.");

        var version = Encoding.ASCII.GetString(data.Slice(3, 3));
        var width = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
        var height = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);
        if (width == 0 || height == 0)
            throw new InvalidDataException("Empty logical screen.");

        var packed = data[10];
        var background = data[11];
        var offset = 13;
        byte[]? globalTable = null;
        if ((packed & 0x80) != 0)
        {
            var length = 3 * (2 << (packed & 7));
            globalTable = Take(data, ref offset, length).ToArray();
        }

        var images = new List<ReferenceGifImage>();
        var comments = new List<string>();
        var loops = new List<ushort>();
        var blocks = new List<string>();
        ReferenceGifControl? control = null;
        while (true)
        {
            var introducer = Take(data, ref offset, 1)[0];
            if (introducer == 0x3B)
                break;

            if (introducer == 0x21)
            {
                var label = Take(data, ref offset, 1)[0];
                var subBlocks = ReadSubBlocks(data, ref offset);
                switch (label)
                {
                    case 0xF9:
                        if (subBlocks.Count == 0 || subBlocks[0].Length != 4)
                            throw new InvalidDataException("The Graphic Control Extension must have one 4-byte block.");

                        var gce = subBlocks[0];
                        control = new ReferenceGifControl((gce[0] >> 2) & 7, (gce[0] & 1) != 0, BinaryPrimitives.ReadUInt16LittleEndian(gce.AsSpan(1)), gce[3]);
                        blocks.Add("GCE");
                        break;

                    case 0xFE:
                        comments.Add(Encoding.Latin1.GetString([.. subBlocks.SelectMany(block => block)]));
                        blocks.Add("Comment");
                        break;

                    case 0xFF:
                        // The loop sub-block (identifier 1) of NETSCAPE2.0/ANIMEXTS1.0; other sub-blocks are skipped
                        var loop = subBlocks.Count >= 2 && subBlocks[0].Length == 11 && (subBlocks[0].AsSpan().SequenceEqual("NETSCAPE2.0"u8) || subBlocks[0].AsSpan().SequenceEqual("ANIMEXTS1.0"u8))
                            ? subBlocks.Skip(1).FirstOrDefault(block => block.Length >= 3 && block[0] == 1)
                            : null;
                        if (loop is not null)
                        {
                            loops.Add(BinaryPrimitives.ReadUInt16LittleEndian(loop.AsSpan(1)));
                            blocks.Add("Loop");
                        }
                        else
                        {
                            blocks.Add("Application");
                        }

                        break;

                    case 0x01:
                        throw new InvalidDataException("Plain text extensions are not supported by the reference reader.");

                    default:
                        blocks.Add("Extension");
                        break;
                }

                continue;
            }

            if (introducer != 0x2C)
                throw new InvalidDataException($"Unknown block introducer 0x{introducer:X2}.");

            var descriptor = Take(data, ref offset, 9);
            var left = BinaryPrimitives.ReadUInt16LittleEndian(descriptor);
            var top = BinaryPrimitives.ReadUInt16LittleEndian(descriptor[2..]);
            var imageWidth = BinaryPrimitives.ReadUInt16LittleEndian(descriptor[4..]);
            var imageHeight = BinaryPrimitives.ReadUInt16LittleEndian(descriptor[6..]);
            var imagePacked = descriptor[8];
            byte[]? localTable = null;
            if ((imagePacked & 0x80) != 0)
            {
                localTable = Take(data, ref offset, 3 * (2 << (imagePacked & 7))).ToArray();
            }

            var codeSize = Take(data, ref offset, 1)[0];
            if (codeSize is < 1 or > 11)
                throw new InvalidDataException($"Invalid LZW minimum code size {codeSize}.");

            var compressed = ReadSubBlocks(data, ref offset).SelectMany(block => block).ToArray();
            var table = localTable ?? globalTable ?? throw new InvalidDataException("The image has no color table.");
            var interlaced = (imagePacked & 0x40) != 0;
            var indices = DecodeLzw(compressed, codeSize, imageWidth * imageHeight);
            var rows = interlaced ? Deinterlace(indices, imageWidth, imageHeight) : indices;
            foreach (var index in rows)
            {
                if (index * 3 >= table.Length && !(control is { HasTransparency: true } c && c.TransparentIndex == index))
                    throw new InvalidDataException($"The color index {index} is outside the color table.");
            }

            images.Add(new ReferenceGifImage(left, top, imageWidth, imageHeight, interlaced, localTable is null ? (ReadOnlyMemory<byte>?)null : localTable, codeSize, control, rows));
            blocks.Add("Image");
            control = null;
        }

        if (offset != data.Length)
            throw new InvalidDataException("Data follows the trailer.");

        if (images.Count == 0)
            throw new InvalidDataException("The file has no image.");

        return new ReferenceGif(version, width, height, globalTable is null ? (ReadOnlyMemory<byte>?)null : globalTable, background, images, comments, loops, blocks);
    }

    /// <summary>Composites every displayed frame (with the conventions of the library decoder) as straight 8-bit RGBA.</summary>
    /// <returns>One full-canvas buffer per image.</returns>
    public IReadOnlyList<RawPixelBuffer> DecodeDisplayedFrames()
    {
        var canvas = new byte[Width * Height * 4];
        var frames = new List<RawPixelBuffer>(Images.Count);
        foreach (var image in Images)
        {
            var table = (image.LocalColorTable ?? GlobalColorTable!.Value).Span;
            var indices = image.Indices.Span;
            var x0 = Math.Min(image.Left, Width);
            var y0 = Math.Min(image.Top, Height);
            var x1 = Math.Min(image.Left + image.Width, Width);
            var y1 = Math.Min(image.Top + image.Height, Height);
            var disposal = image.Control?.DisposalMethod ?? 0;
            byte[]? saved = disposal is 3 or 4 ? (byte[])canvas.Clone() : null;
            for (var y = y0; y < y1; y++)
            {
                for (var x = x0; x < x1; x++)
                {
                    var index = indices[((y - image.Top) * image.Width) + (x - image.Left)];
                    if (image.Control is { HasTransparency: true } control && control.TransparentIndex == index)
                        continue;

                    var target = ((y * Width) + x) * 4;
                    canvas[target] = table[3 * index];
                    canvas[target + 1] = table[(3 * index) + 1];
                    canvas[target + 2] = table[(3 * index) + 2];
                    canvas[target + 3] = 255;
                }
            }

            frames.Add(RawPixelBuffer.Create(Width, Height, RawPixelLayout.Rgba8, canvas));
            for (var y = y0; y < y1; y++)
            {
                var start = ((y * Width) + x0) * 4;
                var length = (x1 - x0) * 4;
                if (disposal == 2)
                {
                    canvas.AsSpan(start, length).Clear();
                }
                else if (saved is not null)
                {
                    saved.AsSpan(start, length).CopyTo(canvas.AsSpan(start));
                }
            }
        }

        return frames;
    }

    private static ReadOnlySpan<byte> Take(ReadOnlySpan<byte> data, ref int offset, int length)
    {
        if (data.Length - offset < length)
            throw new InvalidDataException("Truncated GIF file.");

        var result = data.Slice(offset, length);
        offset += length;
        return result;
    }

    private static List<byte[]> ReadSubBlocks(ReadOnlySpan<byte> data, ref int offset)
    {
        var blocks = new List<byte[]>();
        while (true)
        {
            var length = Take(data, ref offset, 1)[0];
            if (length == 0)
                return blocks;

            blocks.Add(Take(data, ref offset, length).ToArray());
        }
    }

    /// <summary>Decodes an LZW datastream (GIF89a Appendix F): codes LSB first, clear and end codes, growth to 12 bits, deferred clear.</summary>
    private static byte[] DecodeLzw(byte[] data, int minimumCodeSize, int pixelCount)
    {
        var clear = 1 << minimumCodeSize;
        var end = clear + 1;
        var output = new List<byte>(pixelCount);
        var strings = new List<byte[]>(4096);
        void ResetTable()
        {
            strings.Clear();
            for (var i = 0; i < clear; i++)
            {
                strings.Add([(byte)i]);
            }

            strings.Add([]); // clear
            strings.Add([]); // end
        }

        ResetTable();
        var codeSize = minimumCodeSize + 1;
        byte[]? previous = null;
        var bitPosition = 0L;
        var totalBits = (long)data.Length * 8;
        var ended = false;
        while (bitPosition + codeSize <= totalBits)
        {
            var code = 0;
            for (var bit = 0; bit < codeSize; bit++)
            {
                var position = bitPosition + bit;
                code |= ((data[position >> 3] >> (int)(position & 7)) & 1) << bit;
            }

            bitPosition += codeSize;
            if (code == clear)
            {
                ResetTable();
                codeSize = minimumCodeSize + 1;
                previous = null;
                continue;
            }

            if (code == end)
            {
                ended = true;
                break;
            }

            byte[] current;
            if (previous is null)
            {
                if (code >= clear)
                    throw new InvalidDataException($"The code {code} follows a clear code but is not a color index.");

                current = strings[code];
            }
            else
            {
                if (code < strings.Count)
                {
                    current = strings[code];
                }
                else if (code == strings.Count && strings.Count < 4096)
                {
                    current = [.. previous, previous[0]];
                }
                else
                {
                    throw new InvalidDataException($"The code {code} is not defined (next code {strings.Count}).");
                }

                if (strings.Count < 4096)
                {
                    strings.Add([.. previous, current[0]]);
                }
            }

            if (output.Count == pixelCount)
            {
                // The image is complete: a data code made only of the padding bits of the last byte is not data (a missing
                // end code), unless an end code follows it in that byte (which proves it was an excess index); a data code
                // that starts earlier is an excess index
                if (bitPosition - codeSize >= (data.Length - 1) * 8L)
                {
                    var endFollows = false;
                    for (var position = bitPosition; position + codeSize <= totalBits; position += codeSize)
                    {
                        var next = 0;
                        for (var bit = 0; bit < codeSize; bit++)
                        {
                            next |= ((data[(position + bit) >> 3] >> (int)((position + bit) & 7)) & 1) << bit;
                        }

                        endFollows |= next == end;
                    }

                    if (!endFollows)
                    {
                        ended = true;
                        break;
                    }
                }

                throw new InvalidDataException("The datastream decodes to more indices than the image.");
            }

            output.AddRange(current);
            if (output.Count > pixelCount)
                throw new InvalidDataException("The datastream decodes to more indices than the image.");

            previous = current;
            if (strings.Count >= 1 << codeSize && codeSize < 12)
            {
                codeSize++;
            }
        }

        if (output.Count < pixelCount)
            throw new InvalidDataException($"The datastream decodes to {output.Count} indices, {pixelCount} expected.");

        if (!ended && totalBits - bitPosition >= 8)
            throw new InvalidDataException("The datastream has no end code.");

        return [.. output];
    }

    private static byte[] Deinterlace(byte[] indices, int width, int height)
    {
        var result = new byte[indices.Length];
        var source = 0;
        foreach (var (start, step) in new[] { (0, 8), (4, 8), (2, 4), (1, 2) })
        {
            for (var y = start; y < height; y += step)
            {
                indices.AsSpan(source * width, width).CopyTo(result.AsSpan(y * width));
                source++;
            }
        }

        return result;
    }
}
