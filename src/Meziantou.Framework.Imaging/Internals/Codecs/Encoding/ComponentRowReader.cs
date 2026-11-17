using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Reads the components of the rows of the pixel formats that the encoders of the formats storing gray, RGB or CMYK
/// components and an optional alpha (PSD, DNG, Cineon) receive: the color components of a pixel (gray; red, green and
/// blue; or cyan, magenta, yellow and black), then its straight alpha.
/// </summary>
/// <remarks>
/// Integer components are the values of the format (0 to 255, or 0 to 65,535); floating-point components are the values of
/// <see cref="PixelFormat.RgbaFloat"/> and of <see cref="PixelFormat.RgbaHalf"/> as single-precision numbers.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
internal readonly struct ComponentRowReader
{
    private readonly int _bytesPerPixel;
    private readonly int[] _offsets;

    /// <summary>Initializes a reader for a pixel format.</summary>
    /// <param name="format">The pixel format of the rows.</param>
    /// <exception cref="InvalidOperationException">The format is not one of the gray, RGB, RGBA and CMYK formats of 8 or 16-bit integers or of floating-point numbers.</exception>
    public ComponentRowReader(PixelFormat format)
    {
        Format = format;
        (ColorComponents, HasAlpha, Depth, _offsets) = GetLayout(format);
        _bytesPerPixel = PixelFormats.GetBytesPerPixel(format);
    }


    /// <summary>Gets the pixel format of the rows.</summary>
    public PixelFormat Format { get; }

    /// <summary>Gets the number of color components: 1 (gray), 3 (RGB) or 4 (CMYK).</summary>
    public int ColorComponents { get; }

    /// <summary>Gets a value indicating whether the format has an alpha component, the component after the colors.</summary>
    public bool HasAlpha { get; }

    /// <summary>Gets the number of bits of a component: 8 or 16 for integers, 32 for floating-point numbers.</summary>
    public int Depth { get; }

    /// <summary>Gets a value indicating whether the components are floating-point numbers.</summary>
    public bool IsFloatingPoint => Depth == 32;

    /// <summary>Gets the largest value of an integer component: 255 or 65,535 (1 for floating-point components).</summary>
    public int MaxValue => Depth switch
    {
        8 => byte.MaxValue,
        16 => ushort.MaxValue,
        _ => 1,
    };

    /// <summary>Gets the index of the alpha component.</summary>
    public int AlphaComponent => ColorComponents;

    /// <summary>Reads one integer component of every pixel of a row.</summary>
    /// <param name="row">The bytes of the row.</param>
    /// <param name="width">The number of pixels.</param>
    /// <param name="component">The component: a color, or <see cref="AlphaComponent"/>.</param>
    /// <param name="destination">The values, one per pixel.</param>
    public void ReadComponent(ReadOnlySpan<byte> row, int width, int component, Span<ushort> destination)
    {
        var offset = _offsets[component];
        var stride = _bytesPerPixel;
        switch (Depth)
        {
            case 8:
                for (var x = 0; x < width; x++)
                {
                    destination[x] = row[(x * stride) + offset];
                }

                break;

            case 16:
                for (var x = 0; x < width; x++)
                {
                    destination[x] = BitConverter.ToUInt16(row[((x * stride) + offset)..]);
                }

                break;

            default:
                throw new InvalidOperationException("The components of floating-point pixels are numbers.");
        }
    }

    /// <summary>Reads one component of every pixel of a row as numbers: the values of floating-point components, or the integers.</summary>
    /// <param name="row">The bytes of the row.</param>
    /// <param name="width">The number of pixels.</param>
    /// <param name="component">The component: a color, or <see cref="AlphaComponent"/>.</param>
    /// <param name="destination">The values, one per pixel.</param>
    public void ReadComponent(ReadOnlySpan<byte> row, int width, int component, Span<float> destination)
    {
        var offset = _offsets[component];
        var stride = _bytesPerPixel;
        switch (Format)
        {
            case PixelFormat.RgbaFloat:
                for (var x = 0; x < width; x++)
                {
                    destination[x] = BitConverter.ToSingle(row[((x * stride) + offset)..]);
                }

                break;

            case PixelFormat.RgbaHalf:
                for (var x = 0; x < width; x++)
                {
                    destination[x] = (float)BitConverter.ToHalf(row[((x * stride) + offset)..]);
                }

                break;

            default:
                if (Depth == 8)
                {
                    for (var x = 0; x < width; x++)
                    {
                        destination[x] = row[(x * stride) + offset];
                    }
                }
                else
                {
                    for (var x = 0; x < width; x++)
                    {
                        destination[x] = BitConverter.ToUInt16(row[((x * stride) + offset)..]);
                    }
                }

                break;
        }
    }

    /// <summary>Determines whether every pixel of a row is fully opaque (always for a format without alpha).</summary>
    /// <param name="row">The bytes of the row.</param>
    /// <param name="width">The number of pixels.</param>
    /// <returns><see langword="true"/> when the alpha of every pixel is its largest value (1 for floating-point pixels).</returns>
    public bool IsOpaque(ReadOnlySpan<byte> row, int width)
    {
        if (!HasAlpha)
            return true;

        var offset = _offsets[AlphaComponent];
        var stride = _bytesPerPixel;
        for (var x = 0; x < width; x++)
        {
            var position = (x * stride) + offset;
            var opaque = Format switch
            {
                PixelFormat.RgbaFloat => BitConverter.ToSingle(row[position..]) == 1f,
                PixelFormat.RgbaHalf => BitConverter.ToHalf(row[position..]) == Half.One,
                _ when Depth == 8 => row[position] == byte.MaxValue,
                _ => BitConverter.ToUInt16(row[position..]) == ushort.MaxValue,
            };

            if (!opaque)
                return false;
        }

        return true;
    }

    private static (int ColorComponents, bool HasAlpha, int Depth, int[] Offsets) GetLayout(PixelFormat format) => format switch
    {
        PixelFormat.Gray8 => (1, false, 8, [0]),
        PixelFormat.Gray16 => (1, false, 16, [0]),
        PixelFormat.La16 => (1, true, 8, [0, 1]),
        PixelFormat.La32 => (1, true, 16, [0, 2]),
        PixelFormat.Rgb24 => (3, false, 8, [0, 1, 2]),
        PixelFormat.Rgb48 => (3, false, 16, [0, 2, 4]),
        PixelFormat.Rgba32 => (3, true, 8, [0, 1, 2, 3]),
        PixelFormat.Bgra32 => (3, true, 8, [2, 1, 0, 3]),
        PixelFormat.Rgba64 => (3, true, 16, [0, 2, 4, 6]),
        PixelFormat.RgbaFloat => (3, true, 32, [0, 4, 8, 12]),
        PixelFormat.RgbaHalf => (3, true, 32, [0, 2, 4, 6]),
        PixelFormat.Cmyk32 => (4, false, 8, [0, 1, 2, 3]),
        PixelFormat.Cmyk64 => (4, false, 16, [0, 2, 4, 6]),
        _ => throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"The encoder does not read the components of {format} pixels.")),
    };
}
