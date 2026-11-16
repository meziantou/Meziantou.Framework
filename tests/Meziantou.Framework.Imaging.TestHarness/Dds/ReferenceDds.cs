using System.Buffers.Binary;
using System.Numerics;

namespace Meziantou.Framework.Imaging.TestHarness.Dds;

/// <summary>
/// An independent reader of DDS files for the tests and the corpus generator, written from the DDS reference of Microsoft
/// Learn and the definitions of the DXGI formats; it never uses the library under test. It reads the headers, locates
/// every surface, and gives the pixels of the uncompressed formats as the real numbers they define, presented as the
/// library presents them (one channel as gray, two as red and green with a blue of 0, an alpha of 1 when none is stored,
/// an alpha mask as black). It does not decode the block-compressed formats: their references come from DirectXTex.
/// </summary>
public static class ReferenceDds
{
    /// <summary>Reads the headers and the layout of a file.</summary>
    /// <param name="data">The file.</param>
    /// <returns>The texture.</returns>
    /// <exception cref="InvalidDataException">The file is not a DDS file the reader understands.</exception>
    public static DdsTexture Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < 128 || ReadUInt32(data, 0) != DdsFileBuilder.FourCC("DDS ") || ReadUInt32(data, 4) != 124 || ReadUInt32(data, 76) != 32)
            throw new InvalidDataException("Not a DDS file.");

        var height = (int)ReadUInt32(data, 12);
        var width = (int)ReadUInt32(data, 16);
        var mipCount = Math.Max(1, (int)ReadUInt32(data, 28));
        var pixelFlags = ReadUInt32(data, 80);
        var fourCC = ReadUInt32(data, 84);
        var caps2 = ReadUInt32(data, 112);
        var headerLength = 128;
        var arraySize = 1;
        var depth = 1;
        int[] faces = [-1];
        DdsReferenceFormat format;
        if ((pixelFlags & DdsFileBuilder.PixelFourCC) != 0 && fourCC == DdsFileBuilder.FourCC("DX10"))
        {
            if (data.Length < 148)
                throw new InvalidDataException("Truncated DX10 header.");

            headerLength = 148;
            format = DdsReferenceFormat.FromDxgi(ReadUInt32(data, 128));
            var dimension = ReadUInt32(data, 132);
            arraySize = (int)ReadUInt32(data, 140);
            if ((ReadUInt32(data, 136) & 4) != 0)
            {
                faces = [0, 1, 2, 3, 4, 5];
            }

            if (dimension == 4)
            {
                depth = (int)ReadUInt32(data, 24);
            }
        }
        else
        {
            format = DdsReferenceFormat.FromLegacy(pixelFlags, fourCC, ReadUInt32(data, 88), ReadUInt32(data, 92), ReadUInt32(data, 96), ReadUInt32(data, 100), ReadUInt32(data, 104));
            if ((caps2 & DdsFileBuilder.Caps2Volume) != 0)
            {
                depth = (int)ReadUInt32(data, 24);
            }
            else if ((caps2 & DdsFileBuilder.Caps2CubeMap) != 0)
            {
                faces = [.. Enumerable.Range(0, 6).Where(i => (caps2 & (0x400u << i)) != 0)];
            }
        }

        var surfaces = new List<DdsReferenceSurface>();
        long offset = headerLength;
        for (var element = 0; element < arraySize; element++)
        {
            foreach (var face in faces)
            {
                for (var mip = 0; mip < mipCount; mip++)
                {
                    var w = Math.Max(1, width >> mip);
                    var h = Math.Max(1, height >> mip);
                    var d = Math.Max(1, depth >> mip);
                    var length = format.GetSurfaceLength(w, h);
                    for (var slice = 0; slice < d; slice++)
                    {
                        surfaces.Add(new DdsReferenceSurface(element, face, mip, slice, w, h, offset, length));
                        offset += length;
                    }
                }
            }
        }

        if (offset > data.Length)
            throw new InvalidDataException($"The file has {data.Length} bytes; its surfaces end at {offset}.");

        return new DdsTexture(width, height, depth, mipCount, arraySize, faces[0] >= 0 ? faces : [], format, headerLength, surfaces);
    }

    /// <summary>
    /// Gives the pixels of a surface of an uncompressed format: four real numbers per pixel (red, green, blue, alpha), the
    /// rows from the top.
    /// </summary>
    public static double[] ReadPixels(ReadOnlySpan<byte> data, DdsTexture texture, DdsReferenceSurface surface)
    {
        var format = texture.Format;
        if (format.BytesPerPixel == 0)
            throw new InvalidOperationException($"The reference reader does not decode {format.Name}.");

        var pixels = new double[surface.Width * surface.Height * 4];
        var bytes = data.Slice((int)surface.Offset, (int)surface.Length);
        for (var i = 0; i < surface.Width * surface.Height; i++)
        {
            format.Decode(bytes.Slice(i * format.BytesPerPixel, format.BytesPerPixel), pixels.AsSpan(i * 4, 4));
        }

        return pixels;
    }

    /// <summary>
    /// Gets the color labels the format of a file gives: the <c>_SRGB</c> DXGI formats are sRGB encoded, the floating-point
    /// formats (and the signed BC4 and BC5 formats, read as floats) are linear light, the others are untagged.
    /// </summary>
    public static (bool IsSrgb, bool IsLinear) ReadColorLabels(ReadOnlySpan<byte> data)
    {
        var pixelFlags = ReadUInt32(data, 80);
        var fourCC = ReadUInt32(data, 84);
        if ((pixelFlags & DdsFileBuilder.PixelFourCC) == 0)
            return (false, false);

        if (fourCC == DdsFileBuilder.FourCC("DX10"))
        {
            var format = ReadUInt32(data, 128);
            return (format is 29 or 72 or 75 or 78 or 91 or 93 or 99, format is 2 or 6 or 10 or 16 or 26 or 34 or 41 or 54 or 67 or 81 or 84 or 95 or 96);
        }

        return (false, fourCC is >= 111 and <= 116 || fourCC == DdsFileBuilder.FourCC("BC4S") || fourCC == DdsFileBuilder.FourCC("BC5S"));
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
}
