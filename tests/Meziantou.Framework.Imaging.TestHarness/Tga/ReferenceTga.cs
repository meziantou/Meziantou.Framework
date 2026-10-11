using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.TestHarness.Tga;

/// <summary>
/// An independent, deliberately simple reading of the Truevision TGA 2.0 subset the library supports, used to verify the
/// library's encoder output without the library's decoder. It keeps the whole input in memory, expands every pixel to
/// straight RGBA8, and is strict: an implausible header, an unsupported depth, a packet past the last pixel, a color-map
/// index outside the stored map and truncation throw <see cref="InvalidDataException"/>. The TGA 2.0 footer is looked up in
/// the last 26 bytes of the file and its extension offset is followed: an extension area that does not lie between the image
/// data and the footer, one that is too short to hold an attributes type, and premultiplied alpha (attributes type 4 with
/// declared alpha bits) throw <see cref="InvalidDataException"/> too.
/// </summary>
public sealed class ReferenceTga
{
    private ReferenceTga(int width, int height, int imageType, int pixelDepth, byte descriptor, int colorMapLength, int trailingBytes, int packets, byte[] rgba, bool hasFooter, int? attributesType)
    {
        HasFooter = hasFooter;
        AttributesType = attributesType;
        Width = width;
        Height = height;
        ImageType = imageType;
        PixelDepth = pixelDepth;
        Descriptor = descriptor;
        ColorMapLength = colorMapLength;
        TrailingBytes = trailingBytes;
        Packets = packets;
        Rgba = rgba;
    }

    /// <summary>Gets the width, in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the height, in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets the image type (1, 2, 3, 9, 10 or 11).</summary>
    public int ImageType { get; }

    /// <summary>Gets the number of bits of one stored pixel.</summary>
    public int PixelDepth { get; }

    /// <summary>Gets the image descriptor byte.</summary>
    public byte Descriptor { get; }

    /// <summary>Gets the number of alpha bits the image descriptor declares.</summary>
    public int AlphaBits => Descriptor & 0x0F;

    /// <summary>Gets the number of stored color-map entries.</summary>
    public int ColorMapLength { get; }

    /// <summary>Gets the number of bytes after the image data (a TGA 2.0 trailer).</summary>
    public int TrailingBytes { get; }

    /// <summary>Gets the number of run-length and raw packets read (0 for uncompressed images).</summary>
    public int Packets { get; }

    /// <summary>Gets the decoded pixels as straight RGBA8, row-major, top-down.</summary>
    public ReadOnlyMemory<byte> Rgba { get; }

    /// <summary>Gets a value indicating whether a TGA 2.0 footer (its signature) ends the file.</summary>
    public bool HasFooter { get; }

    /// <summary>Gets the attributes type of the extension area the footer points at, or <see langword="null"/> without one.</summary>
    public int? AttributesType { get; }

    /// <summary>Reads a TGA file.</summary>
    /// <param name="data">The whole file.</param>
    /// <returns>The decoded image.</returns>
    /// <exception cref="InvalidDataException">The input is malformed or uses an unsupported variant.</exception>
    public static ReferenceTga Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 18)
            throw new InvalidDataException("Missing TGA header.");

        int idLength = data[0], colorMapType = data[1], imageType = data[2];
        var mapFirst = BinaryPrimitives.ReadUInt16LittleEndian(data[3..]);
        var mapLength = BinaryPrimitives.ReadUInt16LittleEndian(data[5..]);
        int mapEntryBits = data[7];
        var width = BinaryPrimitives.ReadUInt16LittleEndian(data[12..]);
        var height = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]);
        int pixelDepth = data[16];
        var descriptor = data[17];
        if (colorMapType > 1 || width == 0 || height == 0 || (descriptor & 0xC0) != 0)
            throw new InvalidDataException("Implausible TGA header.");

        if (imageType is not (1 or 2 or 3 or 9 or 10 or 11))
            throw new InvalidDataException($"Unsupported TGA image type {imageType}.");

        var colorMapped = imageType is 1 or 9;
        if (colorMapped != (colorMapType == 1))
            throw new InvalidDataException("The color-map type contradicts the image type.");

        if (colorMapped && (mapLength == 0 || mapEntryBits is not (15 or 16 or 24 or 32) || pixelDepth != 8))
            throw new InvalidDataException("Unsupported TGA color map.");

        var grayscale = imageType is 3 or 11;
        if (grayscale && pixelDepth != 8)
            throw new InvalidDataException($"Unsupported {pixelDepth}-bit TGA grayscale.");

        if (!colorMapped && !grayscale && pixelDepth is not (15 or 16 or 24 or 32))
            throw new InvalidDataException($"Unsupported TGA pixel depth {pixelDepth}.");

        var alphaBits = descriptor & 0x0F;
        var sampleBits = colorMapped ? mapEntryBits : pixelDepth;
        var legal = sampleBits switch { 32 => alphaBits is 0 or 8, 16 => alphaBits is 0 or 1, _ => alphaBits == 0 };
        if (!legal)
            throw new InvalidDataException($"The TGA declares {alphaBits} alpha bits for {sampleBits}-bit samples.");

        var offset = 18 + idLength;
        Require(data, offset);
        var palette = new byte[256 * 4];
        var paletteDefined = new bool[256];
        if (colorMapped)
        {
            var entryBytes = (mapEntryBits + 7) / 8;
            Require(data, offset + (mapLength * entryBytes));
            for (var i = 0; i < mapLength; i++)
            {
                var index = mapFirst + i;
                if (index < 256)
                {
                    DecodeSample(data.Slice(offset + (i * entryBytes), entryBytes), mapEntryBits, alphaBits > 0, palette.AsSpan(index * 4, 4));
                    paletteDefined[index] = true;
                }
            }

            offset += mapLength * entryBytes;
        }

        var bytesPerPixel = (pixelDepth + 7) / 8;
        var total = width * height;
        var stored = new byte[total * bytesPerPixel];
        var packets = 0;
        if (imageType is 9 or 10 or 11)
        {
            var written = 0;
            while (written < total)
            {
                Require(data, offset + 1);
                var packet = data[offset++];
                packets++;
                var count = (packet & 0x7F) + 1;
                if (count > total - written)
                    throw new InvalidDataException($"A TGA packet of {count} pixels runs past the last pixel.");

                if ((packet & 0x80) != 0)
                {
                    Require(data, offset + bytesPerPixel);
                    for (var i = 0; i < count; i++)
                    {
                        data.Slice(offset, bytesPerPixel).CopyTo(stored.AsSpan((written + i) * bytesPerPixel));
                    }

                    offset += bytesPerPixel;
                }
                else
                {
                    Require(data, offset + (count * bytesPerPixel));
                    data.Slice(offset, count * bytesPerPixel).CopyTo(stored.AsSpan(written * bytesPerPixel));
                    offset += count * bytesPerPixel;
                }

                written += count;
            }
        }
        else
        {
            Require(data, offset + (total * bytesPerPixel));
            data.Slice(offset, total * bytesPerPixel).CopyTo(stored);
            offset += total * bytesPerPixel;
        }

        var rgba = new byte[total * 4];
        var topDown = (descriptor & 0x20) != 0;
        var rightToLeft = (descriptor & 0x10) != 0;
        for (var index = 0; index < height; index++)
        {
            var y = topDown ? index : height - 1 - index;
            for (var x = 0; x < width; x++)
            {
                var source = stored.AsSpan(((index * width) + x) * bytesPerPixel, bytesPerPixel);
                var destination = rgba.AsSpan(((y * width) + (rightToLeft ? width - 1 - x : x)) * 4, 4);
                if (colorMapped)
                {
                    var paletteIndex = source[0];
                    if (!paletteDefined[paletteIndex])
                        throw new InvalidDataException($"The TGA color-map index {paletteIndex} is outside the stored map.");

                    palette.AsSpan(paletteIndex * 4, 4).CopyTo(destination);
                }
                else if (grayscale)
                {
                    destination[0] = destination[1] = destination[2] = source[0];
                    destination[3] = byte.MaxValue;
                }
                else
                {
                    DecodeSample(source, pixelDepth, alphaBits > 0, destination);
                }
            }
        }

        var hasFooter = data.Length - offset >= FooterLength && data[^FooterSignature.Length..].SequenceEqual(FooterSignature);
        var attributesType = hasFooter ? ReadAttributesType(data, offset) : null;
        if (attributesType == 4 && alphaBits > 0)
            throw new InvalidDataException("Unsupported TGA premultiplied alpha (attributes type 4).");

        return new ReferenceTga(width, height, imageType, pixelDepth, descriptor, mapLength, data.Length - offset, packets, rgba, hasFooter, attributesType);
    }

    private const int FooterLength = 26;
    private const int ExtensionLength = 495;

    private static ReadOnlySpan<byte> FooterSignature => "TRUEVISION-XFILE.\0"u8;

    /// <summary>Follows the extension offset of the footer; <paramref name="imageDataEnd"/> is the first byte the extension area may use.</summary>
    private static int? ReadAttributesType(ReadOnlySpan<byte> data, int imageDataEnd)
    {
        var footer = data.Length - FooterLength;
        var extension = BinaryPrimitives.ReadUInt32LittleEndian(data[footer..]);
        if (extension == 0)
            return null;

        if (extension < imageDataEnd || extension + (long)ExtensionLength > footer)
            throw new InvalidDataException($"The TGA extension area at offset {extension} is not between the image data ({imageDataEnd}) and the footer ({footer}).");

        var area = data.Slice((int)extension, ExtensionLength);
        var size = BinaryPrimitives.ReadUInt16LittleEndian(area);
        if (size < ExtensionLength)
            throw new InvalidDataException($"The TGA extension area declares {size} bytes.");

        return area[494];
    }

    private static void DecodeSample(ReadOnlySpan<byte> source, int bits, bool withAlpha, Span<byte> destination)
    {
        if (bits is 15 or 16)
        {
            var value = BinaryPrimitives.ReadUInt16LittleEndian(source);
            destination[0] = Expand5((value >> 10) & 31);
            destination[1] = Expand5((value >> 5) & 31);
            destination[2] = Expand5(value & 31);
            destination[3] = !withAlpha || (value & 0x8000) != 0 ? byte.MaxValue : (byte)0;
            return;
        }

        destination[0] = source[2];
        destination[1] = source[1];
        destination[2] = source[0];
        destination[3] = bits == 32 && withAlpha ? source[3] : byte.MaxValue;
    }

    private static byte Expand5(int value) => (byte)(((value * 255) + 15) / 31);

    private static void Require(ReadOnlySpan<byte> data, int length)
    {
        if (data.Length < length)
            throw new InvalidDataException($"The TGA data is truncated ({data.Length} bytes, {length} needed).");
    }
}
