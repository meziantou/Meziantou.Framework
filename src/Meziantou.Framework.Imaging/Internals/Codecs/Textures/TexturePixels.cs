using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The working samples of one surface of a texture: RGBA of 8 bits, 16 bits or single-precision floats, row by row, in a
/// buffer charged to the allocation scope of the writer.
/// </summary>
internal sealed class TexturePixels : IDisposable
{
    private PooledBuffer? _buffer;

    private TexturePixels(int width, int height, TexturePrecision precision, PooledBuffer buffer)
    {
        Width = width;
        Height = height;
        Precision = precision;
        _buffer = buffer;
    }

    public int Width { get; }

    public int Height { get; }

    public TexturePrecision Precision { get; }

    /// <summary>Gets the length of a pixel, in bytes.</summary>
    public int BytesPerPixel => GetBytesPerPixel(Precision);

    private Span<byte> Data => (_buffer ?? throw new ObjectDisposedException(nameof(TexturePixels))).Span;

    public static int GetBytesPerPixel(TexturePrecision precision) => precision switch
    {
        TexturePrecision.Byte => 4,
        TexturePrecision.UInt16 => 8,
        _ => 16,
    };

    /// <summary>Gets the pixel format of the working samples.</summary>
    public static PixelFormat GetPixelFormat(TexturePrecision precision) => precision switch
    {
        TexturePrecision.Byte => PixelFormat.Rgba32,
        TexturePrecision.UInt16 => PixelFormat.Rgba64,
        _ => PixelFormat.RgbaFloat,
    };

    /// <summary>Creates an uninitialized surface.</summary>
    /// <exception cref="ImageResourceLimitException">The surface is larger than a buffer can be, or than the limits allow.</exception>
    public static TexturePixels Create(int width, int height, TexturePrecision precision, AllocationScope scope)
    {
        var length = (long)width * height * GetBytesPerPixel(precision);
        if (length > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(scope.Limits);

        return new TexturePixels(width, height, precision, scope.Rent((int)length, AllocationKind.Temporary, clear: false));
    }

    /// <summary>Converts the pixels of a frame to working samples.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="sourceFormat">The pixel format of the frame.</param>
    /// <param name="precision">The working samples.</param>
    /// <param name="scope">The scope charged for the samples.</param>
    /// <param name="cancellationToken">Checked between rows.</param>
    /// <returns>The surface, owned by the caller.</returns>
    public static TexturePixels Load(ImageFrame frame, PixelFormat sourceFormat, TexturePrecision precision, AllocationScope scope, CancellationToken cancellationToken)
    {
        var storage = frame.GetStorage();
        using var lease = storage.AcquireLease();
        var pixels = Create(lease.Width, lease.Height, precision, scope);
        try
        {
            var destinationFormat = GetPixelFormat(precision);
            var plan = sourceFormat == destinationFormat ? null : PixelConversionPlan.Create(sourceFormat, destinationFormat);
            var sourceRowLength = lease.Width * PixelFormats.GetBytesPerPixel(sourceFormat);
            for (var y = 0; y < lease.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = lease.GetRowBytes(y)[..sourceRowLength];
                var destination = pixels.GetRow(y);
                if (plan is null)
                {
                    source.CopyTo(destination);
                }
                else
                {
                    plan.ConvertRow(source, destination);
                }
            }

            return pixels;
        }
        catch
        {
            pixels.Dispose();
            throw;
        }
    }

    /// <summary>Gets the bytes of a row.</summary>
    public Span<byte> GetRow(int y) => Data.Slice(y * Width * BytesPerPixel, Width * BytesPerPixel);

    /// <summary>Reads a pixel as four floats: integer samples as fractions of their maximum.</summary>
    public void GetPixel(int x, int y, Span<float> rgba)
    {
        var row = GetRow(y);
        switch (Precision)
        {
            case TexturePrecision.Byte:
                for (var c = 0; c < 4; c++)
                {
                    rgba[c] = row[(x * 4) + c] / 255f;
                }

                break;

            case TexturePrecision.UInt16:
                var words = unsafe(MemoryMarshal.Cast<byte, ushort>(row));
                for (var c = 0; c < 4; c++)
                {
                    rgba[c] = words[(x * 4) + c] / 65535f;
                }

                break;

            default:
                var floats = unsafe(MemoryMarshal.Cast<byte, float>(row));
                floats.Slice(x * 4, 4).CopyTo(rgba);
                break;
        }
    }

    /// <summary>Gets the 8-bit samples of a row (<see cref="TexturePrecision.Byte"/>).</summary>
    public Span<byte> GetBytes(int y) => GetRow(y);

    /// <summary>Gets the 16-bit samples of a row (<see cref="TexturePrecision.UInt16"/>).</summary>
    public Span<ushort> GetWords(int y)
    {
        var words = unsafe(MemoryMarshal.Cast<byte, ushort>(GetRow(y)));
        return words;
    }

    /// <summary>Gets the float samples of a row (<see cref="TexturePrecision.Float"/>).</summary>
    public Span<float> GetFloats(int y)
    {
        var floats = unsafe(MemoryMarshal.Cast<byte, float>(GetRow(y)));
        return floats;
    }

    public void Dispose()
    {
        _buffer?.Dispose();
        _buffer = null;
    }
}
