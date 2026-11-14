using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>One validated color mask of a <c>BI_BITFIELDS</c> (or implicit <c>BI_RGB</c>) BMP layout.</summary>
/// <remarks>
/// A mask must be a single contiguous run of bits inside the pixel. For a channel of 1 to 8 bits, <see cref="Scale"/> holds
/// the exact expansion of every encoded value to 8 bits (<c>round(value * 255 / max)</c>), so a 5-bit channel maps 0 to 0
/// and 31 to 255 without a multiplication per sample. A layout with a channel wider than 8 bits is expanded to 16 bits
/// instead (<see cref="ReadWide"/>, <c>round(value * 65535 / max)</c>), every channel of it included.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
internal readonly struct BmpChannelMask
{
    private BmpChannelMask(uint mask, int shift, int bits, byte[]? scale)
    {
        Mask = mask;
        Shift = shift;
        Bits = bits;
        Scale = scale;
    }

    /// <summary>Gets the mask, or 0 when the channel is absent.</summary>
    public uint Mask { get; }

    /// <summary>Gets the position of the lowest mask bit.</summary>
    public int Shift { get; }

    /// <summary>Gets the number of mask bits (0 when the channel is absent).</summary>
    public int Bits { get; }

    /// <summary>Gets the expansion of every encoded value to 8 bits, or <see langword="null"/> when the channel is absent or wider than 8 bits.</summary>
    public byte[]? Scale { get; }

    /// <summary>Gets a value indicating whether the channel is present.</summary>
    public bool IsPresent => Mask != 0;

    /// <summary>Validates a mask and precomputes its 8-bit expansion.</summary>
    /// <param name="mask">The mask, or 0 for an absent channel.</param>
    /// <param name="name">The channel name, used in messages.</param>
    /// <param name="bitsPerPixel">The number of bits of one pixel.</param>
    /// <returns>The channel.</returns>
    /// <exception cref="InvalidImageContentException">The mask is not a contiguous run of bits inside the pixel.</exception>
    public static BmpChannelMask Create(uint mask, string name, int bitsPerPixel)
    {
        if (mask == 0)
            return default;

        var shift = BitOperations.TrailingZeroCount(mask);
        var bits = BitOperations.PopCount(mask);
        if ((mask >> shift) != (bits == 32 ? uint.MaxValue : (1u << bits) - 1))
            throw BmpFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The BMP {name} mask 0x{mask:X8} is not a contiguous run of bits."));

        if (shift + bits > bitsPerPixel)
            throw BmpFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The BMP {name} mask 0x{mask:X8} does not fit in a {bitsPerPixel}-bit pixel."));

        if (bits > 8)
            return new BmpChannelMask(mask, shift, bits, scale: null);

        var max = (1u << bits) - 1;
        var scale = new byte[max + 1];
        for (var value = 0u; value <= max; value++)
        {
            scale[value] = (byte)(((value * 255) + (max / 2)) / max);
        }

        return new BmpChannelMask(mask, shift, bits, scale);
    }

    /// <summary>Extracts and expands the channel of a pixel to 8 bits.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte Read(uint pixel) => Scale![(pixel & Mask) >> Shift];

    /// <summary>Extracts and expands the channel of a pixel to 16 bits: <c>round(value * 65535 / max)</c>.</summary>
    public ushort ReadWide(uint pixel)
    {
        var max = Bits == 32 ? uint.MaxValue : (1u << Bits) - 1;
        var value = (ulong)((pixel & Mask) >> Shift);
        return (ushort)(((value * 65535) + (max / 2)) / max);
    }
}
